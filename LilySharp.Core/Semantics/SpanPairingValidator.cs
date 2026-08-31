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

using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Semantics;

/// <summary>
/// Reports a SPAN mark that pairs with nothing — a start (<c>@rit</c>, <c>@textSpan("…")</c>,
/// <c>@ottava</c>) that no <c>@!</c> ever closes, a <c>@!</c> with no span open in its voice,
/// or a second start written inside an open one. Every one of them draws nothing, and until
/// the terminator existed nothing could say so: the length was an engine default the reader
/// was never told about.
/// <para>
/// ⚠️ THE UNTERMINATED FAULT IS AN ERROR; the other two are warnings. One code, two
/// severities - the reason is on the loop.
/// </para>
/// </summary>
/// <remarks>
/// Like <see cref="SlurPairingValidator"/>, this reads back what the shared collector
/// already decided (<see cref="MeasureCollector.UnpairedSpanWarnings"/>) rather than
/// re-deciding the pairing — the reading and the drawing are two halves of ONE call per
/// family (<c>TextSpannerEngraver.PairTextSpanners</c>,
/// <c>OttavaBracketEngraver.PairOttavaBrackets</c>), so a mark cannot be warned about and
/// drawn at the same time.
/// <para>
/// ⚠️ ONE VALIDATOR AND ONE CODE FOR EVERY FAMILY, which is the point rather than a saving:
/// the language now holds ONE answer to "what happens to a span nobody closed", and a
/// second diagnostic code would be a second answer waiting to drift. What differs per
/// family is only the words.
/// </para>
/// <para>
/// LILYPOND-REF: lily/text-spanner-engraver.cc:59-88 Text_spanner_engraver::process_music,
/// :117-127 Text_spanner_engraver::finalize — the three faults are that engraver's three
/// warnings, in the same three situations.
/// ⚠️ THE OTTAVA IS A DECLARED DIVERGENCE, not a port of the same shape:
/// lily/ottava-engraver.cc:220-226 Ottava_spanner_engraver::finalize neither warns nor
/// suicides, so LilyPond draws an unterminated ottava to the end in silence. See
/// docs/APPROXIMATIONS.md.
/// </para>
/// </remarks>
internal sealed class SpanPairingValidator : ISharedCollectValidator
{
    private readonly DiagnosticBag _diagnostics = new();

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.ToList();

    public void Validate(SyntaxTree tree) =>
        ValidateWith(tree, new System.Lazy<MeasureCollector?>(
            () => SemanticValidation.TryCollect(tree)));

    public void ValidateWith(SyntaxTree tree, System.Lazy<MeasureCollector?> sharedCollect)
    {
        // A malformed score (null collector) surfaces its real error elsewhere.
        var warnings = sharedCollect.Value?.UnpairedSpanWarnings;
        if (warnings == null)
            return;

        foreach (var w in warnings)
        {
            // ⚠️ ONE CODE, TWO SEVERITIES, and the split is GRAMMAR.md's rather than a
            // judgement call. An end is REQUIRED for BOTH families - the text spanner sugar
            // says so ("each ended by '@!rit' / '@!accel' / '@!rall' - an end is REQUIRED")
            // and the ottava says "an end is REQUIRED here too" - so a span nobody ends is a
            // book that does not say what it means, and it is refused (owner's decision
            // 2026-08-31). The other two faults are different mistakes the grammar does not
            // speak to - a '@!' that closes nothing, and a second start inside an open span -
            // and both stay warnings.
            //
            // ⚠️ IT WAS A WARNING, AND THAT WAS THE HOLE. Nothing is drawn either way, so a
            // book with a dropped '@!rit' passed 'lysc check' and shipped with its rit.
            // silently absent. The grammar had said REQUIRED since the terminator landed; only
            // the severity had not caught up. REACH WHEN IT CHANGED: 1975 books on disk, 41
            // report an unterminated span and all 41 are past sessions' scratch probes - no
            // tracked book, none of the author's.
            //
            // ASCII punctuation only: these strings reach legacy-codepage consoles via the CLI.
            var span = new TextSpan(w.SourcePosition, 1);
            string message = MessageFor(w.Kind, w.Fault);
            if (w.Fault == SpanPairingFault.Unterminated)
                _diagnostics.Error(span, DiagnosticCodes.UnpairedSpan, message);
            else
                _diagnostics.Warning(span, DiagnosticCodes.UnpairedSpan, message);
        }
    }

    /// <summary>The words for one fault, in the family's own vocabulary.</summary>
    /// <remarks>
    /// The NOUN and the terminator's spelling are all that vary; the sentence a reader has to
    /// act on ("nothing is drawn, write the end here") is the same because the rule is.
    /// </remarks>
    private static string MessageFor(SpanKind kind, SpanPairingFault fault)
    {
        bool ottava = kind == SpanKind.Ottava;
        string noun = ottava ? "an ottava bracket" : "a text spanner";
        string ends = ottava ? "'@!ottava'" : "'@!rit' (or '@!textSpan')";
        return fault switch
        {
            // The two families lose DIFFERENT things, and naming which is the whole use of
            // the message: a text spanner loses its word with its line, an ottava loses the
            // transposition with its bracket.
            SpanPairingFault.Unterminated => ottava
                ? noun + " is never closed, so no bracket is drawn and the notes "
                  + "under it are not transposed; write " + ends + " on the first note back "
                  + "at written pitch - a span with no end has no length to draw"
                : noun + " is never closed, so neither its word nor its line is "
                  + "drawn; write " + ends + " on the note it should reach "
                  + "- a span with no end has no length to draw",
            SpanPairingFault.StopWithNoStart =>
                "this '@!' closes nothing, so nothing is drawn; no " + noun.Split(' ', 2)[1]
                + " is open in this voice - note that a span does not carry into another voice",
            // ⚠️ ONLY THE TEXT SPANNER CAN REACH THIS. An ottava start while one is open is a
            // CHANGE of octavation and a second '@sustain' is RE-PEDALLING: both close the
            // open span and open a new one, which is what LilyPond does and what the notation
            // means. The article is spelt per family rather than glued on, because "a ottava"
            // is what gluing gives.
            _ =>
                "a text spanner is already open in this voice, so this one is ignored; "
                + "close the first with '@!' before starting a second - spans do not nest",
        };
    }
}
