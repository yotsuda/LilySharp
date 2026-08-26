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
        new("repeat", "repeat (unfold|percent|tremolo) count { music }",
            "Repeats the music block. For volta repeats use the symbolic form "
                + "'|: … :|' (count '|: … :|*N') with inline endings '[1. …] [2. …]'.",
            new[] { ("unfold|percent|tremolo", "Repeat kind (volta is the symbolic |: :| form, not this keyword)"),
                    ("count", "Number of repetitions (integer)"),
                    ("{ music }", "Music block to repeat") },
            Sample: "part melody\nsection A { melody {\nrepeat unfold 2 { c4 d e f }\n} }\nform main { ~A }\nscore main { staff melody }",
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
            Sample: "part melody\nsection A { melody {\ntuplet 3/2 { c8 d e }\n} }\nform main { ~A }\nscore main { staff melody }",
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
        return node switch
        {
            NoteSyntax note => $"**Note**: {note.Pitch.PitchName}\n\nOctave offset: {note.Pitch.OctaveOffset}\n\nDuration: {note.Duration?.Value.ToString() ?? "inherited"}",
            RestSyntax rest => $"**Rest**\n\nDuration: {rest.Duration?.Value.ToString() ?? "inherited"}",
            ChordSyntax => "**Chord**",
            BarlineSyntax => "**Barline**",
            TieSyntax => "**Tie**: Connects two notes of the same pitch",
            SlurSyntax slur => slur.IsOpen ? "**Slur start**: `(`" : "**Slur end**: `)`",
            RepeatExpressionSyntax => "**Repeat**: Repeats the enclosed music",
            ParallelExpressionSyntax => "**Parallel**: Multiple voices played simultaneously",
            TimeSignatureSyntax ts => $"**Time Signature**: {ts.Beats}/{ts.BeatType}",
            TempoDeclarationSyntax tempo => $"**Tempo**: {tempo.Marking ?? ""} {(tempo.BeatUnit != null ? $"{tempo.BeatUnit} = " : "")}{tempo.Bpm ?? 120} BPM".Trim(),
            KeySignatureSyntax key => $"**Key Signature**: {key.Pitch?.PitchName} {(key.IsMajor ? "major" : "minor")}",
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
}
