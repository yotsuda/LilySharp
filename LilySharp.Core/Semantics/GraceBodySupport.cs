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

using System.Collections.Generic;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>What a <c>grace { … }</c> body loses, by kind.</summary>
internal enum GraceDropKind
{
    /// <summary>An element that is not a bare note — a chord, a rest, a tuplet, a nested
    /// container. It contributes NO grace column, so a body made only of these engraves
    /// no grace at all.</summary>
    Element,
    /// <summary>An annotation written on a grace note.</summary>
    Annotation,
    /// <summary>A slur, beam or tie marker written inside the body.</summary>
    Span,
}

/// <summary>One thing a grace body holds that does not reach the page, at the span it
/// was written at.</summary>
internal readonly record struct GraceDrop(GraceDropKind Kind, TextSpan Span, string Written);

/// <summary>
/// THE statement of what a <c>grace { … }</c> body carries today, written once and read
/// twice: <see cref="Svg.Collector.MeasureCollector"/> takes from it what it engraves, and
/// <see cref="GraceBodyValidator"/> reports what is left over.
/// </summary>
/// <remarks>
/// ⚠️ THIS IS A NARROWING, NOT A GRAMMAR. A grace body is parsed by the ordinary
/// <c>ParseMusicBlock</c>, so it holds everything a music block can hold — chords, rests,
/// tuplets, slur/beam/tie markers, every annotation. The collector then reads a bare
/// <see cref="NoteSyntax"/>'s PITCH and DURATION VALUE and nothing else, which is why the
/// narrowing has to be stated somewhere a reader can find. MEASURED 2026-08-30 (session
/// 298) by rendering each spelling against a control and comparing the SVG with
/// <c>data-pos</c> masked: chord, rest, tuplet, dots, slur, beam, tie, <c>@staccato</c>,
/// <c>@text</c>, <c>@f</c>, <c>@finger</c>, <c>@trill</c>, <c>@sustain</c>, <c>@rit</c>,
/// <c>@cresc</c> — every one of them byte-identical to the control, i.e. dropped, and a
/// chord or a rest or a tuplet as the body's only element removes the whole grace group.
/// <para>
/// ⚠️ TWO ANNOTATIONS ARE CARRIED, AND THE LINE BETWEEN THEM AND THE REST IS "does it want
/// a COLUMN". The dropped families all want the note's column and a grace note has no
/// <c>itemIndex</c> to give them. These two want none:
/// <list type="bullet">
/// <item>the REHEARSAL MARK, whose grob is the SCORE's — LilyPond consists
/// <c>Mark_engraver</c> in the Score context (ly/engraver-init.ly:729 <c>\name Score</c>,
/// :764), so it never belonged to the note's Voice and a grace Voice being a separate
/// context cannot stand between them. Lily# says the same by building it with no
/// <c>itemIndex</c> (<c>MeasureCollector.CollectArticulations</c>: "a mark belongs to the
/// BAR"). MEASURED on LilyPond 2.26.0: <c>\grace { d'8^\markup{x} \mark "P" }</c> prints
/// BOTH the P and the x (scratch/p298/lpmark.svg) — the x is the half Lily# still loses.</item>
/// <item>the STRING NUMBER <c>\N</c>, which is not a grob at all: it draws nothing on a
/// notation staff (MEASURED — <c>c'4\2</c> and <c>c'4</c> render byte-identical) and is only
/// an input to <c>Tunings.CalculateFret</c>. It rides
/// <see cref="Svg.Model.GraceNoteInfo.StringNumber"/> rather than being built as an item.</item>
/// </list>
/// ⚠️ The string number was NOT free of consequence while it was dropped: the reader's own
/// <c>Real Gone.lys</c> writes <c>grace { a,16\2 }</c> twice, and both were drawn on whatever
/// string the resolver picked. Found by LYS4020 the day it was written.
/// </para>
/// <para>
/// ⇒ The real repair is to WALK the body with the ordinary walker, the way
/// <c>ProcessCueRegion</c> walks a cue region, so that everything a voice can hold works
/// inside it unchanged. That needs an address a grace note can be found at — a grace note
/// is not a measure item, so the <c>itemIndex</c> every annotation family anchors on has
/// nothing to name — and it is its own trip. See docs/HANDOFF.md §2 U8.
/// </para>
/// </remarks>
internal static class GraceBodySupport
{
    /// <summary>The one element a grace column is built from, or null for everything else.
    /// <c>MeasureCollector.CollectGraceNotes</c> asks this and nothing else.</summary>
    internal static NoteSyntax? CarriedNote(SyntaxNode item) => item as NoteSyntax;

    /// <summary>
    /// True for an annotation that asks for NO COLUMN — so that writing it on a grace note
    /// is not writing it on the grace's column at all, and a grace note not being a measure
    /// item takes nothing away from it. The remarks above name the two and say why this is a
    /// category rather than a favour.
    /// </summary>
    internal static bool NeedsNoColumn(SyntaxNode annotation) => annotation switch
    {
        MusicMarkSyntax mark => AnnotationValues.Rehearsal(mark, out _) != null,
        StringNumberAnnotationSyntax => true,
        _ => false,
    };

    /// <summary>
    /// The <c>\N</c> a grace note carries, or null for automatic string selection.
    /// </summary>
    /// <remarks>
    /// ⚠️ IT GOES THROUGH <see cref="NeedsNoColumn"/> ON PURPOSE, although reading the
    /// annotation directly would be shorter. That predicate is the single sentence the drop
    /// list and the collector both read; routing the collector's answer through it is what
    /// makes the link OBSERVABLE — poison the predicate and the string stops being honoured
    /// AND starts being reported, in one edit. A collector that read the node type itself
    /// would keep drawing the string while the validator called it dropped, which is the
    /// drift this whole file exists to prevent.
    /// </remarks>
    internal static int? CarriedStringNumber(NoteSyntax note)
    {
        foreach (var annotation in note.Articulations)
            if (NeedsNoColumn(annotation) && annotation is StringNumberAnnotationSyntax s)
                return s.StringNumber;
        return null;
    }

    /// <summary>
    /// Everything in <paramref name="grace"/>'s body that the collector will not engrave,
    /// in source order, each at the span it was written at.
    /// </summary>
    internal static IEnumerable<GraceDrop> Drops(GraceExpressionSyntax grace)
    {
        foreach (var item in grace.Body.Items)
        {
            if (CarriedNote(item) is not { } note)
            {
                // Not a bare note. The KIND is what the reader needs to hear, so name the
                // node rather than quoting the source — a tuplet body would otherwise be
                // quoted whole into a console line.
                yield return new GraceDrop(KindOf(item), item.Span, Describe(item));
                continue;
            }

            // ⚠️ THE DOTS ARE NOT HERE ANY MORE (session 299). They were the one dropped
            // family that never wanted the note's COLUMN — a dot hangs off the grace's own
            // head, in the grace's own font, so nothing about a grace not being a measure
            // item stood in its way. Session 298 filed it with the annotations because it
            // sorted the drops by FAMILY; the line that actually divides them is which
            // column a thing asks for. See GraceNoteInfo.Dots and Svg.Layout.DotColumn.
            foreach (var annotation in note.Articulations)
            {
                if (NeedsNoColumn(annotation))
                    continue;
                yield return new GraceDrop(
                    GraceDropKind.Annotation, annotation.Span, Describe(annotation));
            }
        }
    }

    /// <summary>True when the body engraves no grace group at all — every element in it is
    /// one the collector does not read, so there is no column left to attach.</summary>
    internal static bool EngravesNothing(GraceExpressionSyntax grace)
    {
        foreach (var item in grace.Body.Items)
            if (CarriedNote(item) != null)
                return false;
        return true;
    }

    /// <summary>Which family a dropped body element belongs to. A slur/beam/tie MARKER is
    /// a sibling in the flat list rather than a child of the note it was written after
    /// (the parser replays it from <c>_pendingPostEventMarkers</c>), so it arrives here
    /// looking like an element and has to be told apart from one.</summary>
    private static GraceDropKind KindOf(SyntaxNode item) => item switch
    {
        SlurSyntax or BeamMarkerSyntax or TieSyntax => GraceDropKind.Span,
        _ => GraceDropKind.Element,
    };

    /// <summary>
    /// The word a diagnostic uses for a dropped node.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE FALLBACK IS A SENTENCE, NOT A PRONOUN. It read "this" until the first sweep,
    /// which turned up two shapes nobody had listed — a phrase reference (<c>grace { A }</c>,
    /// scratch/p194/four-containers.lys) and a string number (the reader's `Real Gone.lys`) —
    /// and both printed "this inside 'grace { }' is not engraved", which names nothing a
    /// reader can look for. A warning whose subject is unnameable is a warning about the
    /// file, the same failure LYS4018 and LYS4019 are each written against.
    /// </remarks>
    private static string Describe(SyntaxNode node) => node switch
    {
        ChordSyntax => "a chord",
        RestSyntax => "a rest",
        TupletExpressionSyntax => "a tuplet",
        SlurSyntax => "a slur mark",
        BeamMarkerSyntax => "a beam bracket",
        TieSyntax => "a tie",
        GraceExpressionSyntax => "a nested grace",
        VariableReferenceSyntax => "a phrase reference",
        CueExpressionSyntax => "a cue",
        RepeatExpressionSyntax => "a repeat",
        DynamicSyntax => "a dynamic",
        StringNumberAnnotationSyntax => "a string number",
        ArticulationSyntax or MusicMarkSyntax => "an annotation",
        _ => "what is written here",
    };
}
