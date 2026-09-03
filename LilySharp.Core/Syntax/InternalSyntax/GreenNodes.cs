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

namespace LilySharp.Core.Syntax.InternalSyntax;

/// <summary>
/// A list of Green nodes (used for children).
/// </summary>
internal sealed class GreenNodeList : GreenNode
{
    private readonly GreenNode[] _nodes;

    public GreenNodeList(GreenNode[] nodes)
        : base(SyntaxKind.None, nodes)
    {
        _nodes = nodes;
    }

    public int Count => _nodes.Length;
    public GreenNode this[int index] => _nodes[index];
}

/// <summary>
/// Base class for syntax nodes (non-token internal nodes).
/// </summary>
internal abstract class GreenSyntaxNode : GreenNode
{
    protected GreenSyntaxNode(SyntaxKind kind, GreenNode?[] children)
        : base(kind, children)
    {
    }
}

/// <summary>
/// Compilation unit - the root node.
/// </summary>
internal sealed class CompilationUnitGreen : GreenSyntaxNode
{
    public CompilationUnitGreen(GreenNode?[] members, SyntaxToken endOfFile)
        : base(SyntaxKind.CompilationUnit, [.. members, endOfFile])
    {
    }
}

/// <summary>
/// A music block: { ... }
/// </summary>
internal sealed class MusicBlockGreen : GreenSyntaxNode
{
    public MusicBlockGreen(SyntaxToken openBrace, GreenNode?[] items, SyntaxToken closeBrace)
        : base(SyntaxKind.MusicBlock, [openBrace, .. items, closeBrace])
    {
    }
}

/// <summary>
/// A pitch with optional octave marks: c, cis', des,,
/// </summary>
internal sealed class PitchGreen : GreenSyntaxNode
{
    public PitchGreen(SyntaxToken pitchToken, GreenNode?[] octaveMarks)
        : base(SyntaxKind.Pitch, [pitchToken, .. octaveMarks])
    {
    }

    /// <summary>
    /// Pitch + articulations (used inside chord brackets for per-pitch annotations
    /// like fingering or ties).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/parser.yy — chord_body grammar accepts post-event
    /// articulations on each pitch.
    /// </remarks>
    public PitchGreen(SyntaxToken pitchToken, GreenNode?[] octaveMarks, GreenNode?[] articulations)
        : base(SyntaxKind.Pitch, [pitchToken, .. octaveMarks, .. articulations])
    {
    }
}

/// <summary>
/// A duration: 4, 8., 16..
/// </summary>
internal sealed class DurationGreen : GreenSyntaxNode
{
    public DurationGreen(SyntaxToken number, GreenNode?[] dots)
        : base(SyntaxKind.Duration, [number, .. dots])
    {
    }
}

/// <summary>
/// A note: pitch + optional duration + optional tremolo + articulations
/// </summary>
internal sealed class NoteGreen : GreenSyntaxNode
{
    public NoteGreen(PitchGreen pitch, DurationGreen? duration, SyntaxToken? tremolo, GreenNode?[] articulations)
        : base(SyntaxKind.Note, [pitch, duration, tremolo, .. articulations])
    {
    }
}

/// <summary>
/// drummap { hh: position 6 notehead x … } — per-score overrides of the
/// built-in drum table. The body tokens are stored verbatim; the red node
/// interprets the entry list (name: key value …).
/// </summary>
internal sealed class DrummapDeclarationGreen : GreenSyntaxNode
{
    public DrummapDeclarationGreen(SyntaxToken keyword, SyntaxToken openBrace, GreenNode?[] tokens, SyntaxToken closeBrace)
        : base(SyntaxKind.DrummapDeclaration, [keyword, openBrace, .. tokens, closeBrace])
    {
    }
}

/// <summary>
/// Error-recovery wrapper for a voice { } opened INSIDE another voice's body
/// (flagged LYS0010): keeps the tokens for full source fidelity while staying
/// a NEUTRAL node — every walker sees the inner notes as plain descendants,
/// so the content INLINES into the enclosing voice instead of spawning a
/// phantom parallel voice.
/// </summary>
internal sealed class NestedVoiceRecoveryGreen : GreenSyntaxNode
{
    public NestedVoiceRecoveryGreen(SyntaxToken voiceKeyword, SyntaxToken? name, GreenNode block)
        : base(SyntaxKind.NestedVoiceRecovery, [voiceKeyword, name, block])
    {
    }
}

/// <summary>
/// A drum note (bd4, sn8, hh): name + optional duration + optional tremolo +
/// articulations — the same trailing shape as NoteGreen.
/// LILYPOND-REF: \drummode note events.
/// </summary>
internal sealed class DrumNoteGreen : GreenSyntaxNode
{
    public DrumNoteGreen(SyntaxToken name, DurationGreen? duration, SyntaxToken? tremolo, GreenNode?[] articulations)
        : base(SyntaxKind.DrumNote, [name, duration, tremolo, .. articulations])
    {
    }
}

/// <summary>
/// A rest: r, s, R + optional duration + optional <c>*N</c> measure-count multiplier.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/parser.yy — multi-measure rest grammar (R1*N)
/// LILYPOND-REF: lily/multi-measure-rest.cc — Multi_measure_rest grob
/// The <c>*N</c> multiplier applies only to <c>R</c> (full-measure rest) and
/// expands into N consecutive measure-rests semantically.
/// </remarks>
internal sealed class RestGreen : GreenSyntaxNode
{
    public RestGreen(SyntaxToken restToken, DurationGreen? duration,
                     SyntaxToken? asterisk = null, SyntaxToken? measureCount = null,
                     GreenNode?[]? articulations = null)
        : base(SyntaxKind.Rest, [restToken, duration, asterisk, measureCount, .. articulations ?? []])
    {
    }
}

/// <summary>
/// A chord: <c>&lt; pitch pitch ... &gt;</c> + optional duration + optional tremolo
/// </summary>
internal sealed class ChordGreen : GreenSyntaxNode
{
    public ChordGreen(
        SyntaxToken openAngle,
        GreenNode?[] pitches,
        SyntaxToken closeAngle,
        GreenNode?[] octaveMarks,
        DurationGreen? duration,
        SyntaxToken? tremolo,
        GreenNode?[] articulations)
        : base(SyntaxKind.Chord,
            [openAngle, .. pitches, closeAngle, .. octaveMarks, duration, tremolo, .. articulations])
    {
    }
}

/// <summary>
/// A chord repetition: <c>q</c> + optional duration + optional tremolo +
/// articulations — the same trailing shape as NoteGreen, with the <c>q</c>
/// identifier token in place of a pitch. The previous chord's notes are filled
/// in by the shared resolver at walk time, never in the tree.
/// LILYPOND-REF: scm/music-functions.scm:923-946 expand-repeat-chords!
/// </summary>
internal sealed class ChordRepetitionGreen : GreenSyntaxNode
{
    public ChordRepetitionGreen(
        SyntaxToken qToken,
        GreenNode?[] octaveMarks,
        DurationGreen? duration,
        SyntaxToken? tremolo,
        GreenNode?[] articulations)
        : base(SyntaxKind.ChordRepetition, [qToken, .. octaveMarks, duration, tremolo, .. articulations])
    {
    }
}

/// <summary>
/// A slash note: <c>/</c> + optional duration + optional tremolo + articulations —
/// the same trailing shape as NoteGreen, with the slash token in place of a pitch.
/// A pitchless note drawn as a slash head on the middle staff line (rhythm /
/// comping notation). Silent in playback.
/// </summary>
internal sealed class SlashNoteGreen : GreenSyntaxNode
{
    public SlashNoteGreen(SyntaxToken slashToken, DurationGreen? duration, SyntaxToken? tremolo, GreenNode?[] articulations)
        : base(SyntaxKind.SlashNote, [slashToken, duration, tremolo, .. articulations])
    {
    }
}

/// <summary>
/// A bare duration: a spaced duration standing alone in a music sequence,
/// repeating the previous note, chord or slash with the new length. The tree
/// holds no pitches — the shared resolver maps the node to its original at
/// walk time, like <see cref="ChordRepetitionGreen"/>.
/// LILYPOND-REF: lily/parser.yy music_embedded — "duration post_events" builds
/// a NoteEvent with no pitch property; the preceding note's or chord's pitches
/// are used when typeset (measured 2.26.0: byte-identical to the explicit
/// spelling for a note, for a chord, and across intervening rests).
/// </summary>
internal sealed class BareDurationGreen : GreenSyntaxNode
{
    public BareDurationGreen(DurationGreen duration, SyntaxToken? tremolo, GreenNode?[] articulations)
        : base(SyntaxKind.BareDuration, [duration, tremolo, .. articulations])
    {
    }
}

/// <summary>
/// An arpeggio: <c>&lt;&lt; note note ... &gt;&gt;</c> — the inner notes play in SEQUENCE
/// (each with its own duration), but their octaves anchor to the FIRST note like a chord.
/// An optional duration after <c>&gt;&gt;</c> is the group's target total (auto-tuplet).
/// </summary>
internal sealed class ArpeggioGreen : GreenSyntaxNode
{
    public ArpeggioGreen(
        SyntaxToken openAngles,
        GreenNode?[] members,
        SyntaxToken closeAngles,
        GreenNode?[] octaveMarks,
        DurationGreen? totalDuration,
        GreenNode?[] articulations)
        : base(SyntaxKind.Arpeggio,
            [openAngles, .. members, closeAngles, .. octaveMarks, totalDuration, .. articulations])
    {
    }
}

/// <summary>
/// A scale-degree chord member: a degree number (an <c>IntegerLiteral</c>, or a
/// <c>ScaleDegree</c> token when an accidental is glued on — <c>3</c> / <c>3is</c>)
/// followed by octave marks (<c>7,</c> / <c>5'</c>). Resolved against the chord's
/// root and the current key into an actual pitch. See
/// <see cref="LilySharp.Core.Music.ChordDegrees"/>.
/// </summary>
internal sealed class ScaleDegreeGreen : GreenSyntaxNode
{
    public ScaleDegreeGreen(SyntaxToken degree, GreenNode?[] octaveMarks)
        : base(SyntaxKind.ChordDegree, [degree, .. octaveMarks])
    {
    }
}

/// <summary>
/// A barline: |, ||, |., |:, :| with an optional <c>*N</c> explicit repeat
/// count on a <c>:|</c> end-repeat (e.g. <c>:|*3</c> = play the span 3 times).
/// </summary>
/// <remarks>
/// The <c>*N</c> multiplier reuses the <c>R1*N</c> idiom and applies only to the
/// <c>:|</c> repeat-end barline; it sets the volta-repeat play count (default 2).
/// </remarks>
internal sealed class BarlineGreen : GreenSyntaxNode
{
    public BarlineGreen(SyntaxToken barToken, SyntaxToken? asterisk = null, SyntaxToken? count = null)
        : base(SyntaxKind.Barline, [barToken, asterisk, count])
    {
    }
}

/// <summary>
/// A tie: ~
/// </summary>
internal sealed class TieGreen : GreenSyntaxNode
{
    public TieGreen(SyntaxToken tilde)
        : base(SyntaxKind.Tie, [tilde])
    {
    }
}

/// <summary>
/// Slur markers: ( or )
/// </summary>
internal sealed class SlurGreen : GreenSyntaxNode
{
    public SlurGreen(SyntaxToken parenToken)
        : base(SyntaxKind.Slur, [parenToken])
    {
    }
}

/// <summary>
/// Beam markers: [ or ]
/// </summary>
internal sealed class BeamMarkerGreen : GreenSyntaxNode
{
    public BeamMarkerGreen(SyntaxToken bracketToken)
        : base(SyntaxKind.BeamMarker, [bracketToken]) { }
}

/// <summary>
/// An inline volta ending inside a <c>|: … :|</c> repeat: <c>[1. … ]</c> (or
/// a range/list <c>[1-2. …]</c> / <c>[1,3. …]</c>). Holds the volta number(s)
/// and the ending's music. Distinct from <see cref="FormAlternativeGreen"/>,
/// which references a named section rather than carrying literal music.
/// </summary>
/// <remarks>
/// Slot layout: <c>[</c>, number, separator?, endNumber?, <c>.</c>, items…, <c>]</c>.
/// </remarks>
internal sealed class InlineVoltaGreen : GreenSyntaxNode
{
    public InlineVoltaGreen(
        SyntaxToken openBracket,
        SyntaxToken number,
        SyntaxToken? separator,
        SyntaxToken? endNumber,
        SyntaxToken dot,
        GreenNode?[] items,
        SyntaxToken? closeBracket)
        : base(SyntaxKind.InlineVolta, [openBracket, number, separator, endNumber, dot, .. items, closeBracket])
    {
    }
}

/// <summary>
/// Property assignment: name: value
/// </summary>
internal sealed class PropertyAssignmentGreen : GreenSyntaxNode
{
    public PropertyAssignmentGreen(SyntaxToken name, SyntaxToken? colon, GreenNode?[] valueTokens)
        : base(SyntaxKind.PropertyAssignment, [name, colon, .. valueTokens])
    {
    }
}

/// <summary>
/// Time signature: time 4/4
/// </summary>
internal sealed class TimeSignatureGreen : GreenSyntaxNode
{
    // In a part/staff header the keyword takes a colon ('time: 4/4'); in the music
    // stream it is a bare command ('time 4/4'), so the colon slot is null there.
    public TimeSignatureGreen(SyntaxToken timeKeyword, SyntaxToken? colon, SyntaxToken numerator, SyntaxToken slash, SyntaxToken denominator)
        : base(SyntaxKind.TimeSignature, [timeKeyword, colon, numerator, slash, denominator])
    {
    }

    // Senza misura (time none): just the keyword and the "none" word.
    public TimeSignatureGreen(SyntaxToken timeKeyword, SyntaxToken? colon, SyntaxToken noneWord)
        : base(SyntaxKind.TimeSignature, [timeKeyword, colon, noneWord])
    {
    }

    // Additive meter (time 3+2/8): extra (+, int)* tokens follow the first
    // numerator IN SOURCE ORDER, before the slash.
    public TimeSignatureGreen(SyntaxToken timeKeyword, SyntaxToken? colon, GreenNode?[] numeratorTokens, SyntaxToken slash, SyntaxToken denominator)
        : base(SyntaxKind.TimeSignature, [timeKeyword, colon, .. numeratorTokens, slash, denominator])
    {
    }
}

/// <summary>
/// Tempo declaration: tempo "Allegro" 4 = 120 or tempo 120
/// </summary>
internal sealed class TempoDeclarationGreen : GreenSyntaxNode
{
    // Colon slot is non-null only in a part/staff header ('tempo: 120'); a bare
    // music-stream 'tempo 120' command leaves it null.
    public TempoDeclarationGreen(SyntaxToken tempoKeyword, SyntaxToken? colon, GreenNode?[] values)
        : base(SyntaxKind.TempoDeclaration, [tempoKeyword, colon, .. values])
    {
    }
}

/// <summary>
/// Partial (anacrusis) declaration: partial 4 — the next measure is a pickup of
/// the given duration. The value is a Duration node (number + optional dots), so
/// 'partial 2.' and 'partial 8' reuse the note-duration grammar.
/// </summary>
internal sealed class PartialDeclarationGreen : GreenSyntaxNode
{
    public PartialDeclarationGreen(SyntaxToken partialKeyword, GreenNode duration)
        : base(SyntaxKind.PartialDeclaration, [partialKeyword, duration])
    {
    }
}

/// <summary>
/// Metadata declaration: title "value" or tempo 120
/// </summary>
internal sealed class MetadataDeclarationGreen : GreenSyntaxNode
{
    public MetadataDeclarationGreen(SyntaxToken keyword, GreenNode?[] valueTokens)
        : base(SyntaxKind.MetadataDeclaration, [keyword, .. valueTokens])
    {
    }
}

/// <summary>
/// Font directive: fonts { KEY VALUE… }
/// </summary>
internal sealed class FontDeclarationGreen : GreenSyntaxNode
{
    public FontDeclarationGreen(SyntaxToken keyword, GreenNode?[] tokens)
        : base(SyntaxKind.FontDeclaration, [keyword, .. tokens])
    {
    }
}

/// <summary>
/// Paper directive: paper { KEY VALUE… }, tokens kept flat like the font block's.
/// </summary>
internal sealed class PaperDeclarationGreen : GreenSyntaxNode
{
    public PaperDeclarationGreen(SyntaxToken keyword, GreenNode?[] tokens)
        : base(SyntaxKind.PaperDeclaration, [keyword, .. tokens])
    {
    }
}

/// <summary>
/// Variable declaration: name = expr (new style) or let name = expr (legacy)
/// </summary>
internal sealed class VariableDeclarationGreen : GreenSyntaxNode
{
    // New style: name = { ... }
    public VariableDeclarationGreen(
        SyntaxToken name,
        SyntaxToken equals,
        GreenNode expression)
        : base(SyntaxKind.VariableDeclaration, [name, equals, expression])
    {
    }
}

/// <summary>
/// Phrase declaration: phrase name { ... }
/// </summary>
internal sealed class PhraseDeclarationGreen : GreenSyntaxNode
{
    public PhraseDeclarationGreen(
        SyntaxToken keyword,
        SyntaxToken name,
        GreenNode body)
        : base(SyntaxKind.PhraseDeclaration, [keyword, name, body])
    {
    }
}

/// <summary>
/// Part declaration: part name { props }
/// </summary>
internal sealed class PartDeclarationGreen : GreenSyntaxNode
{
    // With body: part name { ... }
    public PartDeclarationGreen(
        SyntaxToken keyword,
        SyntaxToken name,
        SyntaxToken openBrace,
        GreenNode?[] properties,
        SyntaxToken closeBrace)
        : base(SyntaxKind.PartDeclaration, [keyword, name, openBrace, .. properties, closeBrace])
    {
    }

    // Without body: part name
    public PartDeclarationGreen(
        SyntaxToken keyword,
        SyntaxToken name)
        : base(SyntaxKind.PartDeclaration, [keyword, name])
    {
    }

    // With inline display name, no body: part name "display"
    public PartDeclarationGreen(
        SyntaxToken keyword,
        SyntaxToken name,
        SyntaxToken displayName)
        : base(SyntaxKind.PartDeclaration, [keyword, name, displayName])
    {
    }

    // With inline display name and body: part name "display" { props }
    public PartDeclarationGreen(
        SyntaxToken keyword,
        SyntaxToken name,
        SyntaxToken displayName,
        SyntaxToken openBrace,
        GreenNode?[] properties,
        SyntaxToken closeBrace)
        : base(SyntaxKind.PartDeclaration, [keyword, name, displayName, openBrace, .. properties, closeBrace])
    {
    }
}

/// <summary>
/// Variable reference: a bare phrase name. A phrase reference may
/// carry trailing octave marks (<c>Chorus'</c> / <c>Chorus,</c>, same spelling
/// as a pitch's <c>c'</c> / <c>c,</c>) that shift where the movable phrase lands.
/// </summary>
internal sealed class VariableReferenceGreen : GreenSyntaxNode
{
    public VariableReferenceGreen(SyntaxToken name)
        : base(SyntaxKind.VariableReference, [name])
    {
    }

    public VariableReferenceGreen(SyntaxToken name, GreenNode?[] octaveMarks)
        : base(SyntaxKind.VariableReference, [name, .. octaveMarks])
    {
    }
}

/// <summary>
/// Articulation: @staccato, @accent, etc.
/// </summary>
internal sealed class ArticulationGreen : GreenSyntaxNode
{
    // Slots: '@', name, '.', direction, then any tokens the parser REJECTED but
    // still consumed (a second '.up'/'.down' — an error, not a silent drop).
    //
    // dotToken is the '.' of the '.up' / '.down' placement qualifier. It is a
    // slot of its own because a token the parser eats without storing is a token
    // the TREE NO LONGER CONTAINS: `@staccato.up` came back out of the tree as
    // `@staccatoup` (round trip broken, no diagnostic), and — worse — every node
    // after it reported a source position one character too early, because a
    // node's position is the running sum of the green widths before it. That
    // drives data-pos in the SVG, the LSP's jump targets and the layout
    // converter that WRITES .lys back out. HANDOFF §5.2.1⑤.
    public ArticulationGreen(
        SyntaxToken atToken,
        SyntaxToken nameToken,
        SyntaxToken? dotToken = null,
        SyntaxToken? directionToken = null,
        params SyntaxToken[] rejectedTokens)
        : base(SyntaxKind.Articulation, [atToken, nameToken, dotToken, directionToken, .. rejectedTokens])
    {
    }
}

/// <summary>
/// Dynamic mark: \p, \f, \cresc, etc.
/// </summary>
internal sealed class DynamicGreen : GreenSyntaxNode
{
    // Slots: '@', name, '.', direction, then any tokens the parser REJECTED but
    // still consumed ('.up' on a hairpin trigger — an error, not a silent drop).
    // dotToken exists for the same reason as ArticulationGreen's: see there.
    public DynamicGreen(
        SyntaxToken backslashToken,
        SyntaxToken dynamicToken,
        SyntaxToken? dotToken = null,
        SyntaxToken? directionToken = null,
        params SyntaxToken[] rejectedTokens)
        : base(SyntaxKind.Dynamic, [backslashToken, dynamicToken, dotToken, directionToken, .. rejectedTokens])
    {
    }
}

/// <summary>
/// Repeat expression: repeat volta 2 { ... } alternative { ... }
/// </summary>
internal sealed class RepeatExpressionGreen : GreenSyntaxNode
{
    public RepeatExpressionGreen(
        SyntaxToken repeatKeyword,
        SyntaxToken repeatType,
        SyntaxToken count,
        MusicBlockGreen body,
        AlternativeClauseGreen? alternative)
        : base(SyntaxKind.RepeatExpression, [repeatKeyword, repeatType, count, body, alternative])
    {
    }
}

/// <summary>
/// Alternative clause: alternative { { ... } { ... } }
/// </summary>
internal sealed class AlternativeClauseGreen : GreenSyntaxNode
{
    public AlternativeClauseGreen(
        SyntaxToken alternativeKeyword,
        SyntaxToken openBrace,
        GreenNode?[] alternatives,
        SyntaxToken closeBrace)
        : base(SyntaxKind.AlternativeClause, [alternativeKeyword, openBrace, .. alternatives, closeBrace])
    {
    }
}

/// <summary>
/// Parallel expression: <c>&lt;&lt; expr \\ expr &gt;&gt;</c>
/// </summary>
internal sealed class ParallelExpressionGreen : GreenSyntaxNode
{
    public ParallelExpressionGreen(
        SyntaxToken openAngle,
        GreenNode?[] voices,
        SyntaxToken closeAngle)
        : base(SyntaxKind.ParallelExpression, [openAngle, .. voices, closeAngle])
    {
    }
}

/// <summary>
/// Key signature: key c major, key g minor
/// </summary>
internal sealed class KeySignatureGreen : GreenSyntaxNode
{
    public KeySignatureGreen(
        SyntaxToken keyKeyword,
        GreenNode pitch,
        SyntaxToken mode)
        : base(SyntaxKind.KeySignature, [keyKeyword, pitch, mode])
    {
    }

    // Non-traditional signature: `key custom fis cis …` — the altered pitches
    // in print order. LILYPOND-REF: keyAlterations; MusicXML key-step/key-alter.
    public KeySignatureGreen(
        SyntaxToken keyKeyword,
        SyntaxToken customWord,
        GreenNode?[] pitches)
        : base(SyntaxKind.KeySignature, [keyKeyword, customWord, .. pitches])
    {
    }
}

/// <summary>
/// Clef declaration: clef treble, clef bass
/// </summary>
internal sealed class ClefDeclarationGreen : GreenSyntaxNode
{
    public ClefDeclarationGreen(
        SyntaxToken clefKeyword,
        SyntaxToken clefName)
        : base(SyntaxKind.ClefDeclaration, [clefKeyword, clefName])
    {
    }
}

/// <summary>
/// Octave mode directive: <c>octave absolute</c> / <c>octave relative</c>.
/// Switches how <c>'</c>/<c>,</c> marks are resolved.
/// </summary>
internal sealed class OctaveDirectiveGreen : GreenSyntaxNode
{
    public OctaveDirectiveGreen(
        SyntaxToken octaveKeyword,
        SyntaxToken mode)
        : base(SyntaxKind.OctaveDirective, [octaveKeyword, mode])
    {
    }
}

/// <summary>
/// Tuplet expression: tuplet 3/2 { ... }
/// </summary>
internal sealed class TupletExpressionGreen : GreenSyntaxNode
{
    public TupletExpressionGreen(
        SyntaxToken tupletKeyword,
        SyntaxToken numerator,
        SyntaxToken slash,
        SyntaxToken denominator,
        GreenNode body)
        : base(SyntaxKind.TupletExpression, [tupletKeyword, numerator, slash, denominator, body])
    {
    }
}

/// <summary>
/// Grace expression: grace { notes } or acciaccatura { notes }
/// </summary>
internal sealed class GraceExpressionGreen : GreenSyntaxNode
{
    public GraceExpressionGreen(
        SyntaxToken graceKeyword,
        GreenNode body)
        : base(SyntaxKind.GraceExpression, [graceKeyword, body])
    {
    }
}

/// <summary>
/// Cue expression: <c>cue { notes }</c> or <c>cue &lt;clef&gt; { notes }</c>.
/// </summary>
/// <remarks>
/// The clef slot is always present and may be null — <see cref="GreenNode"/>'s child array
/// admits nulls — so the body is always child 2 and no index shifts with the option.
/// </remarks>
internal sealed class CueExpressionGreen : GreenSyntaxNode
{
    public CueExpressionGreen(
        SyntaxToken cueKeyword,
        SyntaxToken? clefKeyword,
        GreenNode body)
        : base(SyntaxKind.CueExpression, [cueKeyword, clefKeyword, body])
    {
    }
}

// ============================================================
// Tablature Green Nodes
// ============================================================

/// <summary>
/// String number annotation: \1, \2, etc.
/// </summary>
internal sealed class StringNumberAnnotationGreen : GreenSyntaxNode
{
    public StringNumberAnnotationGreen(SyntaxToken stringNumber)
        : base(SyntaxKind.StringNumberAnnotation, [stringNumber])
    {
    }
}

// ============================================================
// New Section-Oriented Green Nodes

/// <summary>
/// Section declaration: section Name { ... }
/// </summary>
internal sealed class SectionDeclarationGreen : GreenSyntaxNode
{
    /// <param name="tilde">
    /// The <c>~</c> of <c>section ~A { … }</c>, or null. It marks a section that carries
    /// STRUCTURE rather than a rehearsal letter, and it FLIPS that section's label default
    /// (owner's decision, 2026-08-31): a reference's own <c>~</c> stops meaning "hide" and
    /// starts meaning "the other one". The slot is always present so the items keep a fixed
    /// offset; the NAME is found by kind, because it is no longer at a fixed index.
    /// </param>
    public SectionDeclarationGreen(
        SyntaxToken sectionKeyword,
        SyntaxToken? tilde,
        SyntaxToken name,
        SyntaxToken openBrace,
        GreenNode?[] items,
        SyntaxToken closeBrace)
        : base(SyntaxKind.SectionDeclaration, [sectionKeyword, tilde, name, openBrace, .. items, closeBrace])
    {
    }
}

/// <summary>
/// Include directive: using "file.lys"
/// </summary>
internal sealed class UsingDirectiveGreen : GreenSyntaxNode
{
    public UsingDirectiveGreen(SyntaxToken keyword, SyntaxToken path)
        : base(SyntaxKind.UsingDirective, [keyword, path])
    {
    }
}

/// <summary>
/// Part block inside section: guitar { ... }
/// </summary>
internal sealed class PartBlockGreen : GreenSyntaxNode
{
    public PartBlockGreen(
        SyntaxToken partName,
        GreenNode?[] options,
        GreenNode body)
        : base(SyntaxKind.PartBlock, [partName, .. options, body])
    {
    }
}

/// <summary>
/// Structure declaration: structure { ... }
/// </summary>
internal sealed class FormDeclarationGreen : GreenSyntaxNode
{
    public FormDeclarationGreen(
        SyntaxToken formKeyword,
        SyntaxToken? name,
        SyntaxToken openBrace,
        GreenNode?[] items,
        SyntaxToken closeBrace)
        : base(SyntaxKind.FormDeclaration,
            name != null
                ? [formKeyword, name, openBrace, .. items, closeBrace]
                : [formKeyword, openBrace, .. items, closeBrace])
    {
    }
}

/// <summary>
/// Section reference in structure: SectionName, SectionName', SectionName, "label"
/// </summary>
/// <remarks>
/// The trailing octave marks sit BETWEEN the name and the label because that is where
/// the source writes them (<c>B' "reprise"</c>), and a green's slot order IS its text
/// order — every later offset in the file is computed by walking these slots. The name
/// stays at slot 0 so the readers that address it by index keep working; the LABEL is
/// found by KIND (SectionReferenceSyntax.DisplayLabel), since the marks now push it off
/// any fixed index.
/// </remarks>
internal sealed class SectionReferenceGreen : GreenSyntaxNode
{
    public SectionReferenceGreen(SyntaxToken identifier, GreenNode?[] octaveMarks, SyntaxToken? displayLabel = null)
        : base(SyntaxKind.SectionReference, [identifier, .. octaveMarks, displayLabel])
    {
    }
}

/// <summary>
/// Silent section reference in structure: ~SectionName (no label displayed)
/// </summary>
/// <remarks>The name stays at slot 1 — the marks follow it, so
/// <c>SectionReferenceFinder</c>, <c>SectionSymbols</c> and <c>FormWalk</c> keep
/// reading it there.</remarks>
internal sealed class SilentSectionReferenceGreen : GreenSyntaxNode
{
    public SilentSectionReferenceGreen(SyntaxToken tilde, SyntaxToken identifier, GreenNode?[] octaveMarks, SyntaxToken? displayLabel = null)
        : base(SyntaxKind.SilentSectionReference, [tilde, identifier, .. octaveMarks, displayLabel])
    {
    }
}

/// <summary>
/// Custom text in structure: _"text"
/// </summary>
internal sealed class CustomTextGreen : GreenSyntaxNode
{
    public CustomTextGreen(SyntaxToken underscore, SyntaxToken text)
        : base(SyntaxKind.CustomText, [underscore, text])
    {
    }
}

/// <summary>
/// Music mark: @segno, @fine, @ds.al.fine, etc.
/// </summary>
internal sealed class MusicMarkGreen : GreenSyntaxNode
{
    public MusicMarkGreen(GreenNode?[] parts)
        : base(SyntaxKind.MusicMark, parts)
    {
    }
}


/// <summary>
/// Repeat block in structure: |: ... :| or |: ... :|*3
/// </summary>
internal sealed class FormRepeatBlockGreen : GreenSyntaxNode
{
    // Simple repeat: |: items :|
    public FormRepeatBlockGreen(
        SyntaxToken repeatStart,
        GreenNode?[] items,
        SyntaxToken repeatEnd)
        : base(SyntaxKind.FormRepeatBlock, [repeatStart, .. items, repeatEnd])
    {
    }

    // Repeat with alternatives: |: items | 1. A :| 2. B
    public FormRepeatBlockGreen(
        SyntaxToken repeatStart,
        GreenNode?[] items,
        SyntaxToken? barline,
        GreenNode?[] alternativesBeforeEnd,
        SyntaxToken repeatEnd,
        GreenNode? finalAlternative)
        : base(SyntaxKind.FormRepeatBlock, BuildChildren(repeatStart, items, barline, alternativesBeforeEnd, repeatEnd, finalAlternative, null, null))
    {
    }

    // Repeat with count: |: items :|*3 — the SAME spelling the inline music stream uses on its
    // own end-repeat bar line (Parser.Music.cs ParseBarline), which is LilyPond's `R1*20`
    // multiplier idiom. It was `x3` until 2026-08-03, which was a second spelling of one thing
    // AND unreachable: the lexer glues `x3` into one identifier, so the parser branch that read
    // it never fired and the count landed as an undefined section reference instead.
    public FormRepeatBlockGreen(
        SyntaxToken repeatStart,
        GreenNode?[] items,
        SyntaxToken? barline,
        GreenNode?[] alternativesBeforeEnd,
        SyntaxToken repeatEnd,
        GreenNode? finalAlternative,
        SyntaxToken? asterisk,
        SyntaxToken? repeatCount,
        GreenNode?[]? furtherAlternatives = null)
        : base(SyntaxKind.FormRepeatBlock, BuildChildren(repeatStart, items, barline, alternativesBeforeEnd, repeatEnd, finalAlternative, asterisk, repeatCount, furtherAlternatives))
    {
    }

    private static GreenNode?[] BuildChildren(
        SyntaxToken repeatStart,
        GreenNode?[] items,
        SyntaxToken? barline,
        GreenNode?[] alternativesBeforeEnd,
        SyntaxToken repeatEnd,
        GreenNode? finalAlternative,
        SyntaxToken? asterisk,
        SyntaxToken? repeatCount,
        GreenNode?[]? furtherAlternatives = null)
    {
        var children = new List<GreenNode?> { repeatStart };
        children.AddRange(items);
        if (barline != null)
        {
            children.Add(barline);
            children.AddRange(alternativesBeforeEnd);
        }
        children.Add(repeatEnd);
        // ⚠️ THE COUNT SITS ON THE BAR LINE, so it goes in right after it and BEFORE any final
        // ending — `|: A [1. B] :|*3 [2. C]`. Children are in source order or ToFullString
        // reorders the text, which is what the parser round-trip tests read.
        if (asterisk != null)
        {
            children.Add(asterisk);
            children.Add(repeatCount);
        }
        if (finalAlternative != null)
        {
            children.Add(finalAlternative);
        }
        // A THIRD, FOURTH … ending, already interleaved with the ':|' that precedes each one
        // — `|: A [1. B] :| [2. C] :| [3. D]`. Kept as flat children in source order like
        // everything above, which is what lets FormWalk read the block by walking slots
        // rather than by fixed indices, and what keeps ToFullString round-tripping.
        if (furtherAlternatives != null)
        {
            children.AddRange(furtherAlternatives);
        }
        return [.. children];
    }
}



/// <summary>
/// Alternative in repeat: 1. A, 2. B or [1. A] or [1-3. A] or [1. ~A]
/// </summary>
internal sealed class FormAlternativeGreen : GreenSyntaxNode
{
    // Legacy style: 1. A
    public FormAlternativeGreen(
        SyntaxToken number,
        SyntaxToken dot,
        SyntaxToken sectionName)
        : base(SyntaxKind.FormAlternative, [number, dot, sectionName])
    {
    }

    // Bracket style: [1. A] or [1-3. A] or [1,3. A] or [1. ~A] or [1. A "label"] or [1. A']
    public FormAlternativeGreen(
        SyntaxToken openBracket,
        SyntaxToken number,
        SyntaxToken? separator,
        SyntaxToken? endNumber,
        SyntaxToken dot,
        SyntaxToken? tilde,
        SyntaxToken sectionName,
        GreenNode?[] octaveMarks,
        SyntaxToken? displayLabel,
        SyntaxToken? closeBracket)
        : base(SyntaxKind.FormAlternative,
            BuildSlots(openBracket, number, separator, endNumber, dot, tilde, sectionName, octaveMarks, displayLabel, closeBracket))
    {
    }

    private static GreenNode?[] BuildSlots(
        SyntaxToken openBracket,
        SyntaxToken number,
        SyntaxToken? separator,
        SyntaxToken? endNumber,
        SyntaxToken dot,
        SyntaxToken? tilde,
        SyntaxToken sectionName,
        GreenNode?[] octaveMarks,
        SyntaxToken? displayLabel,
        SyntaxToken? closeBracket)
    {
        // Slot layout (always include tilde + displayLabel slots for consistent indexing):
        // With separator: [openBracket, number, separator, endNumber, dot, tilde?, sectionName, marks…, displayLabel?, closeBracket]
        // Without separator: [openBracket, number, dot, tilde?, sectionName, marks…, displayLabel?, closeBracket]
        // ⚠️ Everything UP TO the section name keeps a fixed index; the marks are variable
        // length, so the two slots after it (label, ']') are read by KIND — see
        // FormAlternativeSyntax.DisplayLabel / IsClosed. HasSeparator likewise stopped
        // counting slots: `[1,3. A']` and `[1. A]` can now have the same SlotCount.
        if (separator != null)
        {
            return [openBracket, number, separator, endNumber, dot, tilde, sectionName, .. octaveMarks, displayLabel, closeBracket];
        }
        else
        {
            return [openBracket, number, dot, tilde, sectionName, .. octaveMarks, displayLabel, closeBracket];
        }
    }
}

/// <summary>
/// Navigation mark: segno, fine, coda, dc, ds, etc.
/// </summary>
internal sealed class NavigationMarkGreen : GreenSyntaxNode
{
    // Simple: segno, fine, coda, dc, ds
    public NavigationMarkGreen(SyntaxToken keyword)
        : base(SyntaxKind.NavigationMark, [keyword])
    {
    }

    // Two parts: to coda, dc al, ds al
    public NavigationMarkGreen(SyntaxToken keyword1, SyntaxToken keyword2)
        : base(SyntaxKind.NavigationMark, [keyword1, keyword2])
    {
    }

    // Three parts: dc al fine, dc al coda, ds al fine, ds al coda
    public NavigationMarkGreen(SyntaxToken keyword1, SyntaxToken keyword2, SyntaxToken keyword3)
        : base(SyntaxKind.NavigationMark, [keyword1, keyword2, keyword3])
    {
    }
}

/// <summary>
/// Render declaration: render Name "file.svg" { ... }
/// </summary>
internal sealed class RenderDeclarationGreen : GreenSyntaxNode
{
    public RenderDeclarationGreen(
        SyntaxToken renderKeyword,
        SyntaxToken? name,
        SyntaxToken? filename,
        GreenNode? transpose,
        SyntaxToken openBrace,
        GreenNode?[] items,
        SyntaxToken closeBrace)
        : base(SyntaxKind.RenderDeclaration,
            BuildChildren(renderKeyword, name, filename, transpose, openBrace, items, closeBrace))
    {
    }

    // name, filename and the score transpose are all optional and precede the
    // brace, so assemble the child list rather than enumerate every combination.
    // Source order is preserved (transpose comes after the name, before the brace).
    private static GreenNode?[] BuildChildren(
        SyntaxToken renderKeyword, SyntaxToken? name, SyntaxToken? filename,
        GreenNode? transpose, SyntaxToken openBrace, GreenNode?[] items, SyntaxToken closeBrace)
    {
        var children = new List<GreenNode?> { renderKeyword };
        if (name != null) children.Add(name);
        if (filename != null) children.Add(filename);
        if (transpose != null) children.Add(transpose);
        children.Add(openBrace);
        children.AddRange(items);
        children.Add(closeBrace);
        return [.. children];
    }
}

/// <summary>
/// Staff render: staff [clef] partName  or  staff [clef] { partName }
/// (clef defaults to the part definition; braces are optional).
/// </summary>
internal sealed class StaffRenderGreen : GreenSyntaxNode
{
    public StaffRenderGreen(params SyntaxToken[] tokens)
        : base(SyntaxKind.StaffRender, tokens)
    {
    }
}

/// <summary>
/// Chord-row render: <c>chords name</c> inside a score — places a chord part as an
/// independent row. Tokens: [chordsKeyword, partName].
/// </summary>
internal sealed class ChordRowRenderGreen : GreenSyntaxNode
{
    public ChordRowRenderGreen(params SyntaxToken[] tokens)
        : base(SyntaxKind.ChordRowRender, tokens)
    {
    }
}

/// <summary>
/// Lyrics-row render: <c>lyrics name</c> inside a score — places a lyrics part as
/// an independent row. Tokens: [lyricsKeyword, partName].
/// </summary>
internal sealed class LyricsRowRenderGreen : GreenSyntaxNode
{
    public LyricsRowRenderGreen(params SyntaxToken[] tokens)
        : base(SyntaxKind.LyricsRowRender, tokens)
    {
    }
}

/// <summary>
/// Grand staff render: grandStaff { staff staff ... } — the members are
/// <c>staff</c> items and, between them, bound <c>lyrics NAME</c> rows (score =
/// a vertical stack of bands, inside a group as outside — a chorale's words
/// between the sopranos and the altos). A bad member survives as its kept
/// tokens, width-preserving (see Parser.ParseGrandStaffRender).
/// </summary>
internal sealed class GrandStaffRenderGreen : GreenSyntaxNode
{
    public GrandStaffRenderGreen(
        SyntaxToken grandStaffKeyword,
        SyntaxToken openBrace,
        GreenNode[] members,
        SyntaxToken closeBrace)
        : base(SyntaxKind.GrandStaffRender, [grandStaffKeyword, openBrace, ..members, closeBrace])
    {
    }
}

/// <summary>
/// Condensed-staff render: <c>condensedStaff { partA partB … }</c> — bare part names, not
/// <c>staff</c> items, because the result is ONE staff however many parts go in.
/// </summary>
internal sealed class CondensedStaffRenderGreen : GreenSyntaxNode
{
    public CondensedStaffRenderGreen(
        SyntaxToken condensedStaffKeyword,
        SyntaxToken openBrace,
        SyntaxToken[] partNames,
        SyntaxToken closeBrace)
        : base(SyntaxKind.CondensedStaffRender,
               [condensedStaffKeyword, openBrace, ..partNames, closeBrace])
    {
    }
}

/// <summary>
/// Combined-staff render: <c>combinedStaff { partA partB }</c> — the same bare part names as
/// <c>condensedStaff</c>, because the result is one staff either way. What differs is that
/// this one MERGES: the parts share a notehead where they agree.
/// </summary>
internal sealed class CombinedStaffRenderGreen : GreenSyntaxNode
{
    public CombinedStaffRenderGreen(
        SyntaxToken combinedStaffKeyword,
        SyntaxToken openBrace,
        SyntaxToken[] partNames,
        SyntaxToken closeBrace)
        : base(SyntaxKind.CombinedStaffRender,
               [combinedStaffKeyword, openBrace, ..partNames, closeBrace])
    {
    }
}

/// <summary>
/// Ossia render: ossia [clef] { partName }
/// LILYPOND-REF: ly/engraver-init.ly — ossia staves use reduced fontSize
/// </summary>
internal sealed class OssiaRenderGreen : GreenSyntaxNode
{
    public OssiaRenderGreen(params SyntaxToken[] tokens)
        : base(SyntaxKind.OssiaRender, tokens)
    {
    }
}

/// <summary>
/// Tab render: tab [tuning] partName  or  tab [tuning] { partName }
/// (tuning defaults to the part definition; braces are optional).
/// </summary>
internal sealed class TabRenderGreen : GreenSyntaxNode
{
    public TabRenderGreen(params SyntaxToken[] tokens)
        : base(SyntaxKind.TabRender, tokens)
    {
    }
}

/// <summary>
/// MIDI part render: partName channel:1 instrument:25
/// </summary>
internal sealed class MidiPartRenderGreen : GreenSyntaxNode
{
    public MidiPartRenderGreen(
        SyntaxToken partName,
        GreenNode?[] options)
        : base(SyntaxKind.MidiPartRender, [partName, .. options])
    {
    }
}

/// <summary>
/// Line break: break
/// </summary>
internal sealed class BreakGreen : GreenSyntaxNode
{
    public BreakGreen(SyntaxToken breakKeyword)
        : base(SyntaxKind.Break, [breakKeyword])
    {
    }
}

// ================================================================================
// Lyrics
// ================================================================================

/// <summary>
/// A lyrics block: lyrics { syllable syllable | syllable | }
/// </summary>
internal sealed class LyricsBlockGreen : GreenSyntaxNode
{
    // `lyrics [name] [sings part] { … }` — the optional name binds the lyrics to a
    // same-named voice or part; the optional `sings part` pair binds the track to
    // the named part's melody at the DEFINITION (the score only places it). The
    // optional tokens sit between the keyword and the brace, present-or-absent as
    // slots (the red side finds the brace by kind, not by index).
    public LyricsBlockGreen(
        SyntaxToken lyricsKeyword,
        SyntaxToken? name,
        SyntaxToken? singsKeyword,
        SyntaxToken? singsTarget,
        SyntaxToken openBrace,
        GreenNode?[] measures,
        SyntaxToken closeBrace)
        : base(SyntaxKind.LyricsBlock, Slots(lyricsKeyword, name, singsKeyword, singsTarget, openBrace, measures, closeBrace))
    {
    }

    private static GreenNode?[] Slots(SyntaxToken kw, SyntaxToken? name, SyntaxToken? singsKw,
        SyntaxToken? singsTarget, SyntaxToken open, GreenNode?[] measures, SyntaxToken close)
    {
        var head = new List<GreenNode?> { kw };
        if (name != null) head.Add(name);
        if (singsKw != null) head.Add(singsKw);
        if (singsTarget != null) head.Add(singsTarget);
        head.Add(open);
        head.AddRange(measures);
        head.Add(close);
        return head.ToArray();
    }
}

/// <summary>
/// A lyric measure: syllable syllable syllable |
/// </summary>
internal sealed class LyricMeasureGreen : GreenSyntaxNode
{
    public LyricMeasureGreen(GreenNode?[] syllables, SyntaxToken barline)
        : base(SyntaxKind.LyricMeasure, [.. syllables, barline])
    {
    }
}

/// <summary>
/// A per-occurrence lyric verse: <c>[1. syllable syllable | … ]</c>. The header keys
/// the section's playback occurrence(s) so a repeated/reprised section can carry
/// different words each pass — a single number (<c>[1. …]</c>), a comma list
/// (<c>[1,3. …]</c>), a dash range (<c>[1-2. …]</c>), with an optional leading <c>~</c>
/// that HIDES the stanza-number label while still applying to the listed occurrence(s)
/// (<c>[~1. …]</c>). The body is lyric measures, exactly like a plain (unbracketed) verse.
/// </summary>
/// <remarks>Slot layout: <c>[</c>, header tokens (<c>~</c>? number ((<c>,</c>|<c>-</c>)
/// number)*), <c>.</c>, measures…, <c>]</c>.</remarks>
internal sealed class LyricVoltaGreen : GreenSyntaxNode
{
    public LyricVoltaGreen(
        SyntaxToken openBracket,
        GreenNode?[] header,
        SyntaxToken dot,
        GreenNode?[] measures,
        SyntaxToken? closeBracket)
        : base(SyntaxKind.LyricVolta, [openBracket, .. header, dot, .. measures, closeBracket])
    {
    }
}

/// <summary>
/// A lyric syllable: text, melisma (~), or skip (_)
/// </summary>
internal sealed class LyricSyllableGreen : GreenSyntaxNode
{
    public LyricSyllableGreen(SyntaxToken token)
        : base(SyntaxKind.LyricSyllable, [token])
    {
    }
}

/// <summary>
/// An independent chord part block: <c>chords name { c | g:7 c | }</c>. Same chord
/// entry grammar as <see cref="ChordEntryGreen"/>, but carries a (required)
/// name binding it to a chord row placed via <c>chords name</c> in a score. The
/// name sits between the keyword and the brace (mirrors LyricsBlockGreen).
/// </summary>
internal sealed class ChordPartBlockGreen : GreenSyntaxNode
{
    public ChordPartBlockGreen(
        SyntaxToken keyword,
        SyntaxToken? name,
        SyntaxToken openBrace,
        GreenNode?[] items,
        SyntaxToken closeBrace)
        : base(SyntaxKind.ChordPartBlock,
            name != null
                ? [keyword, name, openBrace, .. items, closeBrace]
                : [keyword, openBrace, .. items, closeBrace])
    {
    }
}

/// <summary>
/// A single chord entry inside a <c>chords</c> body: the SYMBOL as it prints
/// (GRAMMAR_AUDIT 8.1) — a glued token run such as <c>Am</c> (one Identifier),
/// <c>F#m</c> (Identifier '#' Identifier) or <c>Cmaj7/E</c>. The tree keeps the
/// tokens in source order; <c>ChordEntrySyntax.SymbolText</c> re-joins them for
/// <c>ChordStructure.TryParseChordEntry</c>, so the block and <c>@chord</c> read
/// one format.
/// </summary>
internal sealed class ChordEntryGreen : GreenSyntaxNode
{
    public ChordEntryGreen(GreenNode?[] tokens)
        : base(SyntaxKind.ChordEntry, tokens)
    {
    }
}

/// <summary>
/// A chord-row slot extension: a lone <c>.</c> in a <c>chords</c> body. The
/// previous entry (or rest) holds through one more slot of the measure's beat
/// grid — <c>| C . . G7 |</c> in 4/4 is C for three beats, then G7.
/// </summary>
internal sealed class ChordExtendGreen : GreenSyntaxNode
{
    public ChordExtendGreen(SyntaxToken dot)
        : base(SyntaxKind.ChordExtend, [dot])
    {
    }
}

/// <summary>
/// Override declaration: override Grob.property = value
/// LILYPOND-REF: lily/context-property.cc (push)
/// </summary>
internal sealed class OverrideDeclarationGreen : GreenSyntaxNode
{
    public OverrideDeclarationGreen(
        SyntaxToken overrideKeyword,
        SyntaxToken grobName,
        SyntaxToken dot,
        SyntaxToken propertyName,
        SyntaxToken equals,
        SyntaxToken value)
        : base(SyntaxKind.OverrideDeclaration, [overrideKeyword, grobName, dot, propertyName, equals, value])
    {
    }
}

/// <summary>
/// Revert declaration: revert Grob.property
/// LILYPOND-REF: lily/context-property.cc (pop)
/// </summary>
internal sealed class RevertDeclarationGreen : GreenSyntaxNode
{
    public RevertDeclarationGreen(
        SyntaxToken revertKeyword,
        SyntaxToken grobName,
        SyntaxToken dot,
        SyntaxToken propertyName)
        : base(SyntaxKind.RevertDeclaration, [revertKeyword, grobName, dot, propertyName])
    {
    }
}

/// <summary>
/// Once modifier: once override/revert ...
/// LILYPOND-REF: lily/context-property.cc (temporary_override)
/// </summary>
internal sealed class OnceModifierGreen : GreenSyntaxNode
{
    public OnceModifierGreen(
        SyntaxToken onceKeyword,
        GreenNode command)
        : base(SyntaxKind.OnceModifier, [onceKeyword, command])
    {
    }
}