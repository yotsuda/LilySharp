// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using LilySharp.Core.Rendering;

namespace LilySharp.Core.Svg.Layout;

/// <summary>
/// Finds a music font by the name a score writes (<c>fonts { music "…" }</c>): Emmentaler by
/// its name, else a SMuFL font by its metadata — the bundled ones first, then the places the
/// SMuFL specification says a font's metadata is installed (docs/smufl-design.md §1).
/// </summary>
/// <remarks>
/// The resolution order is "試して無ければ次" — no spelling rule guesses where a file is; every
/// place looked is returned, so a failure can name them all. A font found once is kept.
/// <para>
/// SMuFL 1.4, "Font metadata locations": Windows <c>%COMMONPROGRAMFILES%\SMuFL\Fonts\&lt;name&gt;\&lt;name&gt;.json</c>
/// and per user <c>%LOCALAPPDATA%\SMuFL\Fonts\…</c>; macOS <c>/Library/Application Support/SMuFL/Fonts/…</c>
/// and <c>~/Library/…</c>; Linux <c>$XDG_DATA_DIRS</c> and <c>$XDG_DATA_HOME</c> with
/// <c>SMuFL/Fonts/…</c>. The font program is looked for next to the metadata, in the bundle, and
/// in the system font directories.
/// </para>
/// </remarks>
public static class MusicFonts
{
    // Keyed by the metadata file: one font per file, however it was asked for.
    private static readonly ConcurrentDictionary<string, Lazy<SmuflMusicFont?>> Loaded =
        new(StringComparer.OrdinalIgnoreCase);

    // A name's answer, found or not, so a plan that names a missing font does not probe the
    // file system on every layout and render (Find does, by design, for the diagnostic).
    private static readonly ConcurrentDictionary<string, MusicFont?> ByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The music font a score's plan engraves in: the names of
    /// <see cref="Rendering.TextFontPlan.Music"/> that are found, else Emmentaler (the default,
    /// and the answer for a chain in which nothing is found — the diagnostic on the plan has
    /// said so). The scope every layout and render opens
    /// (<see cref="MusicFont.Use"/>; docs/smufl-design.md §6 ② ⒝).
    /// </summary>
    /// <remarks>
    /// A SMuFL font is answered as a <see cref="MusicFontChain"/> of every name found, in the
    /// order written, with Emmentaler last — the glyph-level fallback (§6 ② ⒟): no SMuFL font
    /// draws Emmentaler's own glyphs (<c>feta.</c>), and Leland lacks 25 more. One chain per
    /// list of names, kept: the caches keyed by a font are keyed by the instance.
    /// </remarks>
    internal static MusicFont Of(Rendering.TextFontPlan plan)
    {
        if (plan.Music.IsDefaultOrEmpty)
            return EmmentalerMusicFont.Instance;
        var found = new List<MusicFont>();
        foreach (var name in plan.Music)
            if (ByName.GetOrAdd(name, static n => Find(n, out _)) is { } font)
                found.Add(font);
        if (found.Count == 0 || ReferenceEquals(found[0], EmmentalerMusicFont.Instance))
            return EmmentalerMusicFont.Instance;
        string key = string.Join("\n", found.Select(f => f.Name));
        return Chains.GetOrAdd(key, static (_, fonts) => new MusicFontChain(fonts), found);
    }

    private static readonly ConcurrentDictionary<string, MusicFontChain> Chains = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The font named <paramref name="name"/>, or null when none can be found —
    /// <paramref name="tried"/> then lists every place looked, for the diagnostic.
    /// </summary>
    internal static MusicFont? Find(string name, out IReadOnlyList<string> tried)
    {
        var looked = new List<string>();
        tried = looked;
        if (string.IsNullOrWhiteSpace(name))
            return null;
        name = name.Trim();
        if (string.Equals(name, EmmentalerMusicFont.Instance.Name, StringComparison.OrdinalIgnoreCase))
            return EmmentalerMusicFont.Instance;

        foreach (var metadata in MetadataCandidates(name))
        {
            looked.Add(metadata);
            if (!File.Exists(metadata))
                continue;
            var font = Loaded.GetOrAdd(Path.GetFullPath(metadata),
                static m => new Lazy<SmuflMusicFont?>(() => Load(m), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
            if (font is null)
            {
                looked.Add($"{metadata} (metadata read, but no font program named {NameInMetadata(metadata)} was found)");
                continue;
            }
            if (string.Equals(font.Name, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileNameWithoutExtension(metadata), name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileNameWithoutExtension(metadata), name + "_metadata", StringComparison.OrdinalIgnoreCase))
                return font;
        }
        return null;
    }

    /// <summary>The names a score can write without installing anything: Emmentaler and
    /// every font whose metadata is bundled, by the name the metadata states (the file's
    /// stem when it cannot be read).</summary>
    public static IReadOnlyList<string> BundledNames()
    {
        var names = new List<string> { EmmentalerMusicFont.Instance.Name };
        if (FontLocator.Find() is { } bundle)
            foreach (var f in ExistingFiles(bundle, "*_metadata.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                names.Add(NameInMetadata(f));
        return names;
    }

    private static string NameInMetadata(string metadata)
    {
        try { return SmuflMetadata.Parse(File.ReadAllText(metadata)).FontName; }
        catch { return Path.GetFileNameWithoutExtension(metadata); }
    }

    /// <summary>Every metadata file the name could mean, in resolution order; a file that does
    /// not exist is still listed (it is a place looked).</summary>
    private static IEnumerable<string> MetadataCandidates(string name)
    {
        // ⑴ The bundle: <name>_metadata.json (the distributions' spelling, lower case) and
        // <Name>.json (Bravura's own redist spelling), matched without regard to case.
        if (FontLocator.Find() is { } bundle)
        {
            yield return Path.Combine(bundle, name.ToLowerInvariant() + "_metadata.json");
            yield return Path.Combine(bundle, name + ".json");
            foreach (var f in ExistingFiles(bundle, "*_metadata.json"))
                if (string.Equals(Path.GetFileName(f), name + "_metadata.json", StringComparison.OrdinalIgnoreCase))
                    yield return f;
        }
        // ⑵ The SMuFL specification's locations.
        foreach (var dir in SmuflFontDirectories())
        {
            var fontDir = Path.Combine(dir, name);
            yield return Path.Combine(fontDir, name + ".json");
            yield return Path.Combine(fontDir, name + "_metadata.json");
            yield return Path.Combine(fontDir, name.ToLowerInvariant() + "_metadata.json");
        }
    }

    private static IEnumerable<string> SmuflFontDirectories()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "SMuFL", "Fonts");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SMuFL", "Fonts");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/Library/Application Support/SMuFL/Fonts";
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "SMuFL", "Fonts");
        }
        else
        {
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrEmpty(dataHome))
                dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            yield return Path.Combine(dataHome, "SMuFL", "Fonts");
            var dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
            if (string.IsNullOrEmpty(dataDirs))
                dataDirs = "/usr/local/share:/usr/share";
            foreach (var d in dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries))
                yield return Path.Combine(d, "SMuFL", "Fonts");
        }
    }

    /// <summary>Reads the font at <paramref name="metadataPath"/>: its metadata, and the font
    /// program named in it (next to the metadata, in the bundle, or installed); null when the
    /// program cannot be found — a font the engine cannot draw is no font.</summary>
    private static SmuflMusicFont? Load(string metadataPath)
    {
        SmuflMetadata meta;
        try
        {
            meta = SmuflMetadata.Parse(File.ReadAllText(metadataPath));
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or IOException)
        {
            return null;
        }
        var dir = Path.GetDirectoryName(Path.GetFullPath(metadataPath))!;
        string? program = FindFontProgram(meta.FontName, dir);
        if (program is null)
            return null;
        string? web = FindFile(Path.GetDirectoryName(program)!, meta.FontName + ".woff2");
        return new SmuflMusicFont(meta, program, web);
    }

    private static string? FindFontProgram(string fontName, string metadataDir)
    {
        var dirs = new List<string> { metadataDir };
        if (FontLocator.Find() is { } bundle)
            dirs.Add(bundle);
        dirs.AddRange(SystemFontDirectories());
        foreach (var dir in dirs)
            foreach (var ext in new[] { ".otf", ".ttf" })
                if (FindFile(dir, fontName + ext) is { } found)
                    return found;
        return null;
    }

    private static IEnumerable<string> SystemFontDirectories()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Fonts");
            yield return "/Library/Fonts";
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(home, ".fonts");
            yield return Path.Combine(home, ".local", "share", "fonts");
            yield return "/usr/local/share/fonts";
            yield return "/usr/share/fonts";
        }
    }

    /// <summary>A file by name in <paramref name="dir"/> (recursively, for the system font
    /// trees), matched without regard to case — a Linux file system is case-sensitive and a
    /// score is not.</summary>
    private static string? FindFile(string dir, string fileName)
    {
        if (!Directory.Exists(dir))
            return null;
        var direct = Path.Combine(dir, fileName);
        if (File.Exists(direct))
            return direct;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                if (string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase))
                    return f;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    private static IEnumerable<string> ExistingFiles(string dir, string pattern)
    {
        if (!Directory.Exists(dir))
            return [];
        try { return Directory.EnumerateFiles(dir, pattern).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }
}
