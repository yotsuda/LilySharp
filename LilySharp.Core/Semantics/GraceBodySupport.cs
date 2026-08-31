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
    /// <summary>An element that makes no grace column — a rest, an empty chord, a nested
    /// container. A body made only of these engraves no grace at all.</summary>
    /// <remarks>
    /// ⚠️ A CHORD IS NOT ONE OF THESE ANY MORE (session 308): it is one column with N heads,
    /// and <see cref="GraceBodySupport.CarriedChord"/> reads it. What is left in this kind is
    /// the REST, whose repair is not this one — a rest is a column with no head at all, and
    /// LilyPond's beam then covers only the leading run of heads, which is the same model
    /// change the beam half of docs/HANDOFF.md §2 U8 ⒞ needs.
    /// </remarks>
    Element,
    /// <summary>An annotation written on a grace note.</summary>
    Annotation,
    /// <summary>A slur, beam or tie marker written inside the body.</summary>
    Span,
    /// <summary>The BRACKET AND NUMBER of a tuplet written inside the body. The notes the
    /// tuplet holds ARE engraved - it is a container, see
    /// <see cref="Svg.Collector.GraceTupletStartMarker"/> - so this is the one kind that
    /// names a decoration lost off music that DID reach the page, rather than music that
    /// did not.</summary>
    Bracket,
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
/// THE statement of what a <c>grace { … }</c> body carries today, written once and read by
/// FOUR walks: <see cref="Svg.Collector.MeasureCollector"/> takes from it what it engraves,
/// <see cref="GraceBodyValidator"/> reports what is left over,
/// <see cref="Midi.MidiExporter"/> plays it and <see cref="MusicXml.MusicXmlExporter"/>
/// exports it.
/// </summary>
/// <remarks>
/// ⚠️ IT SAID "READ TWICE" FOR A DAY, AND THE OTHER TWO READERS WERE WALKING
/// <c>grace.Body.Items</c> THEMSELVES. Session 300 taught the first two that a phrase
/// reference is a container; session 301 MEASURED (2026-08-30, scratch/p301/ab) that
/// <c>grace { G } c'4 c'2.</c> then engraved a page byte-identical to the inline spelling —
/// and to the <c>.ly</c> twin — while its MIDI was byte-identical to the book with NO GRACE
/// IN IT and its MusicXML held no <c>&lt;grace/&gt;</c> at all: two grace notes on the page
/// that nobody could hear. ⇒ When a file claims "one statement, N readers", COUNT the N — a
/// grep for the walks that take a <c>GraceExpressionSyntax</c> answered it in thirty seconds.
/// (The fifth walk, <c>LilyPond.LilyPondExporter.EmitGrace</c>, is not a reader of this
/// statement: it re-emits the written source and narrows nothing, so it was right already.)
/// <para>
/// ⚠️ THE FOUR STILL DISAGREE BELOW THE CONTAINER, and that is the open half of
/// docs/HANDOFF.md §2 U8 rather than an oversight. A CHORD no longer does (session 308): all
/// four carry it. A REST still does — it has SOUNDED since 2026-07-10 while the page and the
/// XML drop it — because a rest is a column with NO head, and LilyPond then beams only the
/// LEADING run of heads (measured in session 302's member table), which is the same model
/// change the beam half of §2 U8 ⒞ needs and not this one.
/// </para>
/// <para>
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
/// </para>
/// <para>
/// ⚠️ A PHRASE REFERENCE IS NO LONGER ON THAT LIST (session 300), NOR IS A TUPLET (session
/// 302), NOR IS A CHORD (session 308). The first two are CONTAINERS — they name no grob of
/// their own — so both are expanded in place by <see cref="BodyElements"/> and whatever they
/// hold goes through this narrowing instead. Session 298 filed all four together because a
/// body holding one of them alone engraves no grace at all; that is the SYMPTOM, and sorting
/// by it put two containers in a box with two grobs.
/// <para>
/// The CHORD was the third mis-sort, and it took a fourth reading to see: session 302 filed it
/// with the REST under one difficulty ("the model, not the address"), and the two are not one
/// difficulty either. A chord is N heads on ONE column, which changes nothing about the beam —
/// MEASURED on LilyPond (session 302's member table: a chord anywhere in a grace run leaves
/// polygon 2 and adds only its heads) — so it is <see cref="Svg.Model.GraceColumnInfo"/>'s one
/// word and the ordinary chord engravers at the grace's fonts. A rest is a column with NO
/// head, and LilyPond then beams only the LEADING run of heads and flags the rest, which is
/// the partial-beam-group model §2 U8 ⒞ names. Same box, different repairs — for the fourth
/// time in this one ticket.
/// </para>
/// </para>
/// <para>
/// ⚠️ A TUPLET STILL DROPS ITS BRACKET AND ITS NUMBER, reported as
/// <see cref="GraceDropKind.Bracket"/> rather than <see cref="GraceDropKind.Element"/>: the
/// notes are engraved and the decoration is not, so the sentence the reader gets has to say
/// which half they lost. MEASURED on LilyPond 2.26.0 (session 301, scratch/p301/lp): what
/// <c>\tuplet 3/2</c> adds inside a grace body is the italic serif <c>3</c>, plus the four
/// bracket lines once the durations are long enough that no beam stands in for them — the
/// three notes are byte-identical to the untupleted spelling, coordinates included.
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
/// <see cref="Svg.Model.GraceHeadInfo.StringNumber"/> rather than being built as an item.</item>
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
    /// <summary>A grace column built from ONE pitch, or null for everything else.</summary>
    internal static NoteSyntax? CarriedNote(SyntaxNode item) => item as NoteSyntax;

    /// <summary>
    /// A grace column built from SEVERAL pitches, or null for everything else — a chord is
    /// ONE column with N heads, not N columns (session 308).
    /// </summary>
    /// <remarks>
    /// ⚠️ THE EMPTY CHORD <c>&lt;&gt;</c> IS NOT ONE. It has no member of any kind and
    /// occupies no time — it is a carrier for post-events (see
    /// <see cref="Syntax.ChordSyntax.IsEmpty"/>, whose remarks record what asking that
    /// question two different ways once cost) — so it makes no column and stays on the drop
    /// list, where its post-events are reported like any other body element's.
    /// </remarks>
    internal static ChordSyntax? CarriedChord(SyntaxNode item)
        => item is ChordSyntax { IsEmpty: false } chord ? chord : null;

    /// <summary>
    /// A grace column that sounds NOTHING, or null for everything else — a rest is a column
    /// with no head at all (session 308).
    /// </summary>
    /// <remarks>
    /// ⚠️ A MULTI-MEASURE REST IS NOT ONE. <c>R</c> is a spanner over whole bars, and a grace
    /// group is not a bar; it stays on the drop list rather than being silently drawn as an
    /// ordinary rest, which is what reading <see cref="RestSyntax"/> unconditionally would
    /// have done.
    /// </remarks>
    internal static RestSyntax? CarriedRest(SyntaxNode item)
        => item is RestSyntax rest && rest.RestToken.Text != "R" ? rest : null;

    /// <summary>
    /// True for a body element that becomes a grace COLUMN. THE question the collector, the
    /// validator and the "engraves nothing" test all ask, so the three cannot drift apart.
    /// </summary>
    internal static bool IsCarried(SyntaxNode item)
        => CarriedNote(item) != null || CarriedChord(item) != null || CarriedRest(item) != null;

    /// <summary>
    /// The kinds a grace body engraves, worded as <see cref="GraceBodyValidator"/> says them
    /// to the reader.
    /// </summary>
    /// <remarks>
    /// ⚠️ IT LIVES HERE, TOUCHING <see cref="IsCarried"/>, BECAUSE THE SENTENCE WENT STALE
    /// TWICE IN ONE SESSION. It read "bare notes only" while a chord had just become a column,
    /// and then "notes and chords only" while a rest had (session 308, both halves). A list in
    /// a message is a second spelling of a predicate: nothing goes red when the predicate
    /// grows, because a message is not an assertion. Keeping the two lines adjacent does not
    /// make it impossible — it makes it visible to whoever adds the next kind.
    /// </remarks>
    internal const string CarriedKinds = "notes, chords and rests";

    /// <summary>
    /// The annotations written on a carried element: a note's own, and for a chord both the
    /// chord-level ones and every member's.
    /// </summary>
    /// <remarks>
    /// ⚠️ A MEMBER'S ANNOTATIONS COUNT. <c>&lt;c@staccato e&gt;</c> loses that script exactly
    /// as <c>c@staccato</c> does, and reporting only the chord-level list would have made the
    /// warning silently narrower than the loss the moment a chord became a column.
    /// </remarks>
    internal static IEnumerable<SyntaxNode> CarriedAnnotations(SyntaxNode item)
    {
        if (CarriedNote(item) is { } note)
        {
            foreach (var a in note.Articulations)
                yield return a;
            yield break;
        }
        if (CarriedChord(item) is { } chord)
        {
            foreach (var a in chord.Articulations)
                yield return a;
            foreach (var pitch in chord.Pitches)
                foreach (var a in pitch.Articulations)
                    yield return a;
            yield break;
        }
        if (CarriedRest(item) is { } rest)
        {
            foreach (var a in rest.Articulations)
                yield return a;
        }
    }

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
    /// The elements a grace body offers its readers, in source order, with every CONTAINER
    /// replaced in place by what it holds: a PHRASE REFERENCE by the elements of the phrase
    /// it names, bracketed by the same <see cref="Svg.Collector.RelativeResetMarker"/> /
    /// <see cref="Svg.Collector.PhraseEndMarker"/> pair the ordinary walk uses so the
    /// collector gives a phrase body the fresh relative frame every other call site gives it;
    /// a TUPLET by its body, bracketed by
    /// <see cref="Svg.Collector.GraceTupletStartMarker"/> /
    /// <see cref="Svg.Collector.GraceTupletEndMarker"/> so each reader can borrow the ratio
    /// it reads and give it back at the close.
    /// </summary>
    /// <remarks>
    /// ⚠️ A CONTAINER IS NOT A GROB, and that is why the two of them are expanded here
    /// rather than waiting for the trip that walks a grace body with the ordinary walker.
    /// Session 298 sorted the drops into "wants the host note's column" and "wants none", and
    /// filed a phrase reference and a tuplet with the chords and the rests because a body
    /// holding only one engraves NO GRACE AT ALL. That symptom is shared; the repair is not.
    /// A chord and a rest each need a grob a grace column cannot hold yet, and building one
    /// here would be the second spelling of chord/rest layout that
    /// <c>ArticulationEngraver</c>'s "THE SAME ENGRAVER, NOT A SECOND SPELLING" argues
    /// against. A reference holds no grob at all — it names music written elsewhere — and
    /// every other container in this grammar already expands one: <c>tuplet { A }</c>,
    /// <c>cue { A }</c> and <c>repeat unfold 2 { A }</c> all do (measured on
    /// scratch/p194/four-containers.lys, the book written in session 194 to check exactly
    /// these four; grace was the only one that dropped it, for 106 sessions).
    /// <para>
    /// ⚠️ A TUPLET IS THE SECOND CONTAINER (session 302), and it holds a grob the others do
    /// not: the BRACKET AND THE NUMBER. Expanding it therefore closes three quarters of the
    /// hole and leaves a quarter reported — the notes are engraved, at their WRITTEN
    /// durations, and the decoration is still a <see cref="GraceDropKind.Bracket"/> drop.
    /// MEASURED before the trip started (scratch/p302/ab, both sides Release, data-pos
    /// masked): <c>grace { tuplet 3/2 { d'16 e' f' } } c'4 c'2.</c> rendered a page, a MIDI
    /// file and a MusicXML file each BYTE-IDENTICAL to the book with no grace in it at all,
    /// while the <c>.ly</c> twin (which narrows nothing) wrote the tuplet out correctly. All
    /// three narrowing readers had it, which is why one expansion fixes all three.
    /// </para>
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
            // A TUPLET is the other container. It opens no frame — a tuplet in the main
            // stream does not reset the relative octave and does not reset the running
            // duration, so `tuplet 3/2 { d'16 e' f' } c'` gives the c a sixteenth — and the
            // markers carry only the RATIO, which the page ignores and the two exporters
            // multiply into the arithmetic they already keep for the main stream. There is
            // no cycle to guard: a tuplet names nothing.
            if (item is TupletExpressionSyntax tuplet)
            {
                if (charge())
                {
                    into.Add(new GraceBodyElement(
                        new Svg.Collector.GraceTupletStartMarker(tuplet), via));
                    Expand(BodyItemsOf(tuplet.Body), resolvePhrase, charge, active, into, via);
                    into.Add(new GraceBodyElement(
                        Svg.Collector.GraceTupletEndMarker.Instance, via));
                }
                continue;
            }

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

    /// <summary>True for the markers <see cref="BodyElements"/> brackets an expanded
    /// container with: they are instructions to the readers, not music, so neither the drop
    /// list nor the "engraves nothing" question may count them.</summary>
    /// <remarks>
    /// ⚠️ <see cref="Svg.Collector.GraceTupletStartMarker"/> IS ONE OF THESE AND STILL
    /// PRODUCES A DROP. <see cref="Drops"/> takes it before this predicate, because the
    /// bracket and the number are lost even though the notes around them are not — the two
    /// questions ("is this music?" and "did the reader lose something here?") have different
    /// answers for it, which is the whole reason it is a separate kind.
    /// </remarks>
    private static bool IsFrameMarker(SyntaxNode element)
        => element is Svg.Collector.RelativeResetMarker or Svg.Collector.PhraseEndMarker
                   or Svg.Collector.GraceTupletStartMarker or Svg.Collector.GraceTupletEndMarker;

    /// <summary>
    /// Everything in <paramref name="elements"/> that the collector will not engrave,
    /// in source order, each at the span it was written at.
    /// </summary>
    internal static IEnumerable<GraceDrop> Drops(IReadOnlyList<GraceBodyElement> elements)
    {
        foreach (var (item, via) in elements)
        {
            // The container was expanded, so its NOTES are gone from this list and its
            // decoration is what is left to report. The span comes off the tuplet as
            // WRITTEN: the marker is zero-width and stands at position 0, so on its own it
            // could underline nothing.
            if (item is Svg.Collector.GraceTupletStartMarker tuplet)
            {
                yield return new GraceDrop(
                    GraceDropKind.Bracket, tuplet.Written.Span, Describe(tuplet.Written), via);
                continue;
            }

            if (IsFrameMarker(item))
                continue;

            if (!IsCarried(item))
            {
                // Not a column. The KIND is what the reader needs to hear, so name the
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
            // column a thing asks for. See GraceColumnInfo.Dots and Svg.Layout.DotColumn.
            foreach (var annotation in CarriedAnnotations(item))
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
            if (IsCarried(item))
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
        // Reached only through GraceTupletStartMarker now: the tuplet itself is expanded,
        // and what is reported is the decoration the expansion cannot carry.
        TupletExpressionSyntax => "the bracket and number of a tuplet",
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
