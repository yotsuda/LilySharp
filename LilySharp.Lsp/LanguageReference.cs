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

using LilySharp.Core.Syntax;

namespace LilySharp.Lsp;

/// <summary>
/// THE ONE HOME of the language-reference prose the LSP shows for Lily#'s own
/// constructs: the signature-help table and the hover text live here, side by
/// side, so a grammar change is a change to one file — not three features
/// drifting apart. Before this fold, SignatureHelp advertised a <c>relative</c>
/// entry for grammar the parser rejects (removed 2026-08-26); the drift class
/// is closed structurally by <see cref="SignatureEntry.Sample"/>: every
/// signature row carries a compilable snippet of the grammar it advertises, and
/// LanguageReferenceTests parses each one — dead grammar cannot sit in this
/// table without a red test.
/// </summary>
/// <remarks>
/// The COMPLETION items' Detail strings deliberately stay with their context
/// arrays in Completion.cs: they are context-TUNED, not copies ("This section's
/// tempo (BPM)" in a section body vs "Change tempo (BPM)" mid-music vs "Tempo
/// (BPM)" at top level), and folding them here would erase that tuning. What
/// binds completion to the grammar is CompletionVocabularyTests' compile-proof
/// convention, the same proof Sample gives this table.
/// </remarks>
internal static class LanguageReference
{
    /// <summary>One signature-help row.</summary>
    /// <param name="Keyword">Token that triggers the signature (match-priority
    /// order = array order; the first keyword found on the line wins).</param>
    /// <param name="Label">The signature line shown in the help popup.</param>
    /// <param name="Documentation">Prose under the signature line.</param>
    /// <param name="Parameters">Per-parameter label + prose.</param>
    /// <param name="Sample">A compilable snippet using the advertised grammar —
    /// the drift net's probe (see the class remarks).</param>
    /// <param name="HoverMarker">The bold head of the construct's hover text
    /// (e.g. <c>**Tuplet**</c>); the net requires hovering somewhere in
    /// <paramref name="Sample"/> to produce it, so a construct cannot have a
    /// signature here and lose its hover (or vice versa) silently.</param>
    internal sealed record SignatureEntry(
        string Keyword, string Label, string Documentation,
        (string Label, string Documentation)[] Parameters,
        string Sample, string HoverMarker);

    // Keyword → signature, in match-priority order (the first keyword found on
    // the line wins). Adding a keyword's help is one table row.
    internal static readonly SignatureEntry[] Signatures =
    {
        // The kinds are joined from the compiler's list (SyntaxFacts.RepeatKindVocabulary),
        // not spelled here — this row carried its own copy of the three until 2026-09-03.
        new("repeat", $"repeat ({string.Join("|", LilySharp.Core.Semantics.LanguageVocabulary.RepeatKinds)}) count {{ music }}",
            "A tremolo: the music alternated count times. A percent repeat is written after the "
                + "bars it repeats, one mark a repetition: 'c4 d e f | % | % |', 'a2 b | c d | %2 |'. "
                + "A volta repeat is written in the form: '|: A :|' (count ':|*N'), with endings "
                + "'|: A [1. B] :| [2. C]'.",
            new[] { (string.Join("|", LilySharp.Core.Semantics.LanguageVocabulary.RepeatKinds), "Repeat kind (volta is the symbolic |: :| form, not this keyword)"),
                    ("count", "Number of strokes (integer)"),
                    ("{ music }", "The note or the two notes alternated") },
            Sample: "part melody\nsection A { melody {\nrepeat tremolo 4 { c16 e } r2 |\n} }\nform { ~A }\nscore { staff melody }",
            HoverMarker: "**Repeat**"),
        new("tempo", "tempo \"marking\" duration = bpm",
            "Sets the tempo for playback.",
            new[] { ("\"marking\"", "Optional tempo marking (e.g., \"Allegro\")"),
                    ("duration", "Note duration (e.g., 4 for quarter note)"),
                    ("bpm", "Beats per minute") },
            Sample: "tempo \"Allegro\" 4 = 120",
            HoverMarker: "**Tempo**"),
        new("time", "time numerator/denominator",
            "Sets the time signature.",
            new[] { ("numerator/denominator", "Time signature (e.g., 4/4, 3/4, 6/8)") },
            Sample: "time 3/4",
            HoverMarker: "**Time Signature**"),
        new("key", "key pitch major|minor",
            "Sets the key signature.",
            new[] { ("pitch", "Key pitch (e.g., c, g, fis, bes)"),
                    ("major|minor", "Mode: major or minor") },
            Sample: "key g major",
            HoverMarker: "**Key Signature**"),
        new("tuplet", "tuplet ratio { music }",
            "Creates a tuplet (e.g., triplet).",
            new[] { ("ratio", "Ratio (e.g., 3/2 for triplet)"),
                    ("{ music }", "Notes in the tuplet") },
            Sample: "part melody\nsection A { melody {\ntuplet 3/2 { c8 d e }\n} }\nform { ~A }\nscore { staff melody }",
            HoverMarker: "**Tuplet**"),
        new("override", "override Grob.property = value",
            "Overrides a grob (graphical object) property.",
            new[] { ("Grob.property", "Grob name and property (e.g., NoteHead.color, Stem.transparent)"),
                    ("value", "New value (number, string, or identifier)") },
            Sample: "override NoteHead.color = red",
            HoverMarker: "**Override**"),
        new("phrase", "phrase name { music }",
            "Declares a reusable musical phrase. Reference with name.",
            new[] { ("name", "Phrase name (identifier)"),
                    ("{ music }", "Music content") },
            Sample: "phrase theme { c d e f }",
            HoverMarker: "**Phrase**"),
        new("section", "section Name { parts... }",
            "Declares a section grouping multiple parts.",
            new[] { ("Name", "Section name (identifier)"),
                    ("{ parts... }", "Part blocks with music") },
            Sample: "section S { m { c4 d } }",
            HoverMarker: "**Section**"),
    };

    /// <summary>The hover text for a syntax node (markdown), or null for a node
    /// with no hover. The keyword constructs' bold heads are the
    /// <see cref="SignatureEntry.HoverMarker"/>s the drift net matches.</summary>
    internal static string? Hover(SyntaxNode node)
    {
        if (VoltaBracketHover(node) is { } volta)
            return volta;
        if (CapoHover(node) is { } capo)
            return capo;
        if (InstrumentPresetHover(node) is { } preset)
            return preset;
        return node switch
        {
            NoteSyntax note => $"**Note**: {note.Pitch.PitchName}\n\nOctave offset: {note.Pitch.OctaveOffset}\n\nDuration: {note.Duration?.Value.ToString() ?? "inherited"}",
            RestSyntax rest => $"**Rest**\n\nDuration: {rest.Duration?.Value.ToString() ?? "inherited"}",
            ChordSyntax => "**Chord**",
            BarlineSyntax => "**Barline**",
            TieSyntax => "**Tie**: Connects two notes of the same pitch",
            SlurSyntax slur => slur.IsOpen ? "**Slur start**: `(`" : "**Slur end**: `)`",
            RepeatExpressionSyntax => "**Repeat**: Repeats the enclosed music",
            PercentMarkSyntax { Parent: PercentRepeatSyntax group } mark =>
                $"**Percent repeat**: `{mark.Mark.Text}` — the {(group.Bars == 1 ? "bar" : $"{group.Bars} bars")} before it once more "
                + $"(heard {group.Count} times in all)",
            PercentMarkSyntax mark => $"**Percent mark** `{mark.Mark.Text}`: repeats the bars written before it — here it repeats nothing (LYS1048)",
            ParallelExpressionSyntax => "**Parallel**: Multiple voices played simultaneously",
            TimeSignatureSyntax ts => $"**Time Signature**: {ts.Beats}/{ts.BeatType}",
            TempoDeclarationSyntax tempo => $"**Tempo**: {tempo.Marking ?? ""} {(tempo.BeatUnit != null ? $"{tempo.BeatUnit} = " : "")}{tempo.Bpm ?? 120} BPM".Trim(),
            KeySignatureSyntax key => $"**Key Signature**: {key.DisplayName}",
            ClefDeclarationSyntax clef => $"**Clef**: {clef.ClefName.Text}",
            GraceExpressionSyntax grace => $"**Grace notes**: {(grace.IsAcciaccatura ? "Acciaccatura (slashed)" : grace.IsAppoggiatura ? "Appoggiatura" : "Grace")}",
            TupletExpressionSyntax tuplet => $"**Tuplet**: {tuplet.TupletRatio} in the time of {tuplet.BaseDivision}",
            OverrideDeclarationSyntax ovr => $"**Override**: `{ovr.GrobName.Text}.{ovr.PropertyName.Text}` = `{ovr.ValueToken.Text}`",
            RevertDeclarationSyntax rev => $"**Revert**: `{rev.GrobName.Text}.{rev.PropertyName.Text}`",
            OnceModifierSyntax => "**Once**: Applies override/revert for one note only",
            PhraseDeclarationSyntax phrase => $"**Phrase**: `{phrase.Name.Text}` — Reusable music block",
            SectionDeclarationSyntax section => $"**Section**: `{section.SectionName}` — Groups parts for a musical section",
            FormDeclarationSyntax => "**Structure**: Defines playback order of sections",
            RenderDeclarationSyntax => "**Score**: A printable score — visual layout (staff assignment). Output format is a CLI choice.",
            VariableDeclarationSyntax varDecl => $"**Variable**: `{varDecl.Name.Text}`",
            VariableReferenceSyntax varRef => $"**Variable reference**: `${varRef.Name.Text}`",
            LyricsBlockSyntax => "**Lyrics**: Text aligned to notes",
            ArticulationSyntax art => $"**Articulation**: @{art.NameToken.Text}",
            _ => null
        };
    }

    /// <summary>
    /// The hover for a layout's <c>capo</c> word and the fret after it (owner's design
    /// 2026-09-28, HANDOFF §2 K2; built 2026-09-29): what a capo does, and the capo suggestion —
    /// each fret 0–7 with the barre chords the file's chords would take there
    /// (<see cref="LilySharp.Core.Semantics.CapoAdvisor"/>), fewest first, on the entry's tuning
    /// word, else the guitar. Or null.
    /// </summary>
    private static string? CapoHover(SyntaxNode node)
    {
        const string capo = LilySharp.Core.Semantics.ChordDiagramsKey.CapoWord;
        if (node is not SyntaxTokenNode token || token.Parent is not LayoutDeclarationSyntax layout)
            return null;
        // The word itself, or the number right after it.
        bool onWord = token.Text == capo;
        bool onFret = token.Kind == SyntaxKind.IntegerLiteral && PreviousToken(layout, token)?.Text == capo;
        if (!onWord && !onFret)
            return null;
        string? tuningWord = null;
        foreach (var entry in layout.Entries)
        {
            if (entry.Key != LilySharp.Core.Semantics.ChordDiagramsKey.Key)
                continue;
            if (entry.Values.Count > 0 && LilySharp.Core.Semantics.ChordDiagramsKey.IsTuningWord(entry.Values[0].Text))
                tuningWord = entry.Values[0].Text;
        }
        SyntaxNode root = layout;
        while (root.Parent != null)
            root = root.Parent;
        var tuning = LilySharp.Core.Tablature.Tunings.Parse(tuningWord ?? "guitar");
        var ranking = LilySharp.Core.Semantics.CapoAdvisor.Rank(root, tuning);
        var sb = new System.Text.StringBuilder();
        sb.Append($"**Capo** `{capo} N` — the diagrams are the shapes pressed above the capo, the chord names ")
          .Append("the pressed chords' (`chordNames shape|sounding|both`), and \"Capo N\" stands at the score's head.");
        if (ranking.Count > 0)
        {
            sb.Append($"\n\nOn {tuningWord ?? "guitar"}, fewest barre chords first:");
            foreach (var c in ranking)
                sb.Append("  \n").Append(LilySharp.Core.Semantics.CapoAdvisor.Describe(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// The hover for a part's <c>instrument</c> preset (owner's request 2026-10-24): the defaults
    /// the preset fills in, one line each as the property that would say it alone — clef, octave,
    /// transposition, tuning, MIDI sound — with the line a property of the part itself replaces
    /// struck through. Not the name printed beside the staff (the owner's choice: that one shows
    /// on the page). Null off an <c>instrument</c> property, or for a name that is not a preset.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>octave</c> and the transposition read apart on purpose: the octave places the
    /// WRITTEN notes, the transposition moves only the SOUND, so <c>instrument bass</c> writes a
    /// bare <c>c</c> as C3 and plays C2. A part that writes <c>octave 2</c> meaning "a bass is low"
    /// writes C2 and plays C1 — the line says both pitches so the two cannot be read as one.
    /// </remarks>
    private static string? InstrumentPresetHover(SyntaxNode node)
    {
        var property = node as PropertyAssignmentSyntax ?? node.Parent as PropertyAssignmentSyntax;
        if (property is not { NameToken.Text: "instrument", Parent: PartDeclarationSyntax part })
            return null;
        var texts = property.Values.OfType<SyntaxTokenNode>().Select(t => t.Text);
        string preset = LilySharp.Core.Svg.Model.InstrumentDefaults.SplitInstrument(texts).Preset;
        if (!LilySharp.Core.Svg.Model.InstrumentDefaults.IsKnownInstrument(preset))
            return null;
        preset = preset.ToLowerInvariant();

        // The part's own properties, which replace the preset's.
        var own = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in part.Properties)
            if (p.ValueText is { } v)
                own[p.NameToken.Text] = v;
        string? Own(string key) => own.TryGetValue(key, out var w) ? $"{key} {w}" : null;
        static string Line(string text, string? replacedBy)
            => replacedBy != null ? $"- ~~{text}~~ — this part writes `{replacedBy}`" : $"- {text}";

        var (clef, octave) = LilySharp.Core.Svg.Model.InstrumentDefaults.GetDefaults(preset);
        int shift = LilySharp.Core.Svg.Model.InstrumentDefaults.GetTransposition(preset);
        var sb = new System.Text.StringBuilder();
        sb.Append($"**Instrument preset** `{preset}` — the defaults it sets; a property of the part replaces each:\n\n");
        sb.Append(Line($"`clef {LilySharp.Core.Svg.Model.InstrumentDefaults.ClefWord(clef)}`", Own("clef"))).Append('\n');
        // `octave absolute` / `relative` choose a mode, not the register: only a number replaces it.
        sb.Append(Line($"`octave {octave}` — a bare `c` is written C{octave}",
            own.TryGetValue("octave", out var o) && int.TryParse(o, out _) ? Own("octave") : null)).Append('\n');
        // Every line starts with a code span: VS Code pads one on its left, so a line that started
        // with plain words stood out of the column (the owner's report, 2026-10-24). A shift no
        // ottava marker writes (the clarinet's −2) names the property alone.
        string marker = LilySharp.Core.Svg.Model.InstrumentDefaults.TranspositionMarkers
            .FirstOrDefault(m => LilySharp.Core.Svg.Model.InstrumentDefaults.ParseTranspositionSemitones(m) == shift) is { } m2
            ? $"`transposition {m2}`" : "`transposition`";
        int written = 12 * (octave + 1);   // that bare c, as MIDI
        sb.Append(Line(shift == 0
            ? "`transposition` — none, sounds as written"
            : $"{marker} — sounds {Math.Abs(shift)} semitones {(shift < 0 ? "lower" : "higher")}: "
              + $"written C{octave} sounds {PitchName(written + shift, flats: true)}", Own("transposition"))).Append('\n');
        if (LilySharp.Core.Svg.Model.InstrumentDefaults.GetTuning(preset) is { } tuningWord)
        {
            var strings = LilySharp.Core.Tablature.Tunings.GetTuning(LilySharp.Core.Tablature.Tunings.Parse(tuningWord));
            sb.Append(Line($"`tuning {tuningWord}` — {string.Join(" ", strings.Select(s => PitchName(s, flats: false)))}"
                + " (the strings a tab frets on)", Own("tuning"))).Append('\n');
        }
        if (LilySharp.Core.Svg.Model.InstrumentDefaults.GetMidiProgram(preset) is int program)
            sb.Append(Line($"`midiInstrument \"{LilySharp.Core.Midi.GeneralMidi.InstrumentNames[program]}\"`",
                own.TryGetValue("midiInstrument", out var mi) ? $"midiInstrument \"{mi}\"" : null));
        return sb.ToString().TrimEnd();
    }

    /// <summary>A MIDI pitch as a letter, an accidental and an octave (<c>B♭3</c>, <c>F♯2</c>).</summary>
    private static string PitchName(int midi, bool flats)
        => (flats
            ? new[] { "C", "D♭", "D", "E♭", "E", "F", "G♭", "G", "A♭", "A", "B♭", "B" }
            : new[] { "C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B" })[midi % 12]
           + (midi / 12 - 1);

    /// <summary>The token before <paramref name="token"/> among <paramref name="layout"/>'s
    /// children, or null.</summary>
    private static SyntaxTokenNode? PreviousToken(LayoutDeclarationSyntax layout, SyntaxTokenNode token)
    {
        SyntaxTokenNode? previous = null;
        for (int i = 0; i < layout.SlotCount; i++)
        {
            if (layout.GetChild(i) is not SyntaxTokenNode t)
                continue;
            if (t.SourceStart == token.SourceStart && t.Text == token.Text)
                return previous;
            previous = t;
        }
        return null;
    }

    private const string VoltaBracketValues =
        "`all` — every bar of the ending (the default) · `line` — up to the end of the system "
        + "the bracket starts in · `N` — the ending's first N bars (all of it when shorter). "
        + "A bracket cut short always ends straight.";

    /// <summary>The hover for the <c>voltaBracket</c> layout key, an ending's
    /// <c>@voltaBracket(…)</c>, and an ending's <c>]</c> / <c>-]</c> (owner's design
    /// 2026-09-28), or null.</summary>
    private static string? VoltaBracketHover(SyntaxNode node)
    {
        const string key = LilySharp.Core.Semantics.VoltaBracketLength.Key;
        if (node is SyntaxTokenNode { Text: key, Parent: LayoutDeclarationSyntax })
            return $"**Layout** `{key}` — how far an ending's volta bracket reaches: " + VoltaBracketValues
                + $"\n\nAn ending's own `[1. B C]@{key}(…)` overrides it.";
        var mark = node as MusicMarkSyntax ?? node.Parent as MusicMarkSyntax;
        if (mark is { Parent: FormAlternativeSyntax ending } && mark.Name == key)
            return $"**Ending bracket length** `@{key}` — this ending's own reach, over the layout's "
                + $"`{key}`: " + VoltaBracketValues
                + (ending.LengthOverride is { } length ? $"\n\nHere: `{length}`." : "");
        if (node is SyntaxTokenNode { Kind: SyntaxKind.CloseBracket or SyntaxKind.Minus, Parent: FormAlternativeSyntax alt })
            return alt.EndsOpen
                ? "**Ending end** `-]` — the ending ends here; its bracket's right end stays straight (open)."
                : "**Ending end** `]` — the ending ends here; its bracket's right end hooks down "
                  + "(write `-]` to leave it open). A bracket its length cuts short ends straight.";
        return null;
    }
}
