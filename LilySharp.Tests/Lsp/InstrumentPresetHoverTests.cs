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
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;
using Position = LilySharp.Lsp.Protocol.Position;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// A part's <c>instrument</c> preset hovers as the defaults it fills in (owner's request
/// 2026-10-24): clef, octave, transposition, tuning and MIDI sound, the line a property of the
/// part replaces struck through — and not the name printed beside the staff.
/// </summary>
public class InstrumentPresetHoverTests
{
    private static string? HoverAt(string doc, string needle, int into = 1)
    {
        var server = new LilySharpLanguageServer(Stream.Null, Stream.Null);
        var uri = new Uri("file:///instrument-hover.lys");
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, Text = doc, Version = 1, LanguageId = "lilysharp" },
        });
        int at = doc.IndexOf(needle, StringComparison.Ordinal) + into;
        int line = doc[..at].Count(ch => ch == '\n');
        int col = at - (doc.LastIndexOf('\n', at - 1) + 1);
        return server.Hover(new TextDocumentPositionParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Position = new Position(line, col),
        })?.Contents.Value;
    }

    private static string Book(string header) => $"part b {{ {header} }}\nsection A {{ b {{ c4 d e f }} }}\n";

    [Fact]
    public void ABass_ListsWhatItSets_WrittenAndSoundingApart()
    {
        string? hover = HoverAt(Book("instrument bass"), "bass");
        Assert.NotNull(hover);
        Assert.StartsWith("**Instrument preset bass** — ", hover);
        Assert.Contains("- `clef bass`", hover);
        Assert.Contains("- `octave 3` — a bare `c` is written C3", hover);
        Assert.Contains("- `transposition 8vb` — sounds 12 semitones lower: written C3 sounds C2", hover);
        // Every line starts with a code span, which VS Code pads: a plain start stood out of the column.
        Assert.All(hover.Split('\n').Where(l => l.StartsWith("- ")), l => Assert.Matches(@"^- (~~)?`", l));
        Assert.Contains("- `tuning bass` — E1 A1 D2 G2", hover);
        Assert.Contains("- `midiInstrument \"electric bass (finger)\"`", hover);
        Assert.DoesNotContain("Bass", hover.Replace("bass", ""));   // not the printed name
    }

    [Fact]
    public void TheWordInstrument_HoversTheSame()
        => Assert.Equal(HoverAt(Book("instrument bass"), "bass"), HoverAt(Book("instrument bass"), "instrument"));

    [Fact]
    public void APropertyOfThePart_StrikesTheLineItReplaces()
    {
        string? hover = HoverAt(Book("instrument bass clef treble octave 2 midiInstrument \"fretless bass\""), "bass");
        Assert.NotNull(hover);
        Assert.Contains("- ~~`clef bass`~~ — overridden: the part sets `clef treble` itself", hover);
        Assert.Contains("- ~~`octave 3` — a bare `c` is written C3~~ — overridden: the part sets `octave 2` itself", hover);
        Assert.Contains("- ~~`midiInstrument \"electric bass (finger)\"`~~ — overridden: the part sets `midiInstrument \"fretless bass\"` itself", hover);
        Assert.Contains("- `tuning bass`", hover);   // not written: still the preset's
    }

    [Fact]
    public void AnExplicitTuning_SaysItOverridesThePresets()
        // Owner's request 2026-10-24: say that the written tuning overrode the preset's.
        => Assert.Contains("- ~~`tuning bass` — E1 A1 D2 G2 (the strings a tab frets on)~~ — overridden: the part sets `tuning bass5` itself",
            HoverAt(Book("instrument bass tuning bass5"), "bass"));

    [Fact]
    public void AnOctaveMode_DoesNotReplaceTheRegister()
        => Assert.Contains("- `octave 3`", HoverAt(Book("instrument bass octave absolute"), "bass"));

    [Fact]
    public void AChromaticTransposer_NamesTheSoundingPitch()
    {
        string? hover = HoverAt(Book("instrument clarinet"), "clarinet");
        Assert.Contains("- `transposition` — sounds 2 semitones lower: written C4 sounds B♭3", hover);
        Assert.DoesNotContain("tuning", hover);   // no strings to fret
    }

    [Fact]
    public void AHyphenatedPreset_AndOneThatSoundsAsWritten()
    {
        string? hover = HoverAt(Book("instrument piano-left"), "piano");
        Assert.StartsWith("**Instrument preset piano-left** — ", hover);
        Assert.Contains("- `transposition` — none, sounds as written", hover);
    }

    [Fact]
    public void ANameThatIsNotAPreset_HasNoPresetHover()
        => Assert.DoesNotContain("Instrument preset", HoverAt(Book("instrument \"1st Violin\""), "1st") ?? "");
}
