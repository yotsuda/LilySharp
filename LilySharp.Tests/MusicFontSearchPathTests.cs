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

using System;
using System.IO;
using System.Linq;
using LilySharp.Core.Rendering;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A score names its music font only; the MACHINE says where fonts are (owner's decision
/// 2026-10-09): the editor's <c>lilysharp.fontPaths</c> (<see cref="MusicFonts.SetSearchDirectories"/>)
/// and the environment's <c>LILYSHARP_FONT_PATH</c> are searched after the bundle and before the
/// SMuFL locations. The fixture is the bundled Bravura under another name, "Testura", which no
/// standard location holds.
/// </summary>
// The search list and the variable are process-wide, and changing the list FORGETS every
// resolved font (MusicFonts.ByName / Chains) — so this class runs alone, never beside a render.
// MEASURED (session 871): beside it, SmuflChordSymbolTests.TheCompanionFace_TravelsWithThePage
// failed once in a full run (its PDF lost "Petaluma Script") and passed on every rerun alone.
[Collection(nameof(MusicFontSearchPathTests))]
public sealed class MusicFontSearchPathTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lilysharp-fontpath-" + Guid.NewGuid().ToString("N"));

    public MusicFontSearchPathTests()
    {
        string bundle = FontLocator.Find() ?? throw new InvalidOperationException("no bundled fonts");
        string metadata = Directory.EnumerateFiles(bundle, "*_metadata.json")
            .First(f => Path.GetFileName(f).StartsWith("bravura", StringComparison.OrdinalIgnoreCase));
        // The font's own folder, the SMuFL layout: <dir>/Testura/testura_metadata.json + Testura.otf.
        string fontDir = Directory.CreateDirectory(Path.Combine(_dir, "Testura")).FullName;
        File.WriteAllText(Path.Combine(fontDir, "testura_metadata.json"),
            File.ReadAllText(metadata).Replace("\"fontName\": \"Bravura\"", "\"fontName\": \"Testura\""));
        File.Copy(Path.Combine(bundle, "Bravura.otf"), Path.Combine(fontDir, "Testura.otf"));
    }

    public void Dispose()
    {
        MusicFonts.SetSearchDirectories([]);
        Environment.SetEnvironmentVariable(MusicFonts.FontPathVariable, null);
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void AFontInAnEditorFolder_IsFoundByName()
    {
        Assert.Null(MusicFonts.Find("Testura", out _));
        Assert.True(MusicFonts.SetSearchDirectories([_dir]));

        var font = MusicFonts.Find("Testura", out _);

        Assert.NotNull(font);
        Assert.Equal("Testura", font!.Name);
        Assert.False(MusicFonts.SetSearchDirectories([_dir]));   // the same list is no change
    }

    [Fact]
    public void AFontInTheEnvironmentsFolders_IsFoundByName()
    {
        Environment.SetEnvironmentVariable(MusicFonts.FontPathVariable,
            Path.Combine(_dir, "nowhere") + Path.PathSeparator + _dir);

        Assert.Equal("Testura", MusicFonts.Find("Testura", out _)?.Name);
    }

    [Fact]
    public void AFontNotFound_NamesTheUserFoldersAmongThePlacesLooked()
    {
        MusicFonts.SetSearchDirectories([_dir]);

        Assert.Null(MusicFonts.Find("Nowhere", out var tried));
        Assert.Contains(tried, t => t.StartsWith(_dir, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Runs <see cref="MusicFontSearchPathTests"/> with no other test in parallel.</summary>
[CollectionDefinition(nameof(MusicFontSearchPathTests), DisableParallelization = true)]
public sealed class MusicFontSearchPathCollection { }
