using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
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
    private static Thread _thread;
    private static byte[] _page;
    private static long _lastRequestTicks;

    /// <summary>True while a planner tab has asked for data in the last 20 seconds.</summary>
    public static bool HasClients => DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastRequestTicks) < TimeSpan.FromSeconds(20).Ticks;

    public static void Start()
    {
        if (_listener != null) return;
        try
        {
            _page = LoadPage();
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
            _thread = new Thread(Loop) { IsBackground = true, Name = "ZdbrusPlanner" };
            _thread.Start();
            Core.Logger.Info($"[Planner] Ready at {Url} (F4 opens it).");
        }
        catch (Exception e)
        {
            _listener = null;
            Core.Logger.Warning($"[Planner] Could not start on port {Port}: {e.Message}");
        }
    }

    public static void Tick()
    {
        if (Input.GetKeyDown(Key))
        {
            Interlocked.Exchange(ref _lastRequestTicks, DateTime.UtcNow.Ticks); // start reading right away
            Application.OpenURL(Url);
        }
        RosterExport.Tick();
    }

    private static void Loop()
    {
        while (true)
        {
            TcpClient client;
            try { client = _listener.AcceptTcpClient(); }
            catch { return; } // listener stopped
            try { Handle(client); }
            catch { /* one bad request must not stop the server */ }
            finally { client.Close(); }
        }
    }

    private static void Handle(TcpClient client)
    {
        client.ReceiveTimeout = 3000;
        client.SendTimeout = 3000;
        var stream = client.GetStream();
        var reader = new StreamReader(stream, Encoding.ASCII);
        var first = reader.ReadLine();
        if (string.IsNullOrEmpty(first)) return;
        string line;
        while (!string.IsNullOrEmpty(line = reader.ReadLine())) { } // skip headers

        var parts = first.Split(' ');
        var path = parts.Length > 1 ? parts[1] : "/";
        var q = path.IndexOf('?');
        if (q >= 0) path = path[..q];

        if (parts[0] != "GET") { Send(stream, 405, "text/plain", Encoding.ASCII.GetBytes("GET only")); return; }

        switch (path)
        {
            case "/":
            case "/index.html":
                Send(stream, 200, "text/html; charset=utf-8", _page);
                break;
            case "/roster.json":
                Interlocked.Exchange(ref _lastRequestTicks, DateTime.UtcNow.Ticks);
                var json = RosterExport.Json;
                if (json == null) Send(stream, 503, "application/json", Encoding.UTF8.GetBytes("{\"waiting\":true}"));
                else Send(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
                break;
            default:
                Send(stream, 404, "text/plain", Encoding.ASCII.GetBytes("Not found"));
                break;
        }
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
