using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

namespace Firebot.Planner;

/// <summary>
///     Serves the War Machine Planner at http://localhost:5577 on this PC only (bound to 127.0.0.1, never reachable
///     from the network). Two routes: "/" is the planner page built into the mod, "/roster.json" is the latest
///     <see cref="RosterExport" /> snapshot.
///     <para>
///         The listener thread only copies strings; it never touches the game, which keeps Il2Cpp objects on the main
///         thread. F4 opens the page in your browser.
///     </para>
/// </summary>
public static class PlannerServer
{
    public const int Port = 5577;
    public static KeyCode Key => KeyCode.F4;
    public static string Url => $"http://localhost:{Port}/";

    private static TcpListener _listener;
    private static byte[] _page;
    private static long _lastRequestTicks;
    private static readonly List<Pending> Open = new();

    private sealed class Pending
    {
        public TcpClient Client;
        public float Since;
        public readonly MemoryStream Head = new();
    }

    /// <summary>True while a planner tab has asked for data in the last 20 seconds.</summary>
    public static bool HasClients => DateTime.UtcNow.Ticks - _lastRequestTicks < TimeSpan.FromSeconds(20).Ticks;

    public static void Start()
    {
        if (_listener != null) return;
        try
        {
            _page = LoadPage();
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
            Core.Logger.Info($"[Planner] Ready at {Url} (F4 opens it).");
        }
        catch (Exception e)
        {
            _listener = null;
            Core.Logger.Warning($"[Planner] Could not start on port {Port}: {e.Message}");
        }
    }

    /// <summary>Closes the port and any open requests so nothing keeps the game process alive after quitting.</summary>
    public static void Stop()
    {
        foreach (var p in Open) { try { p.Client.Close(); } catch { } }
        Open.Clear();
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    /// <summary>
    ///     Everything runs here, on the game's main thread, with no background thread at all (a background web
    ///     thread is what crashed the game before). Each call only does work that is already waiting: it never
    ///     blocks, so a quiet planner costs nothing.
    /// </summary>
    public static void Tick()
    {
        if (Input.GetKeyDown(Key))
        {
            _lastRequestTicks = DateTime.UtcNow.Ticks; // start reading right away
            Application.OpenURL(Url);
        }
        RosterExport.Tick();
        HeroPortraits.Tick();
        if (_listener == null) return;

        try
        {
            while (_listener.Pending() && Open.Count < 8)
            {
                var c = _listener.AcceptTcpClient();
                c.NoDelay = true;
                c.SendTimeout = 1000;
                Open.Add(new Pending { Client = c, Since = Time.unscaledTime });
            }
        }
        catch (Exception e) { Core.Logger.Debug($"[Planner] accept: {e.Message}"); }

        for (var i = Open.Count - 1; i >= 0; i--)
        {
            var p = Open[i];
            var done = true;
            try { done = Serve(p); }
            catch { /* a bad request is simply dropped */ }
            if (!done && Time.unscaledTime - p.Since < 3f) continue;
            try { p.Client.Close(); } catch { }
            Open.RemoveAt(i);
        }
    }

    /// <summary>Reads what has arrived; answers once the request head is complete. True when finished with it.</summary>
    private static bool Serve(Pending p)
    {
        var stream = p.Client.GetStream();
        var buf = new byte[2048];
        while (stream.DataAvailable && p.Head.Length < 16384)
        {
            var n = stream.Read(buf, 0, buf.Length);
            if (n <= 0) return true;
            p.Head.Write(buf, 0, n);
        }
        var text = Encoding.ASCII.GetString(p.Head.GetBuffer(), 0, (int)p.Head.Length);
        if (!text.Contains("\r\n\r\n")) return false; // wait for the rest

        var first = text.Substring(0, text.IndexOf("\r\n", StringComparison.Ordinal));
        var parts = first.Split(' ');
        var path = parts.Length > 1 ? parts[1] : "/";
        var q = path.IndexOf('?');
        if (q >= 0) path = path[..q];

        if (parts[0] != "GET") { Send(stream, 405, "text/plain", Encoding.ASCII.GetBytes("GET only")); return true; }
        switch (path)
        {
            case "/":
            case "/index.html":
                Send(stream, 200, "text/html; charset=utf-8", _page);
                break;
            case "/roster.json":
                _lastRequestTicks = DateTime.UtcNow.Ticks;
                var json = RosterExport.Json;
                if (json == null) Send(stream, 503, "application/json", Encoding.UTF8.GetBytes("{\"waiting\":true}"));
                else Send(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                break;
            case "/apply":
                var plan = SquadApplier.Parse(Query(text, "plan"));
                var answer = SquadApplier.Request(plan);
                Send(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(
                    "{\"result\":\"" + answer.Replace("\"", "'") + "\"}"));
                break;
            case var face when face.StartsWith("/hero/", StringComparison.Ordinal) && face.EndsWith(".png", StringComparison.Ordinal):
                var png = HeroPortraits.Get(face.Substring(6, face.Length - 10));
                if (png == null) Send(stream, 404, "text/plain", Encoding.ASCII.GetBytes("No face yet"));
                else Send(stream, 200, "image/png", png);
                break;
            default:
                Send(stream, 404, "text/plain", Encoding.ASCII.GetBytes("Not found"));
                break;
        }
        return true;
    }

    /// <summary>One value from the request line's query string, URL-decoded.</summary>
    private static string Query(string request, string key)
    {
        var line = request.Substring(0, Math.Max(0, request.IndexOf("\r\n", StringComparison.Ordinal)));
        var q = line.IndexOf('?');
        if (q < 0) return null;
        var end = line.IndexOf(' ', q);
        var query = end > q ? line.Substring(q + 1, end - q - 1) : line.Substring(q + 1);
        foreach (var pair in query.Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0 && pair.Substring(0, eq) == key) return Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
        }
        return null;
    }

    private static void Send(Stream s, int code, string type, byte[] body)
    {
        var status = code switch { 200 => "OK", 404 => "Not Found", 405 => "Method Not Allowed", _ => "Service Unavailable" };
        var head = $"HTTP/1.1 {code} {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\n" +
                   "Cache-Control: no-store\r\nConnection: close\r\n\r\n";
        var h = Encoding.ASCII.GetBytes(head);
        s.Write(h, 0, h.Length);
        s.Write(body, 0, body.Length);
        s.Flush();
    }

    private static byte[] LoadPage()
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames())
            if (name.EndsWith("planner.html", StringComparison.OrdinalIgnoreCase))
            {
                using var rs = asm.GetManifestResourceStream(name);
                using var ms = new MemoryStream();
                rs!.CopyTo(ms);
                return ms.ToArray();
            }
        return Encoding.UTF8.GetBytes("<h1>Planner page missing from this build.</h1>");
    }
}
