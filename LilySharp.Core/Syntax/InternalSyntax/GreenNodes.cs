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
    /// LILYPOND-REF: lily/lily-parser.yy — chord_body grammar accepts post-event
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
/// LILYPOND-REF: lily/lily-parser.yy — multi-measure rest grammar (R1*N)
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
/// A chord: < pitch pitch ... > + optional duration + optional tremolo
/// </summary>
internal sealed class ChordGreen : GreenSyntaxNode
{
    public ChordGreen(
        SyntaxToken openAngle,
        GreenNode?[] pitches,
        SyntaxToken closeAngle,
        DurationGreen? duration,
        SyntaxToken? tremolo,
        GreenNode?[] articulations)
        : base(SyntaxKind.Chord, [openAngle, .. pitches, closeAngle, duration, tremolo, .. articulations])
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
/// and the ending's music. Distinct from <see cref="StructureAlternativeGreen"/>,
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
/// Staff declaration: staff Name { ... }
/// </summary>
internal sealed class StaffDeclarationGreen : GreenSyntaxNode
{
    public StaffDeclarationGreen(
        SyntaxToken staffKeyword,
        SyntaxToken? name,
        SyntaxToken openBrace,
        GreenNode?[] members,
        SyntaxToken closeBrace)
        : base(SyntaxKind.StaffDeclaration, [staffKeyword, name, openBrace, .. members, closeBrace])
    {
    }
}

/// <summary>
/// Property assignment: name: value
/// </summary>
internal sealed class PropertyAssignmentGreen : GreenSyntaxNode
{
    public PropertyAssignmentGreen(SyntaxToken name, SyntaxToken colon, GreenNode?[] valueTokens)
        : base(SyntaxKind.PropertyAssignment, [name, colon, .. valueTokens])
    {
    }
}

/// <summary>
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

    // Legacy style: let name = expr
    public VariableDeclarationGreen(
        SyntaxToken letKeyword,
        SyntaxToken name,
        SyntaxToken equals,
        GreenNode expression)
        : base(SyntaxKind.VariableDeclaration, [letKeyword, name, equals, expression])
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

    // Legacy: part name "display" { members }
    public PartDeclarationGreen(
        SyntaxToken keyword,
        SyntaxToken? name,
        SyntaxToken? displayName,
        SyntaxToken openBrace,
        GreenNode?[] members,
        SyntaxToken closeBrace)
        : base(SyntaxKind.PartDeclaration, [keyword, name, displayName, openBrace, .. members, closeBrace])
    {
    }
}

/// <summary>
/// Variable reference: use name or $name or just name
/// </summary>
internal sealed class VariableReferenceGreen : GreenSyntaxNode
{
    public VariableReferenceGreen(SyntaxToken keyword, SyntaxToken name)
        : base(SyntaxKind.VariableReference, [keyword, name])
    {
    }

    public VariableReferenceGreen(SyntaxToken name)
        : base(SyntaxKind.VariableReference, [name])
    {
    }
}

/// <summary>
/// Articulation: @staccato, @accent, etc.
/// </summary>
internal sealed class ArticulationGreen : GreenSyntaxNode
{
    // directionToken is the optional '.up' / '.down' placement word (null = automatic).
    public ArticulationGreen(SyntaxToken atToken, SyntaxToken nameToken, SyntaxToken? directionToken = null)
        : base(SyntaxKind.Articulation, [atToken, nameToken, directionToken])
    {
    }
}

/// <summary>
/// Dynamic mark: \p, \f, \cresc, etc.
/// </summary>
internal sealed class DynamicGreen : GreenSyntaxNode
{
    // directionToken is the optional '.up' / '.down' placement word (null = below default).
    public DynamicGreen(SyntaxToken backslashToken, SyntaxToken dynamicToken, SyntaxToken? directionToken = null)
        : base(SyntaxKind.Dynamic, [backslashToken, dynamicToken, directionToken])
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
/// Parallel expression: << expr \\ expr >>
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

// ============================================================
// Tablature Green Nodes
// ============================================================

/// <summary>
/// Tuning declaration: \tuning guitar | \tuning bass
/// </summary>
internal sealed class TuningDeclarationGreen : GreenSyntaxNode
{
    public TuningDeclarationGreen(
        SyntaxToken backslash,
        SyntaxToken tuningKeyword,
        SyntaxToken tuningName)
        : base(SyntaxKind.TuningDeclaration, [backslash, tuningKeyword, tuningName])
    {
    }
}

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
    public SectionDeclarationGreen(
        SyntaxToken sectionKeyword,
        SyntaxToken name,
        SyntaxToken openBrace,
        GreenNode?[] items,
        SyntaxToken closeBrace)
        : base(SyntaxKind.SectionDeclaration, [sectionKeyword, name, openBrace, .. items, closeBrace])
    {
    }
}

/// <summary>
/// Include directive: include "file.lys"
/// </summary>
internal sealed class IncludeDirectiveGreen : GreenSyntaxNode
{
    public IncludeDirectiveGreen(SyntaxToken keyword, SyntaxToken path)
        : base(SyntaxKind.IncludeDirective, [keyword, path])
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
internal sealed class StructureDeclarationGreen : GreenSyntaxNode
{
    public StructureDeclarationGreen(
        SyntaxToken structureKeyword,
        SyntaxToken openBrace,
        GreenNode?[] items,
        SyntaxToken closeBrace)
        : base(SyntaxKind.StructureDeclaration, [structureKeyword, openBrace, .. items, closeBrace])
    {
    }
}

/// <summary>
/// Section reference in structure: SectionName
/// </summary>
internal sealed class SectionReferenceGreen : GreenSyntaxNode
{
    public SectionReferenceGreen(SyntaxToken identifier, SyntaxToken? displayLabel = null)
        : base(SyntaxKind.SectionReference, [identifier, displayLabel])
    {
    }
}

/// <summary>
/// Silent section reference in structure: ~SectionName (no label displayed)
/// </summary>
internal sealed class SilentSectionReferenceGreen : GreenSyntaxNode
{
    public SilentSectionReferenceGreen(SyntaxToken tilde, SyntaxToken identifier)
        : base(SyntaxKind.SilentSectionReference, [tilde, identifier])
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
/// Repeat count: x3
/// </summary>
internal sealed class RepeatCountGreen : GreenSyntaxNode
{
    public RepeatCountGreen(SyntaxToken x, SyntaxToken number)
        : base(SyntaxKind.RepeatCount, [x, number])
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
/// Repeat block in structure: |: ... :| or |: ... :| x3
/// </summary>
internal sealed class StructureRepeatBlockGreen : GreenSyntaxNode
{
    // Simple repeat: |: items :|
    public StructureRepeatBlockGreen(
        SyntaxToken repeatStart,
        GreenNode?[] items,
        SyntaxToken repeatEnd)
        : base(SyntaxKind.StructureRepeatBlock, [repeatStart, .. items, repeatEnd])
    {
    }

    // Repeat with alternatives: |: items | 1. A :| 2. B
    public StructureRepeatBlockGreen(
        SyntaxToken repeatStart,
        GreenNode?[] items,
        SyntaxToken? barline,
        GreenNode?[] alternativesBeforeEnd,
        SyntaxToken repeatEnd,
        GreenNode? finalAlternative)
        : base(SyntaxKind.StructureRepeatBlock, BuildChildren(repeatStart, items, barline, alternativesBeforeEnd, repeatEnd, finalAlternative, null, null))
    {
    }

    // Repeat with count: |: items :| x3
    public StructureRepeatBlockGreen(
        SyntaxToken repeatStart,
        GreenNode?[] items,
        SyntaxToken? barline,
        GreenNode?[] alternativesBeforeEnd,
        SyntaxToken repeatEnd,
        GreenNode? finalAlternative,
        SyntaxToken? xToken,
        SyntaxToken? repeatCount)
        : base(SyntaxKind.StructureRepeatBlock, BuildChildren(repeatStart, items, barline, alternativesBeforeEnd, repeatEnd, finalAlternative, xToken, repeatCount))
    {
    }

    private static GreenNode?[] BuildChildren(
        SyntaxToken repeatStart,
        GreenNode?[] items,
        SyntaxToken? barline,
        GreenNode?[] alternativesBeforeEnd,
        SyntaxToken repeatEnd,
        GreenNode? finalAlternative,
        SyntaxToken? xToken,
        SyntaxToken? repeatCount)
    {
        var children = new List<GreenNode?> { repeatStart };
        children.AddRange(items);
        if (barline != null)
        {
            children.Add(barline);
            children.AddRange(alternativesBeforeEnd);
        }
        children.Add(repeatEnd);
        if (finalAlternative != null)
        {
            children.Add(finalAlternative);
        }
        if (xToken != null)
        {
            children.Add(xToken);
            children.Add(repeatCount);
        }
        return [.. children];
    }
}



/// <summary>
/// Alternative in repeat: 1. A, 2. B or [1. A] or [1-3. A] or [1. ~A]
/// </summary>
internal sealed class StructureAlternativeGreen : GreenSyntaxNode
{
    // Legacy style: 1. A
    public StructureAlternativeGreen(
        SyntaxToken number,
        SyntaxToken dot,
        SyntaxToken sectionName)
        : base(SyntaxKind.StructureAlternative, [number, dot, sectionName])
    {
    }

    // Bracket style: [1. A] or [1-3. A] or [1,3. A] or [1. ~A]
    public StructureAlternativeGreen(
        SyntaxToken openBracket,
        SyntaxToken number,
        SyntaxToken? separator,
        SyntaxToken? endNumber,
        SyntaxToken dot,
        SyntaxToken? tilde,
        SyntaxToken sectionName,
        SyntaxToken? closeBracket)
        : base(SyntaxKind.StructureAlternative,
            BuildSlots(openBracket, number, separator, endNumber, dot, tilde, sectionName, closeBracket))
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
        SyntaxToken? closeBracket)
    {
        // Slot layout (always include tilde slot for consistent indexing):
        // With separator: [openBracket, number, separator, endNumber, dot, tilde?, sectionName, closeBracket]
        // Without separator: [openBracket, number, dot, tilde?, sectionName, closeBracket]
        if (separator != null)
        {
            return [openBracket, number, separator, endNumber, dot, tilde, sectionName, closeBracket];
        }
        else
        {
            return [openBracket, number, dot, tilde, sectionName, closeBracket];
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
/// Grand staff render: grandStaff { staff staff ... }
/// </summary>
internal sealed class GrandStaffRenderGreen : GreenSyntaxNode
{
    public GrandStaffRenderGreen(
        SyntaxToken grandStaffKeyword,
        SyntaxToken openBrace,
        StaffRenderGreen[] staves,
        SyntaxToken closeBrace)
        : base(SyntaxKind.GrandStaffRender, [grandStaffKeyword, openBrace, ..staves, closeBrace])
    {
    }
}

/// <summary>
/// Ossia render: ossia [clef] { partName }
/// LILYPOND-REF: ly/engraver-init.ly — ossia staves use reduced fontSize
/// </summary>
internal sealed class OssiaRenderGreen : GreenSyntaxNode
{
    public OssiaRenderGreen(
        SyntaxToken ossiaKeyword,
        SyntaxToken partName)
        : base(SyntaxKind.OssiaRender, [ossiaKeyword, partName])
    {
    }

    public OssiaRenderGreen(
        SyntaxToken ossiaKeyword,
        SyntaxToken clefName,
        SyntaxToken partName)
        : base(SyntaxKind.OssiaRender, [ossiaKeyword, clefName, partName])
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

/// <summary>
/// Marker indicating the start of a new section.
/// Used to reset relative pitch resolver at section boundaries.
/// </summary>
internal sealed class SectionStartMarkerGreen : GreenSyntaxNode
{
    public static readonly SectionStartMarkerGreen Instance = new();

    private SectionStartMarkerGreen()
        : base(SyntaxKind.SectionStartMarker, [])
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
    // `lyrics [name] { … }` — the optional name binds the lyrics to a same-named
    // voice. When present it sits between the keyword and the brace.
    public LyricsBlockGreen(
        SyntaxToken lyricsKeyword,
        SyntaxToken? name,
        SyntaxToken openBrace,
        GreenNode?[] measures,
        SyntaxToken closeBrace)
        : base(SyntaxKind.LyricsBlock,
            name != null
                ? [lyricsKeyword, name, openBrace, .. measures, closeBrace]
                : [lyricsKeyword, openBrace, .. measures, closeBrace])
    {
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
/// entry grammar as <see cref="ChordNamesBlockGreen"/>, but carries a (required)
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
/// A single chord entry inside a chordnames block: root[duration][:quality][/bass]
/// (e.g. c1, a:m, g2:7, d:m7/f, b:m7.5-). Reuses PitchGreen for the root/bass and
/// DurationGreen for the duration. The quality is a RUN of adjacent tokens (so
/// m7.5- / 7sus4 are captured whole, not just their first token).
/// </summary>
internal sealed class ChordEntryGreen : GreenSyntaxNode
{
    // Slots: 0 root, 1 duration?, 2 colon?, then the quality tokens (0+), then
    // slash?, bass? as the final two slots. Children stay in SOURCE order so node
    // positions / data-pos remain correct.
    public ChordEntryGreen(GreenNode root, GreenNode? duration, SyntaxToken? colon,
        GreenNode?[] qualityTokens, SyntaxToken? slash, GreenNode? bass)
        : base(SyntaxKind.ChordEntry, [root, duration, colon, .. qualityTokens, slash, bass])
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