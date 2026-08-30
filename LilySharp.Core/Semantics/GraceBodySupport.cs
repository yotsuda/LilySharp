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
/// <param name="ViaPhrase">The phrase named in the grace body this drop was reached
/// through, or null when it was written in the body itself. ⚠️ IT IS NOT DECORATION: the
/// span of a drop inside a phrase points at the phrase's DECLARATION, where there is no
/// <c>grace</c> anywhere in sight, so without the name the warning would name a place the
/// reader cannot connect to the sentence — the unnameable subject <c>Describe</c>'s remark
/// argues against, one level up.</param>
internal readonly record struct GraceDrop(
    GraceDropKind Kind, TextSpan Span, string Written, string? ViaPhrase = null);

/// <summary>One element of an expanded grace body: a node the readers judge, and the phrase
/// written in the body that it came out of (null when it was written in the body itself).
/// </summary>
internal readonly record struct GraceBodyElement(SyntaxNode Node, string? ViaPhrase);

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
/// ⚠️ A PHRASE REFERENCE IS NO LONGER ON THAT LIST (session 300). It is the one element in
/// it that names no grob — it names music written elsewhere — so it is expanded in place by
/// <see cref="BodyElements"/> and whatever the phrase holds goes through this narrowing
/// instead. The three that stay (a chord, a rest, a tuplet) share only the SYMPTOM, that a
/// body holding one of them alone engraves no grace at all.
/// </para>
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
    /// The elements a grace body offers its readers, in source order, with every PHRASE
    /// REFERENCE replaced in place by the elements of the phrase it names — bracketed by
    /// the same <see cref="Svg.Collector.RelativeResetMarker"/> /
    /// <see cref="Svg.Collector.PhraseEndMarker"/> pair the ordinary walk uses, so the
    /// collector gives a phrase body the fresh relative frame every other call site gives it.
    /// </summary>
    /// <remarks>
    /// ⚠️ A PHRASE REFERENCE IS A CONTAINER, NOT A GROB, and that is why it is expanded here
    /// rather than waiting for the trip that walks a grace body with the ordinary walker.
    /// Session 298 sorted the drops into "wants the host note's column" and "wants none", and
    /// filed a phrase reference with the chords and the rests because a body holding only one
    /// engraves NO GRACE AT ALL. That symptom is shared; the repair is not. A chord, a rest
    /// and a tuplet each need a grob a grace column cannot hold yet, and building one here
    /// would be the second spelling of chord/rest layout that
    /// <c>ArticulationEngraver</c>'s "THE SAME ENGRAVER, NOT A SECOND SPELLING" argues
    /// against. A reference holds no grob at all: it names music written elsewhere, and every
    /// other container in this grammar already expands one — <c>tuplet { A }</c>,
    /// <c>cue { A }</c> and <c>repeat unfold 2 { A }</c> all do (measured on
    /// scratch/p194/four-containers.lys, the book written in session 194 to check exactly
    /// these four; grace was the only one that dropped it, for 106 sessions).
    /// <para>
    /// ⚠️ ONE WALK, TWO READERS, which is why the phrase table arrives as
    /// <paramref name="resolvePhrase"/> instead of being read here: the collector resolves
    /// from its own <c>_variables</c> and <see cref="GraceBodyValidator"/> from the tree's
    /// declarations, and if each expanded the body its own way the validator would go on
    /// reporting a reference the collector had started engraving.
    /// </para>
    /// <para>
    /// LILYPOND-REF: lily/parser.yy identifier substitution — a LilyPond variable is
    /// substituted wherever it is named, and a grace body is not an exception. MEASURED on
    /// 2.26.0 (scratch/p300/lp): with <c>G = { d'16 e' }</c>, <c>\grace { \G }</c> and
    /// <c>\grace { d'16 e' }</c> render BYTE-IDENTICAL (8379 bytes, SHA 1EC0BE9A4B9E), and
    /// both differ from the book with no grace at all. ⚠️ The FRESH FRAME below is Lily#'s
    /// own rule rather than that one: LilyPond has no phrase, its variables carry no relative
    /// frame of their own, and a Lily# phrase evaluating in a fresh frame is the grammar's
    /// decision everywhere (MeasureCollector.ExpandVariable). This makes a grace body agree
    /// with the rest of the grammar, not with LilyPond's substitution rule.
    /// </para>
    /// <para>
    /// ⚠️ IT IS A SECOND SPELLING OF PHRASE EXPANSION, and it is one on purpose — checklist
    /// 7.7 names "the same quantity's second spelling" as this repository's most repeated
    /// defect, so the reason it cannot be folded belongs here rather than nowhere.
    /// <c>MeasureCollector.ExpandVariable</c> cannot serve: it is an instance method reading
    /// <c>_variables</c> and <c>ChargeExpansion</c>, which the validator has neither of, and
    /// it flattens with <c>MusicSitesLazy</c>, which DESCENDS into containers — right for the
    /// main stream, wrong here, where a tuplet inside a grace body has to stay one element so
    /// the narrowing can name it. ⇒ Per 7.7's own answer for a pair that cannot be folded,
    /// the two are tied by a DIFFERENTIAL net rather than a shared house:
    /// <c>GraceBodyValidatorTests.APhraseInAGraceBody_HandsTheChainBackAtItsAnchor</c> and
    /// <c>APhraseInAGraceBody_OffersTheSameBodyTheMainStreamDoes</c> put the same phrase
    /// through both expanders and demand the same pitches and the same hand-off.
    /// </para>
    /// <para>
    /// ⚠️ <paramref name="charge"/> IS THE EXPANSION BUDGET, not a nicety: an acyclic phrase
    /// DAG doubles per level, and this walk runs on the LSP's per-keystroke diagnostics pass.
    /// A phrase whose entry cannot be paid for emits NOTHING — no reset marker and no end
    /// marker, balanced by omission — exactly as <c>MeasureCollector.ExpandVariable</c> does,
    /// so a spent budget can never leave the collector's frame stack half-open.
    /// </para>
    /// </remarks>
    internal static List<GraceBodyElement> BodyElements(
        GraceExpressionSyntax grace,
        Func<string, SyntaxNode?> resolvePhrase,
        Func<bool> charge)
    {
        var elements = new List<GraceBodyElement>();
        Expand(grace.Body.Items, resolvePhrase, charge, new HashSet<string>(), elements,
            via: null);
        return elements;
    }

    private static void Expand(
        IEnumerable<SyntaxNode> items,
        Func<string, SyntaxNode?> resolvePhrase,
        Func<bool> charge,
        HashSet<string> active,
        List<GraceBodyElement> into,
        string? via)
    {
        foreach (var item in items)
        {
            // A reference that resolves and is not already open on the active chain is the
            // ONLY thing that expands. An undeclared name (SymbolReferenceValidator's
            // business) and a cycle (PhraseCycleValidator's) both fall through to the drop
            // arm below, so the reference itself is still named as what did not reach the page.
            if (item is VariableReferenceSyntax reference
                && resolvePhrase(reference.Name.Text) is { } body
                && active.Add(reference.Name.Text))
            {
                if (charge())
                {
                    into.Add(new GraceBodyElement(
                        Svg.Collector.RelativeResetMarker.For(
                            reference.OctaveOffset,
                            Music.PhraseAnchor.AnchorStep(body, resolvePhrase)),
                        via));
                    // The name that travels down is the OUTERMOST one — the phrase the
                    // reader wrote in the grace body. A drop three phrases deep is still
                    // reached through the one word they can see next to `grace`.
                    Expand(BodyItemsOf(body), resolvePhrase, charge, active, into,
                        via ?? reference.Name.Text);
                    into.Add(new GraceBodyElement(Svg.Collector.PhraseEndMarker.Instance, via));
                }
                active.Remove(reference.Name.Text);
                continue;
            }

            if (charge())
                into.Add(new GraceBodyElement(item, via));
        }
    }

    /// <summary>The elements of a resolved phrase body. A phrase's body and a grace's body
    /// are the SAME node type, so the two are read the same way; a variable declared to a
    /// bare expression (<c>MeasureCollector._variables</c> also holds those) stands for
    /// itself and goes through the narrowing unflattened, the way a written element does.
    /// </summary>
    private static IEnumerable<SyntaxNode> BodyItemsOf(SyntaxNode body)
        => body is MusicBlockSyntax block ? block.Items : new[] { body };

    /// <summary>True for the frame markers <see cref="BodyElements"/> brackets an expanded
    /// phrase with: they are instructions to the collector, not music, so neither the drop
    /// list nor the "engraves nothing" question may count them.</summary>
    private static bool IsFrameMarker(SyntaxNode element)
        => element is Svg.Collector.RelativeResetMarker or Svg.Collector.PhraseEndMarker;

    /// <summary>
    /// Everything in <paramref name="elements"/> that the collector will not engrave,
    /// in source order, each at the span it was written at.
    /// </summary>
    internal static IEnumerable<GraceDrop> Drops(IReadOnlyList<GraceBodyElement> elements)
    {
        foreach (var (item, via) in elements)
        {
            if (IsFrameMarker(item))
                continue;

            if (CarriedNote(item) is not { } note)
            {
                // Not a bare note. The KIND is what the reader needs to hear, so name the
                // node rather than quoting the source — a tuplet body would otherwise be
                // quoted whole into a console line.
                yield return new GraceDrop(KindOf(item), item.Span, Describe(item), via);
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
                    GraceDropKind.Annotation, annotation.Span, Describe(annotation), via);
            }
        }
    }

    /// <summary>True when the body engraves no grace group at all — every element in it is
    /// one the collector does not read, so there is no column left to attach.</summary>
    internal static bool EngravesNothing(IReadOnlyList<GraceBodyElement> elements)
    {
        foreach (var (item, _) in elements)
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
        // Only an UNDECLARED or CYCLIC name reaches here now — BodyElements expands every
        // reference it can resolve. The word stays because the two that are left are still
        // written as one, and a reader who sees it is looking for the name they typed.
        VariableReferenceSyntax => "a phrase reference",
        CueExpressionSyntax => "a cue",
        RepeatExpressionSyntax => "a repeat",
        DynamicSyntax => "a dynamic",
        StringNumberAnnotationSyntax => "a string number",
        ArticulationSyntax or MusicMarkSyntax => "an annotation",
        _ => "what is written here",
    };
}
