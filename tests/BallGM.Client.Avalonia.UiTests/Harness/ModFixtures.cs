using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;

namespace BallGM.Client.Avalonia.UiTests.Harness;

/// <summary>
/// Writes the mod fixtures of spec 06 section 1.3 into a temporary folder at test time: league packs in
/// the game's own on-disk format (the UI league plus <c>logo</c> and <c>portrait</c> paths), with
/// images drawn here from simple generated shapes. Nothing is committed and no real-world imagery is
/// involved.
/// <para>
/// The game has one mod kind — the league pack, whose teams and players name their own image files —
/// so each fixture "pack" is a league pack. See docs/ui/AUDIT.md section 1 and Decisions.
/// </para>
/// </summary>
internal static class ModFixtures
{
    public enum Kind
    {
        /// <summary>The UI league with no image paths at all: every slot shows its fallback.</summary>
        None,

        /// <summary>Cutouts at 1:1 and 1.37:1, opaque JPEG portraits, logos for all 30 teams.</summary>
        Good,

        /// <summary>Zero-byte, corrupt, mislabelled, huge, extreme-aspect and missing files, and an escaping symlink.</summary>
        Broken,

        /// <summary>The good pack's logos plus at least 5,000 portraits (the roster grown with generated players).</summary>
        Stress,
    }

    public const int StressImageCount = 5_000;

    /// <summary>Builds the pack and returns the path of its pack file.</summary>
    public static string Build(Kind kind, string? root = null)
    {
        root ??= Path.Combine(Path.GetTempPath(), "ballgm-ui-mods-" + kind.ToString().ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var pack = JsonNode.Parse(File.ReadAllText(RepoPaths.UiLeaguePack))!.AsObject();
        pack["rulesetFile"] = Path.Combine(RepoPaths.Root, "data", "rulesets", "calibration-league.json");
        var teams = pack["teams"]!.AsArray();
        var players = pack["players"]!.AsArray();

        switch (kind)
        {
            case Kind.Good:
                WriteLogos(root, teams);
                WritePortraits(root, players, everyOther: false);
                break;
            case Kind.Broken:
                WriteLogos(root, teams);
                WriteBroken(root, teams, players);
                break;
            case Kind.Stress:
                WriteLogos(root, teams);
                GrowToStress(players);
                WritePortraits(root, players, everyOther: false, small: true);
                break;
        }

        var packPath = Path.Combine(root, "pack.json");
        File.WriteAllText(packPath, pack.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        return packPath;
    }

    private static void WriteLogos(string root, JsonArray teams)
    {
        Directory.CreateDirectory(Path.Combine(root, "logos"));
        foreach (var team in teams)
        {
            var key = team!["key"]!.GetValue<string>();
            var primary = SKColor.Parse(team["colours"]!["primary"]!.GetValue<string>());
            var secondary = SKColor.Parse(team["colours"]!["secondary"]!.GetValue<string>());
            var relative = $"logos/{key}.png";
            WritePng(Path.Combine(root, relative), 512, 512, canvas => DrawLogo(canvas, 512, 512, primary, secondary, key));
            team["logo"] = relative;
        }
    }

    private static void WritePortraits(string root, JsonArray players, bool everyOther, bool small = false)
    {
        Directory.CreateDirectory(Path.Combine(root, "faces"));
        var index = 0;
        foreach (var player in players)
        {
            index++;
            if (everyOther && index % 2 == 0)
            {
                continue;
            }

            var hue = (index * 47) % 360;
            string relative;
            switch (index % 3)
            {
                case 0:
                    relative = $"faces/p{index:D5}.png";
                    WritePng(Path.Combine(root, relative), small ? 64 : 512, small ? 64 : 512, canvas => DrawCutout(canvas, small ? 64 : 512, small ? 64 : 512, hue));
                    break;
                case 1:
                    relative = $"faces/p{index:D5}.png";
                    WritePng(Path.Combine(root, relative), small ? 88 : 700, small ? 64 : 512, canvas => DrawCutout(canvas, small ? 88 : 700, small ? 64 : 512, hue));
                    break;
                default:
                    relative = $"faces/p{index:D5}.jpg";
                    WriteJpeg(Path.Combine(root, relative), small ? 52 : 400, small ? 64 : 500, canvas => DrawPortraitPhoto(canvas, small ? 52 : 400, small ? 64 : 500, hue));
                    break;
            }

            player!["portrait"] = relative;
        }
    }

    private static void WriteBroken(string root, JsonArray teams, JsonArray players)
    {
        var faces = Path.Combine(root, "faces");
        Directory.CreateDirectory(faces);
        var rostered = players.Where(player => player!["team"] is not null).ToList();

        // A mapping that points at no file.
        rostered[0]!["portrait"] = "faces/missing.png";

        // A zero-byte file.
        File.WriteAllBytes(Path.Combine(faces, "empty.png"), []);
        rostered[1]!["portrait"] = "faces/empty.png";

        // Random bytes behind a PNG name.
        var random = new Random(42);
        var noise = new byte[4096];
        random.NextBytes(noise);
        File.WriteAllBytes(Path.Combine(faces, "corrupt.png"), noise);
        rostered[2]!["portrait"] = "faces/corrupt.png";

        // A JPEG named .png.
        WriteJpeg(Path.Combine(faces, "really-a-jpeg.png"), 400, 500, canvas => DrawPortraitPhoto(canvas, 400, 500, 200));
        rostered[3]!["portrait"] = "faces/really-a-jpeg.png";

        // 8000 x 8000.
        WritePng(Path.Combine(faces, "huge.png"), 8000, 8000, canvas => DrawCutout(canvas, 8000, 8000, 30));
        rostered[4]!["portrait"] = "faces/huge.png";

        // A 5:1 headshot (unsupported aspect for a photo).
        WritePng(Path.Combine(faces, "banner.png"), 1000, 200, canvas => DrawCutout(canvas, 1000, 200, 90));
        rostered[5]!["portrait"] = "faces/banner.png";

        // A 5:1 "logo".
        WritePng(Path.Combine(root, "logos", "wide.png"), 1000, 200, canvas => DrawLogo(canvas, 1000, 200, SKColors.SteelBlue, SKColors.White, "W"));
        teams[1]!["logo"] = "logos/wide.png";

        // A truncated PNG: a real header with the body cut off.
        var whole = EncodePng(256, 256, canvas => DrawCutout(canvas, 256, 256, 150));
        File.WriteAllBytes(Path.Combine(faces, "truncated.png"), whole[..(whole.Length / 3)]);
        rostered[6]!["portrait"] = "faces/truncated.png";

        // A symlink inside the pack that escapes it: the pack loader's string check passes it, so the
        // asset resolver has to catch it.
        var outside = Path.Combine(Path.GetTempPath(), "ballgm-outside-" + Guid.NewGuid().ToString("N") + ".png");
        WritePng(outside, 64, 64, canvas => DrawCutout(canvas, 64, 64, 10));
        try
        {
            File.CreateSymbolicLink(Path.Combine(faces, "escape.png"), outside);
            rostered[7]!["portrait"] = "faces/escape.png";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Symlinks need a privilege on some Windows machines; the path-safety test covers the case directly.
        }
    }

    /// <summary>Copies every rostered player and free agent under new names until there are enough portraits.</summary>
    private static void GrowToStress(JsonArray players)
    {
        var originals = players.Select(player => player!.DeepClone()).ToList();
        var copy = 0;
        while (players.Count < StressImageCount)
        {
            var clone = originals[copy % originals.Count]!.AsObject();
            var duplicate = clone.DeepClone().AsObject();
            duplicate["name"] = $"{clone["name"]!.GetValue<string>()} {copy / originals.Count + 2}";
            duplicate.Remove("team");
            duplicate.Remove("contract");
            players.Add(duplicate);
            copy++;
        }
    }

    private static void DrawLogo(SKCanvas canvas, int width, int height, SKColor primary, SKColor secondary, string mark)
    {
        canvas.Clear(SKColors.Transparent);
        var size = Math.Min(width, height);
        var centre = new SKPoint(width / 2f, height / 2f);
        using var fill = new SKPaint { Color = primary, IsAntialias = true };
        using var ring = new SKPaint { Color = secondary, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = size * 0.07f };
        canvas.DrawCircle(centre, size * 0.44f, fill);
        canvas.DrawCircle(centre, size * 0.44f, ring);
        using var stripe = new SKPaint { Color = secondary, IsAntialias = true };
        var path = new SKPath();
        path.MoveTo(centre.X - size * 0.30f, centre.Y + size * 0.18f);
        path.LineTo(centre.X + size * 0.12f, centre.Y - size * 0.30f);
        path.LineTo(centre.X + size * 0.30f, centre.Y - size * 0.30f);
        path.LineTo(centre.X - size * 0.12f, centre.Y + size * 0.18f);
        path.Close();
        canvas.DrawPath(path, stripe);
        using var font = new SKFont(SKTypeface.Default, size * 0.22f) { Embolden = true };
        using var ink = new SKPaint { Color = secondary.WithAlpha(255), IsAntialias = true };
        canvas.DrawText(mark, centre.X, centre.Y + size * 0.36f, SKTextAlign.Center, font, ink);
    }

    private static void DrawCutout(SKCanvas canvas, int width, int height, int hue)
    {
        canvas.Clear(SKColors.Transparent);
        var skin = SKColor.FromHsl(hue % 40 + 15, 45, 55);
        var jersey = SKColor.FromHsl(hue, 60, 40);
        using var body = new SKPaint { Color = jersey, IsAntialias = true };
        using var head = new SKPaint { Color = skin, IsAntialias = true };
        var cx = width / 2f;
        canvas.DrawRoundRect(new SKRect(cx - height * 0.42f, height * 0.62f, cx + height * 0.42f, height * 1.1f), height * 0.2f, height * 0.2f, body);
        canvas.DrawRect(new SKRect(cx - height * 0.09f, height * 0.48f, cx + height * 0.09f, height * 0.66f), head);
        canvas.DrawOval(new SKRect(cx - height * 0.17f, height * 0.16f, cx + height * 0.17f, height * 0.56f), head);
    }

    private static void DrawPortraitPhoto(SKCanvas canvas, int width, int height, int hue)
    {
        canvas.Clear(SKColor.FromHsl((hue + 180) % 360, 20, 70));
        DrawCutout(canvas, width, height, hue);
    }

    private static byte[] EncodePng(int width, int height, Action<SKCanvas> draw) => Encode(width, height, draw, SKEncodedImageFormat.Png, 100);

    private static void WritePng(string path, int width, int height, Action<SKCanvas> draw) =>
        File.WriteAllBytes(path, Encode(width, height, draw, SKEncodedImageFormat.Png, 100));

    private static void WriteJpeg(string path, int width, int height, Action<SKCanvas> draw) =>
        File.WriteAllBytes(path, Encode(width, height, draw, SKEncodedImageFormat.Jpeg, 85));

    private static byte[] Encode(int width, int height, Action<SKCanvas> draw, SKEncodedImageFormat format, int quality)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        draw(surface.Canvas);
        using var image = surface.Snapshot();
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }
}
