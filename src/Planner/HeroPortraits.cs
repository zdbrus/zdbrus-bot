using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Il2Cpp;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Firebot.Planner;

/// <summary>
///     Hero faces for the War Machine Planner, taken from your own game: whenever a hero tile is on screen (for
///     example the Engineer's "Select crew" list), its picture is saved once to
///     UserData/planner/heroes/HeroName.png and served to the page. No game art ships with the mod.
/// </summary>
public static class HeroPortraits
{
    private const string CrewPicker = "menusRoot/menuCanvasParent/SafeArea/menuCanvas/popups/SelectWarMachineHeroes";
    private static readonly string Dir = Path.Combine("UserData", "planner", "heroes");
    private static readonly HashSet<string> Have = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex SafeName = new("^[A-Za-z0-9_]{1,40}$");
    private static float _next;
    private static bool _indexed;
    private static string _lastError;

    public static void Tick()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 2f;
        // Only look while a hero list is actually on screen (the crew picker), so this costs nothing otherwise.
        var picker = GameObject.Find(CrewPicker);
        if (picker == null || !picker.activeInHierarchy) return;
        Index();

        try
        {
            var found = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<HeroInteractionSquare>());
            if (found == null) return;
            foreach (var obj in found)
            {
                var square = obj?.TryCast<HeroInteractionSquare>();
                if (square == null || square.isEmpty) continue;
                var hero = square.hero;
                var image = square.heroImage;
                if (hero == null || image == null || image.sprite == null) continue;
                var code = hero.heroCode.ToString();
                if (Have.Contains(code) || !SafeName.IsMatch(code)) continue;
                if (Save(image.sprite, code))
                {
                    Have.Add(code);
                    Core.Logger.Info($"[Planner] Saved face: {code}");
                }
            }
        }
        catch (Exception e)
        {
            var msg = $"{e.GetType().Name}: {e.Message}";
            if (msg != _lastError) Core.Logger.Warning($"[Planner] Face capture failed: {msg}");
            _lastError = msg;
        }
    }

    /// <summary>PNG bytes for a hero, or null when that face has not been seen yet.</summary>
    public static byte[] Get(string code)
    {
        if (string.IsNullOrEmpty(code) || !SafeName.IsMatch(code)) return null;
        var path = Path.Combine(Dir, code + ".png");
        try { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        catch { return null; }
    }

    private static void Index()
    {
        if (_indexed) return;
        _indexed = true;
        try
        {
            Directory.CreateDirectory(Dir);
            foreach (var f in Directory.GetFiles(Dir, "*.png")) Have.Add(Path.GetFileNameWithoutExtension(f));
        }
        catch { /* the folder is created again on the first save */ }
    }

    /// <summary>Copies the sprite's area of its (usually unreadable) atlas through a temporary render target.</summary>
    private static bool Save(Sprite sprite, string code)
    {
        var tex = sprite.texture;
        if (tex == null) return false;
        var r = sprite.textureRect;
        var w = Mathf.RoundToInt(r.width);
        var h = Mathf.RoundToInt(r.height);
        if (w < 8 || h < 8 || w > 1024 || h > 1024) return false;

        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        Texture2D copy = null;
        try
        {
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
            copy.Apply();
            byte[] png = ImageConversion.EncodeToPNG(copy);
            if (png == null || png.Length < 100) return false;
            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(Path.Combine(Dir, code + ".png"), png);
            return true;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            if (copy != null) UnityEngine.Object.Destroy(copy);
        }
    }
}
