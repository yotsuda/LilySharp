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

using System.Text;
using LilySharp.Core.Music;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.LilyPond;

// The music stream: EmitMusicStream and every item it writes - notes, rests, chords, durations,
// pitches and the two octave frames, bar lines, key / time / tempo / dynamics / partial, and
// the nested bodies (tuplet, arpeggio, voice span, grace, cue, repeat). Split out of
// LilyPondExporter.cs as a partial class (2026-10-02); same instance state, no behavior change.
public sealed partial class LilyPondExporter
{
    /// <summary>
    /// Indexes every phrase / variable body in the file so a bare reference inside a
    /// section can be expanded where it stands.
    /// </summary>
    /// <remarks>
    /// The same two declaration shapes MusicXmlExporter indexes (see its
    /// <c>PhraseDeclarationSyntax</c> / <c>VariableDeclarationSyntax</c> cases): this is the
    /// THIRD reader of that pair, and it exists because it was the one missing — a section
    /// body written the ordinary way (<c>melody { partA partB }</c>) exported as an EMPTY
    /// staff, with only a "VariableReference not exported" warning to show for it.
    /// </remarks>
    private void CollectPhrases(CompilationUnitSyntax root)
    {
        foreach (var node in root.DescendantNodes<SyntaxNode>())
        {
            switch (node)
            {
                case PhraseDeclarationSyntax phrase:
                    _shared.Phrases[phrase.Name.Text] = phrase.Body;
                    break;
                case VariableDeclarationSyntax varDecl:
                    _shared.Phrases[varDecl.Name.Text] = varDecl.Expression;
                    break;
            }
        }
    }

    /// <summary>
    /// Expands a bare phrase reference in place, in a FRESH octave frame.
    /// </summary>
    /// <remarks>
    /// Lily# evaluates every phrase body in the default frame, so the same phrase means the
    /// same pitches at every call site (the collector's <c>RelativeResetMarker</c>); the
    /// reference's own marks (<c>Chorus'</c> / <c>Chorus,</c>) shift that frame. In
    /// LilyPond the same thing is a NESTED <c>\relative</c>, whose reference pitch is
    /// absolute — so the body lands on the pitches Lily# gives it whatever precedes the
    /// reference.
    /// <para>
    /// ⚠️ WHAT THIS CANNOT RENDER, and warns about rather than emitting quietly. LilyPond's
    /// nested <c>\relative</c> is TRANSPARENT to the enclosing frame —
    /// LILYPOND-REF: lily/relative-octave-music.cc:39-45 Relative_octave_music::relative_callback,
    /// which hands the incoming pitch straight
    /// back — whereas Lily# hands off the phrase's ANCHOR (its first note's bare letter,
    /// MeasureCollector.Form.cs). The two agree on the body and disagree only on what a
    /// note AFTER the reference is relative to, so a body that is all references (how the
    /// corpus is written) transpiles exactly, and a mixed one gets a warning naming the
    /// spot. A movable phrase's auto-transpose is likewise reported, not guessed: it
    /// would need the pitches re-derived, and this exporter is a transpiler that copies
    /// pitch tokens verbatim. (A reference's glued interval argument was the other such
    /// case and was reported the same way, until the spelling was removed 2026-08-28.)
    /// </para>
    /// </remarks>
    private string EmitPhraseReference(VariableReferenceSyntax v)
    {
        string name = v.Name.Text;
        if (!_shared.Phrases.TryGetValue(name, out var body))
        {
            _warnings.Add($"phrase '{name}' is referenced but not declared — nothing exported for it");
            return "";
        }
        if (!_shared.ActivePhrases.Add(name))
        {
            _warnings.Add($"phrase '{name}' refers to itself — the inner reference is not expanded");
            return "";
        }
        try
        {
            // (A reference used to be able to carry an interval argument — Melody'(3) —
            // which this exporter could not re-derive, so it warned that the body went out
            // UNSHIFTED. The spelling was removed 2026-08-28, so there is nothing left to
            // warn about: the marks below are whole octaves, which the twin CAN express.)
            var buf = OpenNested();
            // The body opens Lily#'s FRESH frame — a crotchet (MeasureCollector.MusicWalk
            // EnterDefaultFrame: `_defaultDuration = … Fraction.Quarter`) — where LilyPond
            // carries the last value written before the reference, so the body's first bare
            // event writes the crotchet out. What the body last wrote carries OUT on both
            // sides (the page walks on with it; PhraseEndMarker restores no value), so it
            // comes back below. Until session 398 the buffer opened at "4" with nothing
            // forced, and `c8 G` read G's first bare note as a quaver in LilyPond.
            buf._lastWrittenValue = "4";
            buf._lastWrittenDots = 0;
            buf._forceNextDuration = true;
            // Both sides open a FRESH frame for the body — LilyPond the nested \relative
            // written below, Lily# its EnterDefaultFrame — at the anchor of the SECTION being
            // played, moved by the reference's own marks.
            // ⚠️ "of the section", not "of the part" (2026-08-31): a section quoted `~B'` is an
            // octave up, and a phrase body inside it is part of that play. The collector says
            // the same in one line (OctaveContext.ResetToInitial reads SectionOctaveOffset),
            // and _sectionOctaveOffset is 0 for every play written without marks.
            // ⚠️ THIS ASSIGNMENT IS BOOKKEEPING, NOT THE ANSWER — measured 2026-08-31, not
            // assumed. The two frames are set to the SAME value and EmitMusicPitch writes the
            // DIFFERENCE between them, so shifting both by a constant cancels in every mark
            // the body writes: taking the section shift out of here alone leaves the .ly BYTE
            // IDENTICAL. What decides the block's octave is its \relative reference pitch at
            // the bottom of this method; the two are kept in step so the pair cannot drift,
            // and the observer sits on the reference pitch (SectionReferenceOctaveTests
            // .APhraseBodyInsideAMarkedSectionMovesWithIt asserts the twin MOVED).
            buf._lysStep = buf._lyStep = 0;
            buf._lysOctave = buf._lyOctave =
                _anchorOctave + _sectionOctaveOffset + v.OctaveOffset;
            buf.EmitMusicStream(MusicItems(body).ToList(), "");
            string inner = NestedText(buf);
            _lastWrittenValue = buf._lastWrittenValue;
            _lastWrittenDots = buf._lastWrittenDots;
            _forceNextDuration = buf._forceNextDuration;
            // The nested \relative the reference opens is where the two frames part company
            // (the warning above says so); stop tracking rather than guess.
            _frameTracked = false;
            if (inner.Length == 0)
                return "";

            // In absolute mode there is no relative frame to reset, so an UNMARKED
            // reference is pure inlining. A marked one moves the ANCHOR the body's bare
            // letters are measured from — the collector's OctaveBase — and LilyPond spells
            // exactly that with a nested \fixed, off the same AbsoluteBaseOctave the part
            // wrapper uses. Nesting is what makes it a SHIFT rather than a re-anchor, so a
            // doubly-referenced phrase composes the way the collector's stack does.
            // ⚠️ Off that constant, so this inherits its documented gate rather than
            // opening a second one: the constant is 4 while Lily#'s absolute anchor honours
            // a part's `octave N`, and every absolute spelling in this file is written
            // against the wrapper actually emitted. A book with both would be a whole
            // octave out HERE TOO, and consistently so — which is the point of reusing it.
            // ⚠️ This branch used to warn "the body is exported UNSHIFTED" and emit the
            // body unmoved, and it was the ONLY one of the four outputs that said anything:
            // the page, the MIDI and the MusicXML all dropped the marks in silence. It was
            // right about what happened and wrong about what should — see
            // MeasureCollector.EnterDefaultFrame.
            if (_octaveAbsolute)
                return v.OctaveOffset == 0
                    ? inner
                    : $"\\fixed {AnchorPitch(_absoluteBaseOctave + v.OctaveOffset)} {{ {inner} }}";

            // The nested \relative's own reference pitch has to be the SAME anchor the body
            // was emitted against (buf._lyOctave above), section shift included — otherwise
            // LilyPond opens the block one octave from where the body's marks were computed.
            return $"\\relative {ReferencePitch(_sectionOctaveOffset + v.OctaveOffset)} {{ {inner} }}";
        }
        finally
        {
            _shared.ActivePhrases.Remove(name);
        }
    }

    /// <summary>
    /// The nested block's reference pitch: the PART's anchor octave written LilyPond's way,
    /// moved by the reference's trailing marks.
    /// </summary>
    /// <remarks>
    /// LilyPond writes octave 4 as <c>c'</c>, so the mark count is the anchor minus 3 — a
    /// treble part's <c>c'</c>, a bass part's bare <c>c</c>. See <see cref="_anchorOctave"/>.
    /// </remarks>
    private string ReferencePitch(int octaveOffset) => AnchorPitch(_anchorOctave + octaveOffset);

    /// <summary>An octave number as a LilyPond pitch: 4 → <c>c'</c>, 3 → <c>c</c>.</summary>
    private static string AnchorPitch(int octave)
    {
        int marks = octave - 3;
        return marks >= 0 ? "c" + new string('\'', marks) : "c" + new string(',', -marks);
    }

    /// <summary>
    /// The octave a part's relative pitches are anchored to, resolved the way the layout
    /// resolves it (InstrumentDefaults.AnchorOctave: explicit <c>octave N</c> &gt; the
    /// <c>instrument</c> preset's octave &gt; 4 — the clef is not consulted).
    /// </summary>
    /// <remarks>
    /// ⚠️ An <c>instrument</c> preset is a BUNDLE — <c>instrument bass</c> means bass clef
    /// AND octave 3 AND a sounding pitch an octave down (InstrumentDefaults.GetTransposition;
    /// an electric bass is written an octave above where it sounds, and that −12 is
    /// deliberate). It is read WHOLE or not at all: taking the octave third of it and leaving
    /// the other two would move the twin's written pitch while its sounding pitch stayed
    /// wrong, i.e. make it wrong in a way that LOOKS right. See <see cref="PartClefWord"/>
    /// for why reading it is still a transpilation and not a re-derivation.
    /// </remarks>
    private static int AnchorOctaveOf(PartDeclarationSyntax part)
        => InstrumentDefaults.AnchorOctave(ExplicitPartOctave(part), InstrumentPresetOf(part));

    /// <summary>The part's own <c>octave N</c> property, or null when it states none — the
    /// one input both anchors take from the part, read once so the relative anchor and
    /// <see cref="_absoluteBaseOctave"/> cannot disagree about what the part said.</summary>
    private static int? ExplicitPartOctave(PartDeclarationSyntax part)
        => PartProperty(part, "octave") is { } octave && int.TryParse(octave, out int n)
            ? n
            : null;

    // ---- Header ------------------------------------------------------------

    private void EmitMusicStream(List<SyntaxNode> items, string indent)
    {
        // A ':|' that closes no '|:' REWINDS — it repeats the piece from its beginning
        // (FormWalk.LoneRepeatEnd; user decision 2026-08-15), and the ':|' half of a
        // form-level ':|:' is the same bar (GRAMMAR §8: ':|:' is ':|' then '|:'). LilyPond
        // has no rewinding bar line — `\bar ":|."` is a glyph and only `\repeat volta`
        // repeats — so the stretch BEFORE the bar is the repeat's body:
        //   form { A B :| }      ->  \repeat volta 2 { A B }
        //   form { A :|: B :| }  ->  \repeat volta 2 { A }  \repeat volta 2 { B }
        // MEASURED 2026-09-10 (scratch/ベースタブLy/pageBreak.lys, form `A :|: B :|`): the
        // page draws ':|' after A, '|:' before B and ':|' after B, while the twin left A
        // outside every repeat and drew no bar after it at all — the one reader of four
        // that did not draw the first ':|'. The last rewind in the stream is the OUTERMOST
        // body (`A :| B :|` nests: the second rewinds over the first's stretch), so it is
        // found first and the prefix is emitted through this same method again.
        int rewind = LastRewind(items);
        if (rewind > 0)
        {
            EmitRewindRepeat(items, rewind, indent);
            return;
        }

        var line = new StringBuilder(indent);
        int i = 0;
        // AN EMPTY `| |` BAR IS ONE BAR OF SILENCE (owner's decision 2026-08-28): the page
        // fills it with a full-measure spacer (MeasureBuilder.EmitEmptyMeasure) and the MIDI
        // walk counts it (MidiExporter.ProcessSequence). LilyPond's `|` is a bar CHECK that
        // takes no time, so a twin that copied the bare bars drew `c1 | | e1` as TWO bars
        // where the page draws three — the twin was silently different music (MEASURED
        // 2026-09-09, scratch/p358/midi/gap.lys: two bars in the twin, three on the page),
        // and an empty pickup `partial 4 | c4 …` failed LilyPond's bar check and pulled the
        // c4 into the pickup. The twin now writes the spacer the author would have typed —
        // `s1 |` in 4/4, `s2. |` in 3/4, `s4 |` under `partial 4` — with the walk's own
        // rule: a bare `|` (or a `|:` that does not open the scope) closes the bar behind it
        // when time has passed since the last boundary, and otherwise stands for an empty
        // bar; a typed bar line (`||`, `:|`, `|.`) decorates a boundary and opens none; the
        // scope start counts as a boundary. ONE rule in four spellings, kept honest by
        // EmptyBarExportTests' identity pairs (`| |` against the spelled `s1 |`).
        _timeSinceBoundary = false;
        bool atScopeStart = true;
        while (i < items.Count)
        {
            var item = items[i];

            // Standalone music a note left behind (`\breathe`, see SplitAttachments) goes
            // out once every post-event written as a SIBLING of that note — the tie, slur,
            // beam marker, dynamic, string number — has been written, so `c4@breath~ c4`
            // is `c4 ~ \breathe c4` and the tie is still attached.
            if (_trailingMusic.Length > 0
                && item is not (TieSyntax or SlurSyntax or BeamMarkerSyntax or DynamicSyntax
                    or StringNumberAnnotationSyntax or ArticulationSyntax or MusicMarkSyntax))
                FlushTrailingMusic(line, indent);

            // A section head ends at the first item that is not one of its directives: what it
            // holds (the restores and the mark, EmitSectionPlay) is written BEFORE anything this
            // item puts on the line — the empty bar's spacer below included.
            if (_sectionHead != null
                && item is not (TimeSignatureSyntax or KeySignatureSyntax or ClefDeclarationSyntax))
            {
                string held = FlushSectionHead();
                if (held.Length > 0)
                    AppendToken(line, held, indent);
            }

            if (item is { Green: SectionPlayGreen })
            {
                // A section boundary: the next section's music opens a fresh scope, as the
                // MIDI walk's per-section sequence does.
                _timeSinceBoundary = false;
                atScopeStart = true;
            }
            else if (item is { Green: ClosedBarGreen })
            {
                // The voice's last bar is closed, whatever this stream last wrote — a phrase
                // reference counts as time here, even when its body ended on its own `|`
                // (see PaddingBars).
                _timeSinceBoundary = false;
            }
            else if (item is BarlineSyntax gapBar && !_chordTrack)
            {
                var kind = gapBar.BarToken.Kind;
                // A form's `|:` stands at a section boundary — the section-play marker follows
                // it — and is the structural bar line the page never pairs with the bar before
                // it (MeasureBuilder.ArmBoundaryForStructuralBarline's remark: "a structural
                // barline is not the second of any such pair"). MEASURED (2.26.0,
                // scratch/p359/lp/formrep-plain.ly, 2026-09-09): read as a pair it wrote `s1`
                // between A's `|` and `\repeat volta`, and LilyPond drew five bars — one 5.51
                // wide and empty — for the page's four.
                bool opensASectionPlay = kind == SyntaxKind.RepeatStartBar
                    && i + 1 < items.Count && items[i + 1] is { Green: SectionPlayGreen };
                bool pairsHere = kind == SyntaxKind.Bar
                    || (kind == SyntaxKind.RepeatStartBar && !atScopeStart && !opensASectionPlay);
                if (pairsHere && !_timeSinceBoundary)
                    AppendToken(line, "s" + ChordModeDuration(_bars.BarLength), indent);
                // The bar behind this bar line closed (or was the empty bar just written), so
                // a pending pickup is spent — as the page's MeasureBuilder.ResetPerMeasureState does.
                if (pairsHere || _timeSinceBoundary)
                    _bars.SpendPartial();
                _timeSinceBoundary = false;
                atScopeStart = false;
            }
            else if (TakesMeasureTime(item))
            {
                _timeSinceBoundary = true;
            }

            // Inline |: … :| repeat span → \repeat volta N { … } \alternative { … }
            if (item is BarlineSyntax { BarToken.Kind: SyntaxKind.RepeatStartBar or SyntaxKind.RepeatBothBar })
            {
                FlushLine(line, indent);
                i = EmitInlineRepeat(items, i, indent);
                // The `:|` that closed the span is a boundary too.
                _timeSinceBoundary = false;
                atScopeStart = false;
                continue;
            }

            // The one regime where a phrase reference does not transpile exactly: LilyPond's
            // nested \relative hands the enclosing frame back UNCHANGED
            // (lily/relative-octave-music.cc:39-45 relative_callback), while Lily# hands off
            // the phrase's ANCHOR. Nothing after the reference sees the difference unless
            // something after it is a pitch, so the warning is raised there and only there.
            // ⚠️ ONLY IN RELATIVE MODE: under `octave absolute` the reference is inlined (or
            // wrapped in a \fixed, EmitVariableReference) and no frame is handed back at all,
            // so the stretch is exact. Until 2026-09-26 it warned there too — four false
            // warnings on a quartet probe written in absolute mode.
            if (!_octaveAbsolute && item is VariableReferenceSyntax vref && FollowedByPitch(items, i))
                _warnings.Add(
                    $"a note follows the phrase reference '{vref.Name.Text}': LilyPond makes it "
                    + "relative to the pitch BEFORE the reference, Lily# to the phrase's anchor "
                    + "— check that stretch by hand");

            string tok = EmitItem(item);
            if (tok.Length == 0) { i++; continue; }

            AppendToken(line, tok, indent);

            // A barline or a break ends the current visual line.
            if (item is BarlineSyntax || item is BreakSyntax)
                FlushLine(line, indent);

            i++;
        }
        string stillHeld = FlushSectionHead();
        if (stillHeld.Length > 0)
            AppendToken(line, stillHeld, indent);
        FlushTrailingMusic(line, indent);
        FlushLine(line, indent);
    }

    /// <summary>Standalone music that trails the last note written (<see cref="_trailingMusic"/>),
    /// written out and forgotten.</summary>
    private void FlushTrailingMusic(StringBuilder line, string indent)
    {
        if (_trailingMusic.Length == 0)
            return;
        AppendToken(line, _trailingMusic.ToString(), indent);
        _trailingMusic.Clear();
    }

    /// <summary>
    /// The index of the last bar line in <paramref name="items"/> that REWINDS — a <c>:|</c>
    /// (or the <c>:|</c> half of a <c>:|:</c>) standing at this stream's top level with no
    /// <c>|:</c> open before it — or -1 when none does.
    /// </summary>
    /// <remarks>
    /// The pairing is the collector's (MeasureBuilder, read back by RepeatPairingValidator):
    /// a <c>:|</c> closes the nearest open <c>|:</c>, and a <c>:|:</c> closes one and opens one
    /// — so inside an open repeat it is a divider (<see cref="EmitInlineRepeat"/> hands it back
    /// to open the next block), and at depth 0 its first half is the rewind and its second
    /// half opens a block the rest of the stream closes. A stream with no rewind is unchanged
    /// by this scan, which is every stream in the tree before 2026-09-10 but the one-sided
    /// ones (the sweep's control).
    /// </remarks>
    private static int LastRewind(List<SyntaxNode> items)
    {
        int depth = 0, last = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not BarlineSyntax bar)
                continue;
            switch (bar.BarToken.Kind)
            {
                case SyntaxKind.RepeatStartBar:
                    depth++;
                    break;
                case SyntaxKind.RepeatEndBar:
                    if (depth > 0) depth--;
                    else if (!IsEndingDivider(items, i)) last = i;
                    break;
                case SyntaxKind.RepeatBothBar:
                    // Inside a repeat: closes one, opens one, depth unchanged.
                    if (depth == 0) { last = i; depth = 1; }
                    break;
            }
        }
        return last;
    }

    /// <summary>
    /// Whether the <c>:|</c> at <paramref name="i"/> stands between two endings of one repeat
    /// (<c>[1. B] :| [2. C] :| [3. D]</c>) — a divider the repeat owns, read the way the parser
    /// reads it (Parser.Form.cs ParseFormRepeatBlock's furtherAlternatives: a <c>:|</c> right
    /// before another ending). Not a close and not a rewind.
    /// </summary>
    private static bool IsEndingDivider(List<SyntaxNode> items, int i)
        => i + 1 < items.Count && items[i + 1] is InlineVoltaSyntax;

    /// <summary>
    /// Writes <paramref name="items"/> with the stretch before the rewinding bar at
    /// <paramref name="rewind"/> as a <c>\repeat volta N { … }</c> body, then the rest of the
    /// stream — for a <c>:|:</c>, opened by the <c>|:</c> it also is.
    /// </summary>
    /// <remarks>
    /// ⚠️ printInitialRepeatBar: LilyPond prints <c>.|:</c> at moment 0 for the body that
    /// opens the piece only when the twin's <c>\layout</c> says so, and the twin says so for
    /// every book (<see cref="EmitScore"/>'s remark: a WRITTEN <c>|:</c> is printed, owner
    /// decision session 328). A rewind's body has no written <c>|:</c> — the page draws
    /// none at the start (measured on pageBreak.lys: the first system opens with a plain
    /// clef) — so a book that opens with a rewind turns the setting off. A rewind over a
    /// written <c>|: … :|</c> (<c>form { |: A :| B :| }</c>) keeps it on: the opener is
    /// written.
    /// ⚠️ Two rewinds nest (<c>A :| B :|</c> → <c>\repeat { \repeat { A } B }</c>), and there
    /// LilyPond REPLAYS THE INNER REPEAT on the outer pass, while Lily# replays the WRITTEN
    /// stretch once (MidiExporter.RepeatFromTheBeginning: A B A B C A B C, not A B A B C
    /// A B A B C). Same page, one more pass — said in the warning channel, since no
    /// <c>\repeat</c> spelling plays the written order.
    /// </remarks>
    private void EmitRewindRepeat(List<SyntaxNode> items, int rewind, string indent)
    {
        var bar = (BarlineSyntax)items[rewind];
        var body = items.GetRange(0, rewind);

        if (!OpensWithAWrittenRepeatStart(body))
            _rewindOpensThePiece = true;
        if (LastRewind(body) > 0 && !_chordTrack)
            _warnings.Add(
                "two one-sided ':|' nest in the twin (\\repeat volta inside \\repeat volta), "
                + "so LilyPond replays the inner repeat on the outer pass where Lily# replays "
                + "the written stretch once — same page, one more pass");

        int count = bar.HasExplicitRepeatCount ? bar.RepeatCount : 2;
        _sb.Append(indent).Append("\\repeat volta ").Append(count).Append(" {\n");
        EmitMusicStream(body, indent + "  ");
        _sb.Append(indent).Append("}\n");

        var rest = items.GetRange(rewind + 1, items.Count - rewind - 1);
        if (bar.BarToken.Kind == SyntaxKind.RepeatBothBar)
            rest.Insert(0, CreateBarline(SyntaxKind.RepeatStartBar, "|:", bar.Position, 0));
        if (rest.Count > 0)
            EmitMusicStream(rest, indent);
    }

    /// <summary>Whether the first thing in <paramref name="items"/> that draws or sounds is
    /// a written <c>|:</c> — header directives and section markers before it do not count.</summary>
    private static bool OpensWithAWrittenRepeatStart(List<SyntaxNode> items)
    {
        foreach (var item in items)
        {
            if (item is BarlineSyntax bar)
                return bar.BarToken.Kind == SyntaxKind.RepeatStartBar;
            if (TakesMeasureTime(item))
                return false;
        }
        return false;
    }

    /// <summary>
    /// Whether <paramref name="item"/> takes measure time — the question the `| |` rule in
    /// <see cref="EmitMusicStream"/> asks. Spelled as the list of what does NOT (bar lines,
    /// marks, spanner ends, directives, a grace body), so that a node type this list has
    /// never met counts as music and never earns a spurious spacer: the only mistake this
    /// can then make is to leave a gap unwritten, which is the twin as it always was.
    /// </summary>
    private static bool TakesMeasureTime(SyntaxNode item) => item switch
    {
        BarlineSyntax or BreakSyntax or TieSyntax or SlurSyntax or BeamMarkerSyntax
            or DynamicSyntax or KeySignatureSyntax or TimeSignatureSyntax
            or TempoDeclarationSyntax or ClefDeclarationSyntax or PartialDeclarationSyntax
            or MusicMarkSyntax or NavigationMarkSyntax or GraceExpressionSyntax
            or StringNumberAnnotationSyntax or ArticulationSyntax
            or OctaveDirectiveSyntax or MetadataDeclarationSyntax
            or SectionDeclarationSyntax or FormDeclarationSyntax
            or PartDeclarationSyntax or RenderDeclarationSyntax => false,
        { Green: SectionPlayGreen } => false,
        { Green: ClosedBarGreen } => false,
        { Green: RepeatTieGreen } => false,
        { Green: VoltaShapeGreen } => false,
        _ => true,
    };

    /// <summary>
    /// Whether anything after <paramref name="index"/> in this stream carries a pitch —
    /// i.e. whether the enclosing octave frame is read again after this point.
    /// </summary>
    private static bool FollowedByPitch(List<SyntaxNode> items, int index)
    {
        for (int j = index + 1; j < items.Count; j++)
            if (items[j] is NoteSyntax or ChordSyntax)
                return true;
        return false;
    }

    // items[start] is |: . Returns the index just past the matching :| (and any
    // trailing [n. …] alternatives).
    private int EmitInlineRepeat(List<SyntaxNode> items, int start, string indent)
    {
        // Collect common items until the first alternative or the closing :|,
        // tracking nesting so a nested |: … :| does not close us early.
        var common = new List<SyntaxNode>();
        var alternatives = new List<InlineVoltaSyntax>();
        int depth = 0;
        int repeatCount = 2;
        int i = start + 1;
        bool closed = false;

        for (; i < items.Count; i++)
        {
            var it = items[i];

            // Once the ':|' has been seen the scan keeps running ONLY to pick up trailing
            // '[2. …]' endings. Anything else ends this repeat — in particular a second
            // '|:', which starts the NEXT repeat and must not be read as nesting. This
            // test has to come before the nesting arm below: while it sat after it,
            // '|: A :| |: B :|' re-entered as depth++ and emitted one repeat with an
            // empty '\repeat volta 2 { }' inside it, leaving B outside every repeat and
            // closed by a bare '\bar ":|."' — the page, MIDI and MusicXML all read the
            // same source as two repeats (measured 2026-08-15: 8 repeat dots / 16 noteOn /
            // two forward+backward pairs), so the twin was alone in disagreeing.
            if (closed)
            {
                if (it is InlineVoltaSyntax trailing) { alternatives.Add(trailing); continue; }
                if (it is BreakSyntax) { /* absorb a break right after :| */ continue; }
                // The ':|' BETWEEN two endings (`[1. B] :| [2. C] :| [3. D]` — the parser's
                // furtherAlternatives, Parser.Form.cs) is this repeat's own divider: the
                // body plays once more and the next ending follows. It closes nothing new
                // and rewinds nothing, so it is absorbed here and the ending count sets the
                // play count below. MEASURED 2026-09-10 (sweep of 925 books, p364): it used
                // to fall out of this scan as a stray bar, written `\bar ":|."` after the
                // \alternative, with the third ending outside the repeat.
                if (IsEndingDivider(items, i)) continue;
                break;
            }

            if (it is BarlineSyntax { BarToken.Kind: SyntaxKind.RepeatStartBar } && alternatives.Count == 0)
            {
                depth++; common.Add(it); continue;
            }
            if (it is BarlineSyntax rb && rb.BarToken.Kind is SyntaxKind.RepeatEndBar or SyntaxKind.RepeatBothBar)
            {
                if (depth > 0) { depth--; common.Add(it); continue; }
                repeatCount = rb.HasExplicitRepeatCount ? rb.RepeatCount : repeatCount;
                closed = true;
                // ':|:' both closes this repeat and opens the next one, so hand the token
                // back to the caller (EmitMusicStream opens a repeat on RepeatBothBar too)
                // instead of consuming it. A ':|' only closes, so keep scanning for the
                // trailing endings that may follow it.
                if (rb.BarToken.Kind == SyntaxKind.RepeatBothBar) break;
                continue;
            }
            if (it is InlineVoltaSyntax v)
            {
                alternatives.Add(v);
                continue;
            }
            common.Add(it);
        }

        if (alternatives.Count > 0)
        {
            int maxVolta = alternatives.SelectMany(a => a.Numbers).DefaultIfEmpty(2).Max();
            repeatCount = Math.Max(repeatCount, maxVolta);
        }

        _sb.Append(indent).Append("\\repeat volta ").Append(repeatCount).Append(" {\n");
        EmitMusicStream(common, indent + "  ");
        _sb.Append(indent).Append("}\n");

        if (alternatives.Count > 0)
        {
            // A ranged or listed ending ([1-2. …], [1,3. …]) names its volte: without a
            // \volta on every alternative LilyPond hands the extra volte to the FIRST one.
            // LILYPOND-REF: scm/music-functions.scm:346-354 make-repeat — with no specced alternative, alt-1-count = times - lalts + 1 volte go to the first.
            // A plain `[1.] [2.]` run is left unspecced: LilyPond's fill IS that reading.
            bool specced = alternatives.Any(a => a.HasSeparator);
            _sb.Append(indent).Append("\\alternative {\n");
            foreach (var alt in alternatives)
            {
                _sb.Append(indent).Append("  ");
                if (specced)
                    _sb.Append("\\volta ").Append(string.Join(",", alt.Numbers)).Append(' ');
                _sb.Append("{\n");
                EmitMusicStream(alt.Items.ToList(), indent + "    ");
                _sb.Append(indent).Append("  }\n");
            }
            _sb.Append(indent).Append("}\n");
        }

        return i;
    }

    // ---- Per-item emit -----------------------------------------------------

    /// <summary>
    /// One item, through the section head (<see cref="_sectionHead"/>): at a play's head a
    /// <c>time</c> or <c>key</c> that states again what the section before left in force writes
    /// nothing and cancels the restore held for it — LilyPond would draw both (a TimeSignature
    /// for every \time, a KeySignature for every \key), and the page draws neither
    /// (MeasureBuilder.SectionHead, LILYSHARP-OWN, owner's decision 2026-10-02). Any other
    /// <c>time</c>/<c>key</c> writes its held restore first, as the play used to; a clef passes
    /// (LilyPond draws no clef that does not change); anything else writes what is held and ends
    /// the head.
    /// </summary>
    private string EmitItem(SyntaxNode item)
    {
        var head = _sectionHead;
        switch (item)
        {
            case TimeSignatureSyntax ts:
            {
                // Unchanged against the head (what the section before left) or, elsewhere, the
                // meter in force; `time!` writes it regardless — LilyPond draws every \time.
                bool same = ts.IsSenzaMisura == (head?.Senza ?? _bars.SenzaMisura)
                            && (ts.IsSenzaMisura || TimeText(ts) == (head?.TimeText ?? TimeTextInForce));
                string written = EmitItemCore(item);   // advances the running meter either way
                if (same && !ts.IsForced)
                {
                    if (head != null)
                        _heldTimeRestore = null;
                    return "";
                }
                if (head == null)
                    return written;
                string held = _heldTimeRestore ?? "";
                _heldTimeRestore = null;
                return Join(Join(held, TakeHeldMark()), written);
            }
            case KeySignatureSyntax k:
            {
                var (sharpsBefore, tonicBefore) = head is { } h ? (h.KeySharps, h.Tonic) : (_keySharps, _tonic);
                string written = EmitItemCore(item);   // advances the running key either way
                bool same = _keySharps == sharpsBefore && _tonic == tonicBefore;
                if (same && !k.IsForced)
                {
                    if (head != null)
                        _heldKeyRestore = null;
                    return "";
                }
                if (head == null)
                    return written;
                string held = _heldKeyRestore ?? "";
                _heldKeyRestore = null;
                return Join(Join(held, TakeHeldMark()), written);
            }
            case ClefDeclarationSyntax cl:
                // LilyPond draws no clef that changes nothing (clef-engraver.cc:139-166), which is
                // the page's default; `clef!` is LilyPond's forceClef.
                return cl.IsForced
                    ? Join("\\set Staff.forceClef = ##t", EmitItemCore(item))
                    : EmitItemCore(item);
            default:
                return head == null ? EmitItemCore(item) : Join(FlushSectionHead(), EmitItemCore(item));
        }

        static string Join(string a, string b) => a.Length == 0 ? b : b.Length == 0 ? a : a + " " + b;
    }

    /// <summary>Writes the restores a section head still holds and ends the head.</summary>
    private string FlushSectionHead()
    {
        string held = string.Join(" ",
            new[] { _heldTimeRestore, _heldKeyRestore, _heldMark }.Where(s => !string.IsNullOrEmpty(s)));
        _heldTimeRestore = _heldKeyRestore = _heldMark = null;
        _sectionHead = null;
        return held;
    }

    private string TakeHeldMark()
    {
        string mark = _heldMark ?? "";
        _heldMark = null;
        return mark;
    }

    private string EmitItemCore(SyntaxNode item) => item switch
    {
        NoteSyntax n => CloseImprovisation() + EmitNote(n) + TakeRepeatTie(n.Articulations),
        DrumNoteSyntax dn => CloseImprovisation() + EmitDrumNote(dn),
        RestSyntax r => DropRepeatTie() + EmitRest(r),
        ChordSyntax c => CloseImprovisation() + EmitChord(c) + TakeRepeatTie(c.Articulations),
        { Green: RepeatTieGreen } => ArmRepeatTie(),
        ChordRepetitionSyntax q => EmitChordRepetition(q),
        SlashNoteSyntax sl => EmitSlashNote(sl),
        BareDurationSyntax bd => EmitBareDuration(bd),
        BarlineSyntax b => EmitBarline(b),
        BreakSyntax br => br.Directive switch
        {
            BreakKind.NoLine => "\\noBreak",
            BreakKind.Page => "\\pageBreak",
            BreakKind.NoPage => "\\noPageBreak",
            _ => "\\break",
        },
        TieSyntax => "~",
        SlurSyntax s => s.IsOpen ? "(" : ")",
        BeamMarkerSyntax bm => bm.IsStart ? "[" : "]",
        DynamicSyntax d => EmitDynamic(d),
        KeySignatureSyntax k => EmitKey(k),
        TimeSignatureSyntax ts => EmitTime(ts),
        TempoDeclarationSyntax t => EmitTempo(t),
        ClefDeclarationSyntax cl => EmitClef(cl),
        PartialDeclarationSyntax p => ArmPartial(p),
        TupletExpressionSyntax tup => EmitTuplet(tup),
        ArpeggioSyntax arp => CloseImprovisation() + EmitArpeggio(arp),
        ParallelExpressionSyntax par => EmitParallel(par),
        GraceExpressionSyntax g => EmitGrace(g),
        CueExpressionSyntax cue => EmitCue(cue),
        RepeatExpressionSyntax rep => EmitRepeat(rep),
        MusicMarkSyntax mk => EmitMark(mk),
        // The section-play sentinel is matched by its GREEN: a marker inside a rebuilt
        // volta ending comes back as a GenericSyntaxNode red, and only the green (which
        // carries the payload) survives that rebuild.
        { Green: SectionPlayGreen sp } => EmitSectionPlay(sp),
        // A chord track's bar, pre-spelled (ChordBars): matched by its green for the same
        // reason as the play sentinel above.
        { Green: ChordBarGreen cb } => cb.Entries,
        { Green: VoltaShapeGreen vs } => EmitVoltaShape(vs),
        // Writes nothing: it only tells the `| |` rule a bar has closed (PaddingBars).
        { Green: ClosedBarGreen } => "",
        NavigationMarkSyntax nav => EmitNavMark(nav),
        StringNumberAnnotationSyntax sn => sn.StringNumberToken.Text,
        ArticulationSyntax a => MapArticulation(a),
        VariableReferenceSyntax vr => EmitPhraseReference(vr),
        // Structural nodes that carry no inline music are skipped silently.
        OctaveDirectiveSyntax or MetadataDeclarationSyntax
            or SectionDeclarationSyntax or FormDeclarationSyntax
            or PartDeclarationSyntax or RenderDeclarationSyntax => "",
        _ => Skip(item),
    };

    /// <summary>
    /// The duration to WRITE for an event, and the two pieces of state that decide it.
    /// </summary>
    /// <remarks>
    /// An omitted duration is not the same thing on the two sides once a grace has gone by.
    /// LILYPOND-REF: lily/parser.yy:3503-3515 maybe_notemode_duration / optional_notemode_duration
    ///   — a written duration becomes <c>parser-&gt;default_duration_</c> and an omitted one is
    ///   replaced by it. That is PARSER state (lily/include/lily-parser.hh:44), which is why it
    ///   reaches straight through a grace body.
    /// LilyPond repeats the last duration it READ, and it read the grace body
    /// (<c>\grace { d8 } c</c> makes that <c>c</c> an EIGHTH); Lily# collects the grace with
    /// its own local default (MeasureCollector.CollectGraceNotes' graceDefaultDuration) which
    /// never escapes, so its <c>c</c> is whatever the main stream last said — a QUARTER at the
    /// head of a piece. The twin was then a different piece of music, and LilyPond said so:
    /// test/ossia-beams failed its bar check at 7/8.
    /// <para>
    /// A DOT parted them the same way, and in the other direction: until 2026-08-07 Lily#
    /// carried the note VALUE and dropped the dots while LilyPond carries the duration whole,
    /// so <c>c4. d</c> was 5/8 of music on the page and 6/8 in the twin (measured 2026-08-01:
    /// <c>c'4. d'</c> and <c>c'4. d'4</c> drew the same six glyphs). ⇒ the event after a
    /// DOTTED one writes its value out. ⚠️ Since 2026-08-07 the collector carries the dots too
    /// (MeasureCollector.ItemFactory CreateNoteItem: <c>dots = note.Duration?.DotCount ??
    /// _defaultDots</c>), so the forced write now says the value WITH its dots
    /// (<see cref="_lastWrittenDots"/>) — the same music LilyPond would carry unwritten. The
    /// write stays because the arpeggio arm (<see cref="EmitArpeggio"/>) reuses this forcing
    /// where the two sides really do part: after a <c>&lt;&lt; … &gt;&gt;</c> LilyPond carries
    /// the member value and Lily# the group's.
    /// </para>
    /// <para>
    /// Those two cases aside, everything keeps copying the source, because a transpiler that
    /// re-spells durations everywhere is much harder to read against the .lys it came from.
    /// </para>
    /// <para>
    /// ⚠️ <see cref="_lastWrittenValue"/> mirrors Lily#'s rule and not LilyPond's: the note
    /// VALUE only, dots dropped (MeasureCollector.ItemFactory
    /// <c>_defaultDuration = Fraction.FromNoteValue(noteValue)</c>), reset to a quarter per
    /// part variable. It is fed only by the main stream — the grace body is emitted by its own
    /// exporter instance, which is exactly why its durations do not leak in here either.
    /// </para>
    /// </remarks>
    private string EmitEventDuration(DurationSyntax? d)
    {
        if (d != null)
        {
            _lastWrittenValue = d.NumberToken.Text;
            _lastWrittenDots = d.DotCount;
            // A dotted event's carry is written out on the next one (see the remarks).
            _forceNextDuration = d.DotCount > 0;
            return EmitDuration(d);
        }
        if (!_forceNextDuration)
            return "";
        _forceNextDuration = false;
        return _lastWrittenValue + new string('.', _lastWrittenDots);
    }

    // ---- a tie carried over a repeat (the section carry rule) ---------------------------
    //
    // A tie at the end of a play is carried to the first note of every play that follows it
    // in the PLAYED order (Svg.Collector.PlayedOrder). LilyPond draws the arc to the note
    // printed next by itself; every OTHER such note — the first of a repeat's body the pass
    // returns to, of a later ending — takes \repeatTie, as the page draws it
    // (Svg.Collector.SectionTieCarry). The plays that do are planned per part before its
    // music is flattened (RepeatTiePlays), and a RepeatTieMarker in the stream arms the next
    // note or chord. ⚠️ The pitch is not compared here: where it differs the page draws no
    // repeat tie and warns (LYS4007), and the twin still writes one.

    private bool _pendingRepeatTie;
    private HashSet<int> _repeatTiePlays = new();
    private int _lpPlayIndex;

    private string ArmRepeatTie()
    {
        _pendingRepeatTie = true;
        return "";
    }

    private string DropRepeatTie()
    {
        _pendingRepeatTie = false;
        return "";
    }

    /// <summary>`\repeatTie` after the note or chord the marker armed, unless one is written.</summary>
    private string TakeRepeatTie(IEnumerable<SyntaxNode> articulations)
    {
        if (!_pendingRepeatTie)
            return "";
        _pendingRepeatTie = false;
        foreach (var a in articulations)
            if (a is ArticulationSyntax { NameToken.Text: "repeatTie" })
                return "";
        return "\\repeatTie";
    }

    /// <summary>The printed plays (in form-walk order) whose first note gets a repeat tie in
    /// this part: those a play ending in a tie is followed by in the PLAYED order, other than
    /// the play printed right after it.</summary>
    private HashSet<int> RepeatTiePlays(IReadOnlyList<FormWalk.Item> items,
        Dictionary<string, (SectionDeclarationSyntax Section, SyntaxNode Container)> byName)
    {
        var plays = new List<Svg.Collector.PrintedPlay>();
        var names = new List<string>();
        bool rewind = false;
        void Add(string name, Svg.Model.SectionRepeatRole role, bool runStart, int count,
            Semantics.PassSet passes = default)
        {
            plays.Add(new Svg.Collector.PrintedPlay(role, runStart, runStart ? count : 0, rewind, passes));
            names.Add(name);
            rewind = false;
        }
        foreach (var item in items)
        {
            switch (item)
            {
                case FormWalk.SectionRef s:
                    Add(s.Name, Svg.Model.SectionRepeatRole.None, false, 0);
                    break;
                case FormWalk.Ending e:
                    foreach (var es in e.Sections)
                        Add(es.Name, Svg.Model.SectionRepeatRole.None, false, 0);
                    break;
                case FormWalk.LoneRepeatEnd:
                    rewind = true;
                    break;
                case FormWalk.Repeat rb:
                    bool runStart = true;
                    int count = rb.ExplicitPlayCount ?? 0;
                    foreach (var child in rb.Children)
                    {
                        if (child is FormWalk.SectionRef bs)
                        {
                            Add(bs.Name, Svg.Model.SectionRepeatRole.Body, runStart, count);
                            runStart = false;
                        }
                        else if (child is FormWalk.Ending be)
                        {
                            // [1. C D] is two printed plays of ONE ending; its passes ride the first.
                            var role = Svg.Model.SectionRepeatRole.Ending;
                            var passes = Semantics.PassSet.Of(be.Node.Numbers);
                            foreach (var es in be.Sections)
                            {
                                Add(es.Name, role, runStart, count, passes);
                                runStart = false;
                                role = Svg.Model.SectionRepeatRole.EndingContinued;
                                passes = default;
                            }
                        }
                        else if (child is FormWalk.BothBar)
                            runStart = true;
                    }
                    break;
            }
        }
        var result = new HashSet<int>();
        var successors = Svg.Collector.PlayedOrder.Successors(plays);
        for (int q = 0; q < plays.Count; q++)
        {
            if (!byName.TryGetValue(names[q], out var entry) || !EndsWithTie(entry.Container))
                continue;
            foreach (int s in successors[q])
                if (s != q + 1)
                    result.Add(s);
        }
        return result;
    }

    /// <summary>Whether a part's music for a section ends on a tie (its last event is tied).</summary>
    private bool EndsWithTie(SyntaxNode container)
    {
        var music = ContainerMusic(container).ToList();
        for (int i = music.Count - 1; i >= 0; i--)
        {
            if (music[i] is TieSyntax)
                return true;
            if (TakesMeasureTime(music[i]))
                return false;
        }
        return false;
    }

    private string EmitNote(NoteSyntax n)
    {
        var (prefix, suffix) = SplitAttachments(n.Articulations);
        string trem = n.Tremolo is { } t ? t.Text : "";
        // `cis?4` / `cis!4`: the accidental marks stand between the pitch and the duration.
        return prefix + EmitMusicPitch(n.Pitch) + AccidentalMarks(n.Articulations)
            + EmitEventDuration(n.Duration) + trem + suffix;
    }

    /// <summary>
    /// A drum note (<c>hh8</c>, <c>bd4</c>) — the name verbatim, because Lily#'s drum
    /// vocabulary IS LilyPond's (see <see cref="_drumParts"/>).
    /// </summary>
    private string EmitDrumNote(DrumNoteSyntax d)
    {
        if (!_drumMode)
        {
            // Reached through a phrase reference the part scan did not follow, or from a part
            // that also writes pitches. Either way \drummode is not open and the name would
            // be read as something else entirely.
            _warnings.Add($"drum note '{d.DrumName}' is outside \\drummode and was dropped");
            return "";
        }
        var (prefix, suffix) = SplitAttachments(d.Articulations);
        string trem = d.Tremolo is { } t ? t.Text : "";
        return prefix + d.DrumName + EmitEventDuration(d.Duration) + trem + suffix;
    }

    /// <summary>
    /// A chord repetition passes through verbatim — LilyPond understands <c>q</c>,
    /// and BOTH engines expand it after relative resolution (LP:
    /// toplevel-music-functions; Lily#: the collector's shared resolver), so the
    /// octave frames are untouched: a <c>q</c> has no pitches when \relative runs.
    /// The duration takes the normal carry (both parsers read it as the running
    /// default), and the repetition's own post-events ride along.
    /// </summary>
    private string EmitChordRepetition(ChordRepetitionSyntax q)
    {
        var (prefix, suffix) = SplitAttachments(q.Articulations);
        string trem = q.Tremolo is { } t ? t.Text : "";
        string body = prefix + "q" + EmitEventDuration(q.Duration) + trem + suffix;

        // ⚠️ Lily#'s q takes octave marks and LilyPond's does not, so a displaced q
        // has to be SAID a different way: the chord is written out at its new octave.
        //
        // ⚠️⚠️ \transpose c c' { q } was tried first and is WRONG — it compiles and
        // changes nothing. LilyPond expands chord repetitions in
        // toplevel-music-functions, AFTER \transpose has been applied, so the wrapper
        // moves an empty placeholder and the expansion then fills in the original's
        // untransposed pitches. Measured against 2.26.0: all eight chords came out at
        // the written octave. Writing the chord out is the only spelling that holds.
        //
        // ⚠️⚠️ NOT DONE YET, and deliberately loud rather than quietly wrong. Writing
        // the chord out means re-entering EmitChord at the q's position, and that
        // function resolves its octave against the RUNNING relative frame, while a q
        // copies the original's ABSOLUTE pitches and is transparent to that frame. In
        // `octave absolute` the two agree; in relative mode they need not. Until that
        // is worked out and measured, the twin says what it cannot express instead of
        // shipping a chord in the wrong octave.
        int displacement = Music.ChordRepetitions.DisplacementOf(q);
        if (displacement != 0)
            _warnings.Add(
                "a displaced chord repetition (q" + OctaveMarks(displacement) + ") has no LilyPond "
                + "spelling — LilyPond's q takes no octave marks, and \\transpose does not reach it "
                + "because chord repetitions expand after transposition. The twin writes a plain q, "
                + "so it sounds an octave away from the Lily# score; write the chord out by hand.");
        return body;
    }

    /// <summary>The written pitch that sits on the MIDDLE staff line of a clef -
    /// where Lily# draws every slash note. Step index per
    /// <see cref="Semantics.RelativeOctave.StepIndex"/>, octave in the c'=4
    /// convention.</summary>
    private static (int Step, int Octave) MiddleLinePitch(ClefType clef) => clef switch
    {
        ClefType.Bass => (1, 3),   // d
        ClefType.Alto => (0, 4),   // c'
        ClefType.Tenor => (5, 3),  // a
        _ => (6, 4),               // b' - treble, treble_8, and anything unmapped
    };

    /// <summary>
    /// A slash note. LilyPond has no pitchless slash token, so the twin spells it
    /// the way LilyPond users do: <c>\improvisationOn</c> (slash heads, no
    /// accidentals - ly/property-init.ly) around a pitch on the clef's middle
    /// line. The pitch moves LILYPOND's relative frame and not Lily#'s (a slash
    /// has no pitch), so only <see cref="_lyStep"/>/<see cref="_lyOctave"/>
    /// advance; <see cref="EmitMusicPitch"/> already compensates the next real
    /// pitch for diverged frames.
    /// </summary>
    private string EmitSlashNote(SlashNoteSyntax slash)
    {
        var (prefix, suffix) = SplitAttachments(slash.Articulations);
        string trem = slash.Tremolo is { } t ? t.Text : "";
        var (step, octave) = MiddleLinePitch(_lysClef);
        string pitchText;
        if (_octaveAbsolute)
        {
            pitchText = StepName(step) + OctaveMarks(octave - _absoluteBaseOctave);
        }
        else
        {
            int marks = octave - Semantics.RelativeOctave.Resolve(_lyStep, _lyOctave, step, 0);
            pitchText = StepName(step) + OctaveMarks(marks);
            _lyStep = step;
            _lyOctave = octave;
        }
        string on = _improvisationOpen ? "" : "\\improvisationOn ";
        _improvisationOpen = true;
        return on + prefix + pitchText + EmitEventDuration(slash.Duration) + trem + suffix;
    }

    /// <summary>Closes an open slash run before a pitched event; empty otherwise.</summary>
    private string CloseImprovisation()
    {
        if (!_improvisationOpen) return "";
        _improvisationOpen = false;
        return "\\improvisationOff ";
    }

    private static string StepName(int step) => "cdefgab"[step].ToString();

    /// <summary>
    /// A bare duration passes through verbatim - LilyPond 2.20+ reads an isolated
    /// duration as the previous note or chord again (LILYPOND-REF: lily/parser.yy
    /// music_embedded), which is Lily#'s reading too; both engines fill the pitch
    /// after relative resolution, so the octave frames are untouched. The number
    /// takes the normal written-duration carry, and the repetition's own
    /// post-events ride along.
    /// </summary>
    private string EmitBareDuration(BareDurationSyntax bare)
    {
        var (prefix, suffix) = SplitAttachments(bare.Articulations);
        string trem = bare.Tremolo is { } t ? t.Text : "";
        return prefix + EmitEventDuration(bare.Duration) + trem + suffix;
    }

    /// <summary>
    /// A pitch in the music stream: the source's own token, and the octave frames advanced
    /// past it. See <see cref="_lysStep"/> for why the marks are not always the source's.
    /// </summary>
    private string EmitMusicPitch(PitchSyntax p)
    {
        // \fixed has no frame: every mark is an absolute offset from the wrapper's c' —
        // plus whatever octave THIS section's reference asked for (see
        // _sectionOctaveOffset; zero for every play written without marks).
        if (_octaveAbsolute)
            return p.PitchToken.Text + OctaveMarks(p.OctaveOffset + _sectionOctaveOffset);

        int step = RelativeOctave.StepIndex(p.PitchName[0]);
        int source = p.OctaveOffset;
        int lys = RelativeOctave.Resolve(_lysStep, _lysOctave, step, source);
        // What LilyPond would do with the source's own marks, and what it takes to land on
        // Lily#'s note instead. The two agree — and this is source + 0 — unless a degree
        // chord has left the frames apart.
        int written = source + lys - RelativeOctave.Resolve(_lyStep, _lyOctave, step, source);
        _lysStep = _lyStep = step;
        _lysOctave = _lyOctave = lys;
        return p.PitchToken.Text + OctaveMarks(written);
    }

    /// <summary>Octave marks for a net shift: <c>'</c> up, <c>,</c> down.</summary>
    private static string OctaveMarks(int offset)
        => offset > 0 ? new string('\'', offset)
         : offset < 0 ? new string(',', -offset)
         : "";

    /// <summary>
    /// A resolved (step, alteration) as a LilyPond pitch name — the same suffixes the parser
    /// spells (<see cref="KeySpelling.SpellLetter"/>): <c>fis</c>, <c>bes</c>, <c>ees</c>.
    /// </summary>
    private string SpellPitch(int step, int alteration)
    {
        char letter = "cdefgab"[step];
        if (alteration is < -2 or > 2)
            _warnings.Add(
                $"a scale degree resolved to {Math.Abs(alteration)} accidentals on {letter}, "
                + "which LilyPond's note names do not spell — written as a double");
        int n = Math.Clamp(alteration, -2, 2);
        return letter + (n > 0 ? string.Concat(Enumerable.Repeat("is", n))
                       : n < 0 ? string.Concat(Enumerable.Repeat("es", -n))
                       : "");
    }

    private string EmitRest(RestSyntax r)
    {
        var (prefix, suffix) = SplitAttachments(r.Articulations);
        string mmr = r.IsMultiMeasure ? "*" + r.MeasureCount : "";
        if (Music.BarRest.IsBare(r))
        {
            // A bare `R` lasts its bar (Music.BarRest): LilyPond's `R1`, `R2.`, `R4*5` — and,
            // as it leaves Lily#'s running duration alone while LilyPond's moves to the value
            // written here, the next event states its own.
            _forceNextDuration = true;
            return prefix + "R" + Music.BarRest.LilyPondDuration(_bars.BarLength)
                + mmr + suffix;
        }
        return prefix + r.RestToken.Text + EmitEventDuration(r.Duration) + mmr + suffix;
    }

    /// <summary>
    /// A chord. Lily#'s chord-level octave marks go AFTER the <c>&gt;</c>
    /// (<c>&lt;d f a&gt;,</c> = the whole chord down an octave); LilyPond has no such
    /// spelling and rejects it outright (<c>syntax error, unexpected ','</c>), so the
    /// shift is pushed onto the members.
    /// </summary>
    /// <remarks>
    /// WHICH members depends on the mode this variable is wrapped in, and the two answers
    /// are different:
    /// <list type="bullet">
    /// <item><c>\fixed</c> — every pitch stands on its own against the reference, so every
    /// one of them carries the shift.</item>
    /// <item><c>\relative</c> — inside a chord each pitch is octaved against the PREVIOUS
    /// member, so shifting the first one carries the rest with it; adding the marks to all
    /// of them would move member N by N octaves.</item>
    /// </list>
    /// LILYPOND-REF: lily/music-sequence.cc:142-160 music_list_to_relative — walks the
    ///   members CHAINING <c>last = m-&gt;to_relative_octave (last)</c>, so member N is
    ///   octaved against member N-1, and returns the FIRST member when ret_first.
    /// LILYPOND-REF: lily/music-sequence.cc:213-219 event_chord_relative_callback — an
    ///   EventChord calls that with ret_first true, which is also why the chord's first
    ///   note is what the NEXT event octaves against
    ///   (scm/define-music-types.scm:268-269 wires the to-relative-callback).
    /// </remarks>
    private string EmitChord(ChordSyntax c)
    {
        if (c.IsShapeChord)
            return EmitShapeChord(c);
        int off = c.ChordOctaveOffset;
        // The incoming Lily# frame, for a chord that anchors nothing (drums only): it hands
        // the frame on shifted only by `off` — see the update at the end.
        int frameStepIn = _lysStep, frameOctaveIn = _lysOctave;
        // A member's fret diagram is the CHORD's post-event in the twin: LilyPond will not take a
        // text script on one note head of a chord ("cannot add text scripts to individual note
        // heads"), and the page draws the diagram for the chord's column anyway.
        var memberFrames = new StringBuilder();
        bool hasDegrees = c.Degrees.Any();
        if (hasDegrees && !_frameTracked)
            _warnings.Add(
                "a degree chord follows a phrase reference, whose nested \\relative leaves the "
                + "octave frame with a different answer on each side — check its octave by hand");

        // LilyPond's chain WITHIN the chord: each member is octaved against the one written
        // before it, so what a degree has to write depends on its neighbour, not on the root.
        int chainStep = _lyStep, chainOctave = _lyOctave;
        int firstStep = -1, firstOctave = 0;

        // Lily#'s anchor: the root's LETTER resolved bare in the incoming frame, plus the
        // whole-chord marks. The root's OWN marks are local to its sounding pitch — the anchor
        // is what the degrees stack on and what the next event is relative to
        // (MeasureCollector.ItemFactory CreateChordItem).
        int anchorStep = _lysStep, anchorOctave = _lysOctave;
        if (c.Root is { } root)
        {
            anchorStep = RelativeOctave.StepIndex(root.PitchName[0]);
            anchorOctave = _octaveAbsolute
                ? _absoluteBaseOctave + root.OctaveOffset + off
                : RelativeOctave.Resolve(_lysStep, _lysOctave, anchorStep, 0) + off;
        }
        else if (hasDegrees)
        {
            // Omitted root (<1 3 5>): degree 1 is the KEY'S TONIC, anchored in the frame as a
            // written root would be. A custom/atonal key has no tonic, so C — the collector's
            // own fallback.
            anchorStep = _tonic.Valid ? _tonic.Step : 0;
            anchorOctave = _octaveAbsolute
                ? _absoluteBaseOctave + off
                : RelativeOctave.Resolve(_lysStep, _lysOctave, anchorStep, 0) + off;
        }

        var sb = new StringBuilder("<");
        bool first = true;
        foreach (var p in c.Pitches)
        {
            if (!first) sb.Append(' ');
            if (first && !_octaveAbsolute)
            {
                // The root, written where Lily# sounds it: the anchor plus its own marks.
                int want = anchorOctave + p.OctaveOffset;
                sb.Append(p.PitchToken.Text)
                  .Append(OctaveMarks(want - RelativeOctave.Resolve(chainStep, chainOctave, anchorStep, 0)));
                chainStep = firstStep = anchorStep;
                chainOctave = firstOctave = want;
            }
            else if (_octaveAbsolute)
            {
                // Absolute: every pitch stands on its own, so every one carries the shift —
                // the chord-level marks, and the section reference's own (_sectionOctaveOffset).
                // ⚠️ ONE net figure, not the member's marks followed by the chord's: until
                // 2026-09-05 this appended the chord-level marks as a second string, so
                // `<b cis' fis>,` came out `<b, cis', fis,>` — and `cis',` is a LilyPond
                // syntax error (lily/parser.yy steno_pitch: sup_quotes OR sub_quotes, never
                // both). Found
                // by the twin of scratch/ベースタブLy/bench.lys refusing to compile.
                sb.Append(p.PitchToken.Text)
                  .Append(OctaveMarks(p.OctaveOffset + _sectionOctaveOffset + off));
            }
            else
            {
                // Relative: Lily# STACKS the member on the ROOT — same octave as the root,
                // bumped when its letter is below the root's, plus its own marks — while
                // LilyPond CHAINS member to member and takes the nearest. So the source's
                // marks are not the twin's: they are recomputed against the chain, exactly
                // as the degrees below are. LILYPOND-REF for the chain: music-sequence.cc
                // :142-160; Lily#'s rule: MeasureCollector.ItemFactory CreateChordItem
                // (`firstOctave + (step >= rootStepForStack ? 0 : 1) + pitch.OctaveOffset`),
                // which the collector's own comment calls a deliberate divergence.
                // ⚠️ Copying them verbatim made `<a c g>` a DIFFERENT CHORD in the twin
                // (Lily# A3 C4 G4, LilyPond A3 C4 G3) — measured on test/tab-beam-slope,
                // whose notation beam was the last one differing after gate ⑹.
                // ⚠️ The LETTER is still the source's, verbatim (PitchToken.Text carries the
                // accidental and any quarter tone) — only the octave MARKS are recomputed.
                int step = RelativeOctave.StepIndex(p.PitchName[0]);
                int want = anchorOctave + (step >= anchorStep ? 0 : 1) + p.OctaveOffset;
                sb.Append(p.PitchToken.Text)
                  .Append(OctaveMarks(want - RelativeOctave.Resolve(chainStep, chainOctave, step, 0)));
                chainStep = step;
                chainOctave = want;
            }

            // Member-level post-events. Lily# renders these per MEMBER
            // (ChordNoteInfo.HasLaissezVibrer/HasRepeatTie, NoteInChord.Fingering), so a
            // twin that dropped one would show fewer ties — or fewer digits — than its
            // source. The half-ties take the neutral `-`, the member form the regression
            // books themselves write (repeat-tie-chords.ly, laissez-vibrer-chords.ly);
            // ^/_ comes back from MapArticulation already prefixed.
            // ⚠️ THE FINGERING WAS DROPPED IN SILENCE UNTIL 2026-08-10, and it is the SAME
            // defect session 96 closed one level up: <c@finger(1) e@finger(3) g@finger(5)>
            // exported as a bare <c e g>, a twin that COMPILES and is DIFFERENT MUSIC. It
            // was caught building audit/lp-geometry/probes/chord-fingering.ly, whose two
            // books measure exactly the digits that went missing. The note-level hole was
            // caught by the exporter's WARNING; this one had none to raise, because a
            // member articulation the loop did not recognise simply fell out of the `if`.
            // ⇒ every unrecognised member node now warns, so the next hole in this family
            // is visible the way the last one was.
            // LILYPOND-REF: lily/parser.yy:3165-3166 chord_body_element — a chord member takes
            //   post-events (`<g-1 b-3 d'-5>`), the same spelling as a note's.
            // `<f? a>`: a member's @courtesy is spelt on its pitch, before the post-events.
            // (The page gives a MEMBER no @editorial — ItemFactory reads only the courtesy —
            // so a member's @editorial is left to the warning below, as before.)
            foreach (var art in p.Articulations)
                if (art is ArticulationSyntax ca && AccidentalMark(ca) == '?')
                {
                    sb.Append('?');
                    break;
                }
            foreach (var art in p.Articulations)
                switch (art)
                {
                    case ArticulationSyntax { Type: ArticulationType.None } ma
                        when ma.NameToken.Text is "laissezVibrer" or "repeatTie":
                        string ev = MapArticulation(ma);
                        if (ev[0] == '\\')
                            sb.Append('-');
                        sb.Append(ev);
                        break;
                    case ArticulationSyntax ca when AccidentalMark(ca) == '?':
                        break; // written on the pitch above
                    // A member's own script (`<c'@staccato e'>`, chord-scripts.ly) and string
                    // number (`<a,\2 d>`) take the note's spelling — a chord member takes
                    // post-events (chord_body_element). Until 2026-10-02 both were "dropped (out
                    // of scope)": 65 warnings, three of the owner's tab books among them.
                    case StringNumberAnnotationSyntax msn:
                        sb.Append(msn.StringNumberToken.Text);
                        break;
                    case ArticulationSyntax ma2 when MapArticulation(ma2) is { Length: > 0 } mev:
                        if (mev[0] == '\\')
                            sb.Append('-');
                        sb.Append(mev);
                        break;
                    case MusicMarkSyntax mk when Fingering(mk) is { } fg:
                        sb.Append(fg);
                        break;
                    case MusicMarkSyntax mk when FretDiagram(mk) is { } fd:
                        memberFrames.Append(fd);
                        break;
                    default:
                        _warnings.Add(
                            $"chord member {p.PitchName}: {art.GetType().Name} dropped (out of scope)");
                        break;
                }
            // A slur mark written on this member (<c e( g>) — LilyPond's note-slur, bound to
            // the head (lily/slur-engraver.cc:138-152). Written in source order after the
            // member's other post-events; the chord's own post-events below skip it.
            foreach (var (member, mark) in c.MemberSlurs)
                if (member.SourceStart == p.SourceStart)
                    sb.Append(mark.IsOpen ? '(' : ')');
            first = false;
        }

        // Scale-degree members (<d 3 5 7,>, <1 3 5>): resolved here, because LilyPond has no
        // spelling for a degree at all — it was the last thing this exporter dropped in
        // silence, and `<>` is a zero-length event, so test/chord-octave-marks failed its bar
        // check at 1/4 and read as a book with no beams. Same call the collector makes.
        foreach (var degree in c.Degrees)
        {
            if (!first) sb.Append(' ');
            var (step, alteration, octave) = ChordDegrees.Resolve(
                anchorStep, anchorOctave, degree.Number, degree.Alteration,
                degree.OctaveOffset, _keySharps);
            int written = _octaveAbsolute
                ? octave - _absoluteBaseOctave + _sectionOctaveOffset
                : octave - RelativeOctave.Resolve(chainStep, chainOctave, step, 0);
            sb.Append(SpellPitch(step, alteration)).Append(OctaveMarks(written));
            if (first) { firstStep = step; firstOctave = octave; }
            chainStep = step;
            chainOctave = octave;
            first = false;
        }

        // Drum members (<bd hh>): names, like a bare drum note, and only inside \drummode.
        foreach (var drum in c.DrumNames)
        {
            if (!_drumMode)
            {
                _warnings.Add(
                    $"drum chord member '{drum.DrumName}' is outside \\drummode and was dropped");
                continue;
            }
            if (!first) sb.Append(' ');
            sb.Append(drum.DrumName);
            first = false;
        }

        // Where the two sides stand now: Lily# on the chord's ANCHOR (the root's bare letter,
        // or the tonic, plus the whole-chord marks — MeasureCollector.ItemFactory
        // CreateChordItem), LilyPond on its FIRST member (ret_first, cited in this method's
        // remarks). Equal for an ordinary chord; a root with its own marks or a degree chord
        // parts them, and tracking the two apart is what lets the events AFTER the chord be
        // spelled against LilyPond's frame while sounding Lily#'s pitches.
        if (!_octaveAbsolute && firstStep >= 0)
        {
            bool anchored = c.Root is not null || hasDegrees;
            _lysStep = anchored ? anchorStep : frameStepIn;
            _lysOctave = anchored ? anchorOctave : frameOctaveIn + off;
            _lyStep = firstStep;
            _lyOctave = firstOctave;
        }

        sb.Append('>');
        sb.Append(EmitEventDuration(c.Duration));
        // The chord's post-events are the whole list less the member slurs written above.
        IEnumerable<SyntaxNode> chordArts = c.Articulations;
        if (c.MemberSlurs.Any())
        {
            var memberMarks = c.MemberSlurs.Select(ms => ms.Mark.SourceStart).ToHashSet();
            chordArts = c.Articulations.Where(a => !(a is SlurSyntax s && memberMarks.Contains(s.SourceStart))).ToList();
        }
        var (prefix, suffix) = SplitAttachments(chordArts);
        return prefix + sb.ToString() + suffix + memberFrames;
    }

    /// <summary>
    /// The notes of a <c>chord(…)</c> item (the page's ShapeNotesOf): the part's strings;
    /// outside a part (a phrase body, written once for every part that plays it) the one tuning
    /// the file's parts share, else the guitar — which <paramref name="warn"/> reports.
    /// </summary>
    private System.Collections.Immutable.ImmutableArray<Music.ShapeNote> ShapeNotesOf(ChordSyntax c, bool warn = false)
    {
        var (tuning, shift) = (TuningType.Guitar, 0);
        if (_currentPartName != null && _shared.Root is { } root)
        {
            var header = Semantics.PartHeaderDefaults.Read(Semantics.ConcertPitch.FindPart(root, _currentPartName));
            (tuning, shift) = (Music.ShapeChords.TuningOf(header), header.SoundingShiftSemitones);
        }
        else if (Music.ShapeChords.PartTuningsOf(c) is [var only])
            (tuning, shift) = (only.Tuning, only.SoundingShift);
        else if (warn)
            _warnings.Add("a chord(...) item in a phrase played by parts of different tunings is "
                + "written on the guitar's strings - check its notes by hand");
        // A capo raises every string by its fret (the page's ShapeNotesOf; 2026-09-29): the shift
        // is "sounding = written + shift", so the capo comes OFF it.
        return Music.ShapeChords.Notes(c, tuning, shift - _layoutPlan.Chords.Capo, _keySharps);
    }

    /// <summary>
    /// <c>chord(SYMBOL SHAPE)</c> written out: the shape's strings on the part's tuning
    /// (<see cref="Music.ShapeChords"/>, the page's reading) as an ordinary LilyPond chord,
    /// lowest sounding note first, each note with its string number — <c>&lt;c\5 e\4 g\3 c'\2
    /// g'\1&gt;1</c> in absolute terms — and, with no usable shape, a spacer of the item's length
    /// (LYS1040). LilyPond has no such item (LILYSHARP-OWN).
    /// </summary>
    /// <remarks>
    /// The octave marks are computed like a degree's (<see cref="EmitChord"/>): in <c>\relative</c>
    /// each note against the one before it, the first against LilyPond's frame; in
    /// <c>\fixed</c> against the part's base. The notes are written lowest first so that
    /// LilyPond's frame after the chord (its FIRST note) is the note Lily# hands on (its LOWEST,
    /// owner's decision 2026-09-28) — the two frames agree again after the item.
    /// </remarks>
    private string EmitShapeChord(ChordSyntax c)
    {
        var notes = ShapeNotesOf(c, warn: true);
        var (prefix, suffix) = SplitAttachments(c.Articulations);
        if (notes.IsEmpty)
            return prefix + "s" + EmitEventDuration(c.Duration) + suffix;
        if (!_octaveAbsolute && !_frameTracked)
            _warnings.Add(
                "a chord(...) item follows a phrase reference, whose nested \\relative leaves the "
                + "octave frame with a different answer on each side — check its octave by hand");

        var sb = new StringBuilder("<");
        int chainStep = _lyStep, chainOctave = _lyOctave;
        int firstStep = -1, firstOctave = 0;
        foreach (var n in Music.ShapeChords.Ascending(notes))
        {
            if (firstStep >= 0) sb.Append(' ');
            int marks = _octaveAbsolute
                ? n.Octave - _absoluteBaseOctave
                : n.Octave - RelativeOctave.Resolve(chainStep, chainOctave, n.Step, 0);
            sb.Append(SpellPitch(n.Step, n.Alter)).Append(OctaveMarks(marks))
              .Append('\\').Append(n.StringNumber);
            if (firstStep < 0) { firstStep = n.Step; firstOctave = n.Octave; }
            chainStep = n.Step;
            chainOctave = n.Octave;
        }
        if (!_octaveAbsolute)
        {
            _lysStep = _lyStep = firstStep;
            _lysOctave = _lyOctave = firstOctave;
        }
        sb.Append('>');
        sb.Append(EmitEventDuration(c.Duration));
        return prefix + sb + suffix;
    }

    /// <summary>
    /// The octave a bare letter means in absolute mode — the <c>\fixed c'</c> this exporter
    /// wraps every absolute part in, i.e. the octave of middle C.
    /// </summary>
    /// <remarks>
    /// Read off the part exactly as the collector reads it
    /// (<c>InstrumentDefaults.AbsoluteBaseOctave</c> = the explicit <c>octave N</c> property,
    /// or 4 — LilyPond's fixed <c>c'</c> — when the part declares none). The CLEF's default is
    /// deliberately not used: absolute octave is middle C whatever the clef, which is what
    /// <c>OctaveContext.OctaveBase</c> says too.
    /// <para>
    /// ⚠️ This was <c>const int = 4</c> until 2026-08-16, and its remark called the mismatch
    /// "an existing gate, not a new one". It was not a gate — no entry in the gate list
    /// excluded those books, so they were compared against LilyPond as different music:
    /// <c>test/octave-base.lys</c> declares <c>octave 3</c>, its own header says bare
    /// <c>c</c> is C3, the page draws C3, and the twin said <c>\fixed c'</c> = C4. A whole
    /// octave, in the one direction nothing was watching.
    /// </para>
    /// <para>
    /// It has to be ONE value for the whole part: the body's pitches are written with the
    /// SOURCE's own marks (see <c>EmitMusicPitch</c>'s absolute arm), so the wrapper is the
    /// only thing that decides what they sound, and the degree spellings below measure their
    /// marks from the same anchor. Move one without the others and the twin becomes wrong in
    /// a way that looks right.
    /// ⚠️ "The source's own marks" gained ONE addend on 2026-08-31 — a marked section
    /// reference's <see cref="_sectionOctaveOffset"/> — and it is deliberately NOT this
    /// value: see that field's remarks for why shifting the base instead would cancel.
    /// </para>
    /// </remarks>
    private int _absoluteBaseOctave = 4;

    /// <summary>
    /// ABSOLUTE mode's half of a marked section reference (<c>~B'</c>): the octaves to add
    /// to every pitch this play writes, reset at each section boundary by
    /// <c>EmitSectionPlay</c>.
    /// </summary>
    /// <remarks>
    /// It is a separate running value rather than a shift of <see cref="_absoluteBaseOctave"/>
    /// because the two are read in opposite directions and would CANCEL: the base is what the
    /// part's <c>\fixed</c> wrapper is written from and what a degree's marks are measured
    /// AGAINST (<c>octave − base</c>), so moving it up moves the anchor and the subtraction by
    /// the same amount and nothing lands anywhere new. What the twin has to say is "one octave
    /// higher than the source wrote", and that is an addend on the WRITTEN marks.
    /// ⚠️ A slash note deliberately does not read it: it stands on the clef's middle line in
    /// both engines and carries no pitch to shift (MeasureCollector does the same). MEASURED
    /// 2026-08-31, not assumed — <c>section B { /4 4 4 4 | }</c> played as <c>~B</c> and as
    /// <c>~B'</c> gives the same page and the same <c>b,4</c> here; the observer is
    /// <c>SectionReferenceOctaveTests.ASlashNoteDoesNotMove_ItStandsOnTheClefsMiddleLine</c>.
    /// </remarks>
    private int _sectionOctaveOffset;

    private static string EmitPitch(PitchSyntax p)
    {
        var sb = new StringBuilder(p.PitchToken.Text);
        int off = p.OctaveOffset;
        if (off > 0) sb.Append(new string('\'', off));
        else if (off < 0) sb.Append(new string(',', -off));
        return sb.ToString();
    }

    private static string EmitDuration(DurationSyntax? d)
        => d == null ? "" : d.NumberToken.Text + new string('.', d.DotCount);

    private string EmitBarline(BarlineSyntax b)
    {
        // A ':|' that EmitInlineRepeat did not consume is a ONE-SIDED end-repeat, and in
        // Lily# that means "repeat from the beginning of the piece" — which `\bar ":|."`
        // does NOT say. LilyPond's `\bar` is a glyph; only `\repeat volta` repeats. Since
        // 2026-09-10 EmitRewindRepeat wraps the preceding stream in `\repeat volta N { … }`
        // before the bar can get here, so this arm is reached only by a ':|' with NOTHING
        // before it (`form { :| A }` — nothing to repeat), where the twin draws the glyph
        // and plays the music once: a twin that COMPILES AND IS DIFFERENT MUSIC, the defect
        // class this exporter's warning channel exists for (see the remark on
        // Fingering_BecomesAnAttachedPostEvent). (Said once, from the music stream — a
        // chord track meets the same bar line.)
        if (b.BarToken.Kind == SyntaxKind.RepeatEndBar && !_chordTrack)
            _warnings.Add(
                "a one-sided ':|' repeats from the beginning of the piece in Lily#, but "
                + "LilyPond's \\bar \":|.\" only DRAWS the barline — the twin engraves the "
                + "same page and plays the music once");

        return b.BarToken.Kind switch
        {
            // Under \cadenzaOn a bare `|` is a bar CHECK that draws nothing; Lily#'s `|`
            // closes and draws the bar wherever it stands, so the twin writes the glyph.
            SyntaxKind.Bar => _bars.SenzaMisura ? "\\bar \"|\"" : "|",
            SyntaxKind.DoubleBar => "\\bar \"||\"",
            SyntaxKind.FinalBar => "\\bar \"|.\"",
            SyntaxKind.DashedBar => "\\bar \"!\"",
            // Repeat barlines are consumed by EmitInlineRepeat; a stray one is a
            // best-effort fallback.
            SyntaxKind.RepeatStartBar => "\\bar \".|:\"",
            SyntaxKind.RepeatEndBar => "\\bar \":|.\"",
            SyntaxKind.RepeatBothBar => "\\bar \":|.|:\"",
            _ => "|",
        };
    }

    /// <summary>
    /// A key signature — and the running key a scale-degree chord stacks in, advanced here
    /// because this is the one place a key is WRITTEN (the file's own settings, a section's
    /// header, and a mid-stream change all come through it, in emission order).
    /// </summary>
    private string EmitKey(KeySignatureSyntax k)
    {
        _tonic = KeyTonic.Of(k);
        // MeasureCollector.CalculateKeySharps — PitchName, which carries the accidental
        // suffix and normalizes LilyPond's `es`/`as` contractions the table does not hold.
        _keySharps = k.IsCustom ? 0 : KeySpelling.SharpsFor(k.Pitch.PitchName, k.Mode.Text) ?? 0;
        // A drum part writes NO key: inside \drummode the tonic is read as a drum name
        // (`\key f \major` fails: "Expecting pitch, found \"f\""), and the DrumStaff has no
        // Key_engraver to draw one anyway (ly/engraver-init.ly:297) — the page draws none
        // either (SpacingRules.ClefEngravesKey). Until 2026-09-26 a keyed book with a drum
        // part exported a twin LilyPond refused (a big-band probe in F).
        if (_drumMode) return "";
        if (k.IsCustom) { _warnings.Add("custom key signature emitted as \\key c \\major (unsupported)"); return "\\key c \\major"; }
        string mode = k.IsMajor ? "major" : k.Mode.Text.ToLowerInvariant();
        return "\\key " + EmitPitch(k.Pitch) + " \\" + mode;
    }

    /// <summary>
    /// A written meter, and the one place the RUNNING meter advances — so a section
    /// boundary can tell whether the score meter still stands (see EmitSectionPlay).
    /// </summary>
    private string EmitTime(TimeSignatureSyntax ts)
    {
        // `time none` → \cadenzaOn; the next metered `time` → \cadenzaOff \time N/M. LilyPond
        // 2.26.0's \cadenzaOff is only `\set Timing.timing = ##t` (ly/property-init.ly:284) and
        // resets nothing else, so the \time that follows it is the re-arm — and it PRINTS, as
        // every \time event does (measured, scratch/p354/lp/senza-reprint.ly), which is what
        // the page draws for it too (MeasureCollector.MusicWalk's TimeSignatureChangeItem).
        bool wasSenza = _bars.SenzaMisura;
        if (!_bars.SetTime(ts))   // `time none`: the last metered pair stays in force
        {
            // Opened mid-bar: LilyPond's clock freezes there and the return will need a
            // \partial (see _cadenzaOpenedMidBar). A second `time none` changes nothing.
            if (!wasSenza)
                _cadenzaOpenedMidBar = _timeSinceBoundary;
            return "\\cadenzaOn";
        }
        return wasSenza
            ? "\\cadenzaOff " + TimeText(ts) + CadenzaReturnPartial(new Fraction(ts.Beats, ts.BeatType))
            : TimeText(ts);
    }

    /// <summary>The <c>\partial</c> a return from a cadenza opened mid-bar needs after its
    /// <c>\time</c> — the whole of the returning meter, so measurePosition lands on 0 (see
    /// <see cref="_cadenzaOpenedMidBar"/>); empty when the cadenza opened at a bar line.</summary>
    private string CadenzaReturnPartial(Fraction measureLength)
    {
        if (!_cadenzaOpenedMidBar)
            return "";
        _cadenzaOpenedMidBar = false;
        return " \\partial " + ChordModeDuration(measureLength);
    }

    /// <summary>An additive meter (<c>time 3+2/8</c>) in LilyPond 2.26.0's spelling: the
    /// numerator as a Scheme list, <c>\time #'((3 2) . 8)</c>. MEASURED 2026-09-17
    /// (LilySharp-Lab/sessions/p398/probes/r14): <c>\time 3+2/8</c> is a syntax error
    /// ("unexpected '+'"), <c>\compoundMeter</c> is no longer a command, and the pair
    /// spelling compiles.
    /// LILYPOND-REF: ly/music-functions-init.ly:2375-2410 time, sane-time-signature? — "The
    ///   numerator is one number or a list of two or more numbers. A list represents
    ///   concatenation."</summary>
    private static string TimeText(TimeSignatureSyntax ts)
        => TimeText(new Semantics.Meter(ts.Beats, ts.BeatType, ts.BeatsText));

    /// <summary>The same spelling for the meter in force (<see cref="TimeTextInForce"/>).</summary>
    private static string TimeText(Semantics.Meter meter)
    {
        string beats = meter.BeatsText ?? meter.Beats.ToString();
        if (beats.Contains('+'))
            return "\\time #'((" + beats.Replace("+", " ") + ") . " + meter.BeatType + ")";
        return "\\time " + beats + "/" + meter.BeatType;
    }

    /// <summary>
    /// <c>\tempo "Andante" 4 = 66</c> — the marking AND the metronome mark when the book
    /// writes both; until 2026-09-23 a tempo with both lost its text in the twin (the
    /// nocturne sample's "Andante espressivo").
    /// LILYPOND-REF: ly/music-functions-init.ly tempo = define-music-function
    ///   ((text) duration tempo) — <c>\tempo [text] [duration = count]</c>, either part optional.
    /// </summary>
    private static string EmitTempo(TempoDeclarationSyntax t)
    {
        string text = !string.IsNullOrEmpty(t.Marking) ? " \"" + Escape(t.Marking!) + "\"" : "";
        if (t.SwingSubdivision != 0)
            return EmitSwingTempo(t, text);
        if (t.Bpm is int bpm)
        {
            int unit = t.BeatUnit is int u ? u : 4;
            string dots = new string('.', t.BeatDots);
            return $"\\tempo{text} {unit}{dots} = {bpm}";
        }
        if (text.Length > 0)
            return "\\tempo" + text;
        return "";
    }

    /// <summary>
    /// <c>tempo 122 swing</c> as LilyPond's own swing idiom (the <c>\rhythm</c> doc example):
    /// <c>\tempo \markup { [marking] [♩ = 122] \hspace #0.4 \rhythm { 8[ 8] } = \rhythm
    /// { \tuplet 3/2 { 4 8 } } }</c> — sixteenths a value down. The count is drawn inside the
    /// markup in the metronome's plain face, so a written bpm still sets the tempo but its
    /// own printing is hidden (<c>tempoHideNote</c>) — one mark, as Lily# draws it.
    /// LILYPOND-REF: scm/define-markup-commands.scm:1920-1990 define-markup-command (rhythm …);
    /// scm/translation-functions.scm:100-151 format-metronome-markup (tempoHideNote).
    /// </summary>
    private static string EmitSwingTempo(TempoDeclarationSyntax t, string text)
    {
        var sb = new StringBuilder("\\tempo \\markup {");
        sb.Append(text);
        string unit = (t.BeatUnit is int u ? u : 4) + new string('.', t.BeatDots);
        if (t.Bpm is int bpm)
            sb.Append(" \\normal-text \\concat { ")
              .Append(text.Length > 0 ? "\"(\" " : "")
              .Append("\\smaller \\general-align #Y #DOWN \\note {").Append(unit).Append("} #UP \" = ")
              .Append(bpm).Append(text.Length > 0 ? ")" : "").Append("\" }");
        if (text.Length > 0 || t.Bpm != null)
            sb.Append(" \\hspace #0.4");
        sb.Append(t.SwingSubdivision >= 16
            ? " \\rhythm { 16[ 16] } = \\rhythm { \\tuplet 3/2 { 8 16 } } }"
            : " \\rhythm { 8[ 8] } = \\rhythm { \\tuplet 3/2 { 4 8 } } }");
        if (t.Bpm is int count)
            return "\\once \\set Score.tempoHideNote = ##t " + sb + " " + unit + " = " + count;
        return sb.ToString();
    }

    /// <summary>
    /// A dynamic as LilyPond spells it: the hairpin triggers are the WEDGE events
    /// <c>\&lt;</c> / <c>\&gt;</c>, every level its own command. Until 2026-09-23 the twin
    /// wrote <c>\cresc</c>, which in LilyPond is the TEXT spanner "cresc." with a dashed
    /// line (span-type 'text), where <c>@cresc</c> draws a hairpin (HairpinEngraver) — the
    /// twin printed different music and compiled without a word.
    /// LILYPOND-REF: ly/declarations-init.ly:89-90 "\\&gt;" / "\\&lt;" = make-span-event
    ///   'DecrescendoEvent / 'CrescendoEvent START;
    /// LILYPOND-REF: ly/spanners-init.ly:56-60 endcresc = make-span-event STOP, and cresc /
    ///   dim / decresc = make-music … 'span-type 'text.
    /// ⚠️ No <c>\!</c> is synthesised: Lily#'s hairpin ends at the next dynamic or hairpin on
    ///   its staff (HairpinEngraver.DetectHairpins) exactly as LilyPond's does, and where
    ///   neither follows Lily# runs the wedge to the next bar (that engraver's own rule,
    ///   declared there) while LilyPond warns "unterminated crescendo" and draws none —
    ///   that book's twin is not the page, and the warning is LilyPond's to give.
    /// </summary>
    /// <remarks>
    /// An explicit side (<c>@f.up</c> / <c>@f.down</c>) is written as LilyPond's <c>^</c> / <c>_</c>
    /// before the event, the way the page takes it (MeasureCollector reads
    /// <c>DynamicSyntax.ForcedAbove</c>). Until 2026-09-26 it was dropped, so the twin drew
    /// `@f.up` below the staff (Lab sessions/p644 x4).
    /// </remarks>
    private static string EmitDynamic(DynamicSyntax d) => IsSecondDynamicLevel(d) ? "" : (d.ForcedAbove switch
    {
        true => "^",
        false => "_",
        null => "",
    }) + d.DynamicToken.Text switch
    {
        "cresc" => "\\<",
        "decresc" or "dim" => "\\>",
        var level => "\\" + level,
    };

    /// <summary>Whether <paramref name="d"/> is a dynamic LEVEL after the first on its note —
    /// not printed on the page (LYS4022), and not written to the twin either, where LilyPond
    /// would discard it with a warning.</summary>
    private static bool IsSecondDynamicLevel(DynamicSyntax d)
    {
        if (d.Level == DynamicLevel.None || d.Parent is not { } host)
            return false;
        for (int i = 0; i < host.SlotCount; i++)
            if (host.GetChild(i) is DynamicSyntax { Level: not DynamicLevel.None } first)
                return first.Span.Start != d.Span.Start;
        return false;
    }

    private static string EmitPartial(PartialDeclarationSyntax p)
    {
        var d = p.Duration;
        return d == null ? "" : "\\partial " + d.NumberToken.Text + new string('.', d.DotCount);
    }

    /// <summary>Writes the <c>\partial</c> and remembers its length for an empty bar written
    /// inside the pickup (<c>_bars.Partial</c>, <see cref="Semantics.BarContext.SetPartial"/>).</summary>
    private string ArmPartial(PartialDeclarationSyntax p)
    {
        // Under `time none` the page's clock stands still and the pickup shortens nothing
        // (LYS2015). LilyPond's \partial under \cadenzaOn would MOVE its frozen measurePosition
        // (mid-piece: measureLength − dur) and the next \cadenzaOff \time then returns
        // mid-measure — a different book (MEASURED 2.26.0, scratch/p359/lp/partial-senza.ly:
        // bar check failed at 3/4, an automatic bar inside the following whole note). Not
        // written, and said so.
        if (_bars.SenzaMisura)
        {
            _warnings.Add(
                "a 'partial' inside 'time none' is not exported: the clock stands still there "
                + "and the page ignores it (LYS2015), where LilyPond's \\partial would move its "
                + "frozen measurePosition");
            return "";
        }
        if (p.Duration != null)
            _bars.SetPartial(p.ToFraction());
        return EmitPartial(p);
    }

    private string EmitTuplet(TupletExpressionSyntax tup)
    {
        var buf = OpenNested();
        buf.EmitMusicStream(MusicItems(tup.Body).ToList(), "");
        // A tuplet is plain sequential music on both sides: its notes are in the enclosing
        // frame and the note after it follows the tuplet's last, so the frame comes back.
        CarryFrameBack(buf);
        string body = NestedText(buf);
        return $"\\tuplet {tup.Numerator.Text}/{tup.Denominator.Text} {{ {body} }}";
    }

    /// <summary>
    /// A written-out broken chord (<c>&lt;&lt; c e g &gt;&gt;</c>) as the tuplet LilyPond
    /// needs to say it with: the members at <see cref="ArpeggioSubdivision"/>'s value under its
    /// M/P bracket — <c>\tuplet 3/2 { c8 e g }</c> — or plain notes when the frame holds them
    /// exactly (<c>&lt;&lt; c e g a &gt;&gt;</c> after a quarter is <c>c16 e g a</c>).
    /// </summary>
    /// <remarks>
    /// <para>OCTAVES. Lily# STACKS every pitched member on the ROOT — the first pitched
    /// member, its letter resolved bare in the incoming frame plus the group's own marks —
    /// exactly as a chord's members stack, and the event after the group is relative to that
    /// root (MeasureCollector.MusicWalk ProcessArpeggio; degrees stack by ChordDegrees on the
    /// same anchor, or on the key's tonic when no pitched member precedes; a nested chord
    /// stacks by its first letter and its own members are then read in ABSOLUTE mode on that
    /// octave, so <c>&lt;&lt; c &lt;g e&gt; &gt;&gt;</c> is C E-below-G, not E stacked above G).
    /// LilyPond's <c>\relative</c> reads the members as a SEQUENCE, each against the previous
    /// pitch with rests passed over, so every mark is recomputed against that chain — the
    /// source's marks are not the twin's — and after the group the two frames part (Lily# on
    /// the group's anchor, the marks after <c>&gt;&gt;</c> included; LilyPond on the last
    /// pitch) the way a degree chord parts them;
    /// <see cref="EmitMusicPitch"/> closes the gap on the next note.
    /// LILYPOND-REF: lily/music-sequence.cc:142-160 music_list_to_relative — the chain, and
    ///   a rest has no pitch to hand it, which is why a rest leaves <c>last</c> alone.</para>
    /// <para>DURATIONS. The members carry none, so the first writes the subdivision's value
    /// and the rest inherit it inside the tuplet, as both parsers carry. After the group
    /// LilyPond would carry that member value while Lily# carries the trailing
    /// <c>&gt;&gt;N</c> (dots and all) or, absent one, what ran before — so the next event is
    /// forced to write Lily#'s value (<see cref="EmitEventDuration"/>).</para>
    /// <para>Until 2026-09-07 this node fell to <see cref="Skip"/>: a twin with an "Arpeggio
    /// not exported" warning and a bar short by the group's whole duration, so no
    /// <c>&lt;&lt; … &gt;&gt;</c> book could be measured against LilyPond at all — found when
    /// ArpeggioSubdivision's spelling needed its LilyPond picture and the twin could only be
    /// built from the hand-written tuplet.</para>
    /// </remarks>
    private string EmitArpeggio(ArpeggioSyntax arp)
    {
        // A chord(…) member spread into its notes (the page's MeasureCollector.ProcessArpeggio).
        var members = Music.ArpeggioSpread.Of(arp, c => ShapeNotesOf(c));
        if (members.Count == 0)
            return "";

        // The group's total: the trailing `>>N`, or the running duration Lily# would give a
        // bare note here — value AND dots (MeasureCollector.ItemFactory carries both).
        string runningValue = _lastWrittenValue;
        int runningDots = _lastWrittenDots;
        Fraction total = arp.TotalDuration?.ToFraction()
            ?? Fraction.FromNoteValue(int.TryParse(runningValue, out int rv) ? rv : 4).Dotted(runningDots);
        var sub = ArpeggioSubdivision.Compute(Music.ArpeggioSpread.ShareCount(members), total);
        int groupOctave = arp.OctaveOffset;

        if (!_octaveAbsolute && !_frameTracked && members.Any(m => m.Member.Node is ScaleDegreeSyntax))
            _warnings.Add(
                "a degree arpeggio follows a phrase reference, whose nested \\relative leaves the "
                + "octave frame with a different answer on each side — check its octave by hand");

        // Group-level post-events (`>>@f`): the prefix goes before the tuplet, the suffix
        // rides on the FIRST member, which is where Lily# sounds them (CollectDynamics). A
        // string number on the group is every member's (the collector's groupString), so it
        // is written on each member that names none of its own, not on the first.
        string? groupString = arp.Articulations.OfType<StringNumberAnnotationSyntax>()
            .FirstOrDefault()?.StringNumberToken.Text;
        var (groupPrefix, groupSuffix) = SplitAttachments(
            arp.Articulations.Where(a => a is not StringNumberAnnotationSyntax));

        var body = new StringBuilder();
        // The incoming Lily# frame, for a group with no pitched member (see the update after
        // the member loop).
        int savedFrameStep = _lysStep, savedFrameOctave = _lysOctave;
        bool rootSet = false;
        int rootStep = 0, anchorOctave = 0;
        for (int i = 0; i < members.Count; i++)
        {
            var ((member, shares, slurStart, slurEnd, _, _), spreadNote) = members[i];
            // What the member's shares spell: one note, or notes tied to one another
            // (a rest's parts stand apart). Every part writes its duration.
            var parts = sub.SpellShares(shares);
            if (i > 0)
                body.Append(' ');
            string head;               // the member without its duration
            string prefix = "", suffix = "";
            bool tieParts = true;
            switch (member)
            {
                case RestSyntax rest:
                    (prefix, suffix) = SplitAttachments(rest.Articulations);
                    head = rest.RestToken.Text;
                    tieParts = false;
                    break;

                case ScaleDegreeSyntax degree:
                {
                    if (!rootSet)
                    {
                        // Degrees before any pitched member anchor on the KEY'S TONIC (C when
                        // the key has none), which then becomes the group's anchor.
                        rootSet = true;
                        rootStep = _tonic.Valid ? _tonic.Step : 0;
                        anchorOctave = (_octaveAbsolute ? _absoluteBaseOctave
                            : RelativeOctave.Resolve(_lysStep, _lysOctave, rootStep, 0)) + groupOctave;
                    }
                    var (step, alteration, octave) = ChordDegrees.Resolve(
                        rootStep, anchorOctave, degree.Number, degree.Alteration,
                        degree.OctaveOffset, _keySharps);
                    head = SpellPitch(step, alteration) + OctaveMarks(ArpeggioMarks(step, octave));
                    suffix = groupString ?? "";
                    AdvanceLilyPondFrame(step, octave);
                    break;
                }

                case PitchSyntax p:
                {
                    int step = RelativeOctave.StepIndex(p.PitchName[0]);
                    int want;
                    if (!rootSet)
                    {
                        // The root. Relative: the anchor is its bare letter in the frame plus
                        // the group's marks, and its own marks are local. Absolute: there is
                        // no frame, so the anchor is where it sounds, marks included — the
                        // collector's two arms (BuildArpeggioNoteItems).
                        rootSet = true;
                        rootStep = step;
                        if (_octaveAbsolute)
                        {
                            anchorOctave = _absoluteBaseOctave + p.OctaveOffset + groupOctave;
                            want = anchorOctave;
                        }
                        else
                        {
                            anchorOctave = RelativeOctave.Resolve(_lysStep, _lysOctave, step, 0) + groupOctave;
                            want = anchorOctave + p.OctaveOffset;
                        }
                    }
                    else
                    {
                        // Stacked on the root: the root's octave, bumped when the letter is
                        // below the root's, plus its own marks.
                        want = anchorOctave + (step >= rootStep ? 0 : 1) + p.OctaveOffset;
                    }
                    (prefix, suffix) = SplitAttachments(p.Articulations);
                    if (groupString != null && !p.Articulations.OfType<StringNumberAnnotationSyntax>().Any())
                        suffix += groupString;
                    head = p.PitchToken.Text + OctaveMarks(ArpeggioMarks(step, want));
                    AdvanceLilyPondFrame(step, want);
                    break;
                }

                // A spread note of a chord(…) member: that one note, absolute, on its string —
                // the root when it comes first (its lowest note, as the item hands on).
                case ChordSyntax spreadItem when spreadNote is { } sn:
                {
                    if (!rootSet)
                    {
                        rootSet = true;
                        rootStep = sn.Step;
                        anchorOctave = sn.Octave;
                    }
                    head = SpellPitch(sn.Step, sn.Alter) + OctaveMarks(ArpeggioMarks(sn.Step, sn.Octave));
                    (prefix, suffix) = SplitAttachments(spreadItem.Articulations);
                    suffix = "\\" + sn.StringNumber + suffix;
                    AdvanceLilyPondFrame(sn.Step, sn.Octave);
                    break;
                }

                // A chord(…) member with no shape on the tuning: a spacer of its share, as the
                // item alone is (EmitShapeChord).
                case ChordSyntax { IsShapeChord: true } unshaped:
                    (prefix, suffix) = SplitAttachments(unshaped.Articulations);
                    head = "s";
                    tieParts = false;
                    break;

                case ChordSyntax chord:
                    head = EmitArpeggioChord(chord, groupOctave, ref rootSet, ref rootStep, ref anchorOctave);
                    (prefix, suffix) = SplitAttachments(chord.Articulations);
                    break;

                default:
                    continue;
            }

            body.Append(prefix);
            for (int k = 0; k < parts.Count; k++)
            {
                bool first = k == 0, last = k == parts.Count - 1;
                if (!first)
                    body.Append(' ');
                body.Append(head).Append(parts[k].Value).Append('.', parts[k].Dots);
                if (first)
                {
                    body.Append(suffix);
                    if (i == 0)
                        body.Append(groupSuffix);
                    if (slurStart)
                        body.Append('(');
                }
                if (last && slurEnd)
                    body.Append(')');
                if (!last && tieParts)
                    body.Append('~');
            }
        }

        // After the group Lily# stands on its ANCHOR — the root's bare letter (or the tonic)
        // plus the marks after '>>', the chord rule (MeasureCollector.MusicWalk
        // ProcessArpeggio); a group of rests hands the incoming frame on, shifted by the
        // marks. LilyPond's already stands on the last pitch written (AdvanceLilyPondFrame),
        // and EmitMusicPitch closes the gap on the next note.
        if (!_octaveAbsolute)
        {
            _lysStep = rootSet ? rootStep : savedFrameStep;
            _lysOctave = rootSet ? anchorOctave : savedFrameOctave + groupOctave;
        }

        // Lily#'s carry after the group: the trailing `>>N` whole, or what ran before it.
        // LilyPond's is the member value, so the next event writes Lily#'s out.
        if (arp.TotalDuration is { } td)
        {
            _lastWrittenValue = td.NumberToken.Text;
            _lastWrittenDots = td.DotCount;
        }
        else
        {
            _lastWrittenValue = runningValue;
            _lastWrittenDots = runningDots;
        }
        _forceNextDuration = true;

        string inner = body.ToString();
        return groupPrefix + (sub.HasTuplet
            ? $"\\tuplet {sub.TupletNum}/{sub.TupletBase} {{ {inner} }}"
            : inner);
    }

    /// <summary>The octave marks that put a pitch at <paramref name="octave"/>: against
    /// LilyPond's running relative frame, or against the <c>\fixed</c> base plus the section
    /// reference's shift in absolute mode.</summary>
    private int ArpeggioMarks(int step, int octave) => _octaveAbsolute
        ? octave - _absoluteBaseOctave + _sectionOctaveOffset
        : octave - RelativeOctave.Resolve(_lyStep, _lyOctave, step, 0);

    /// <summary>LilyPond's frame moves past every pitch it reads in a sequence; Lily#'s is
    /// set once, after the group (the root).</summary>
    private void AdvanceLilyPondFrame(int step, int octave)
    {
        if (_octaveAbsolute)
            return;
        _lyStep = step;
        _lyOctave = octave;
    }

    /// <summary>
    /// A chord member of an arpeggio (<c>&lt;&lt; &lt;c e&gt; g &gt;&gt;</c>), written where the
    /// collector sounds it (MeasureCollector.ItemFactory CreateChordItem through
    /// MusicWalk.EmitArpeggioMember): as the ROOT it is an ordinary chord in the incoming
    /// frame with the group's marks folded in — its members stack on its own root in relative
    /// mode; STACKED, its first letter takes the stacked octave and every member is then read
    /// in ABSOLUTE mode on that octave (its own marks and the chord's, no stacking). Degrees
    /// resolve on the chord's root either way. Written against LilyPond's chain member by
    /// member, whose frame then stands on the chord's FIRST member
    /// (LILYPOND-REF: lily/music-sequence.cc:213-219 event_chord_relative_callback).
    /// Returns the <c>&lt;…&gt;</c> alone — no duration, no post-events.
    /// </summary>
    private string EmitArpeggioChord(ChordSyntax chord, int groupOctave,
        ref bool rootSet, ref int rootStep, ref int anchorOctave)
    {
        int chordOff = chord.ChordOctaveOffset;
        var pitches = chord.Pitches.ToList();
        char? letter = RelativeOctave.FirstPitchLetter(chord);
        bool isRoot = !rootSet;

        // The chord's own root letter, the octave its degrees stack on (CreateChordItem's
        // firstOctave: the bare anchor in relative mode, the root's sounding octave in
        // absolute mode), where its first pitch sounds, and — for a stacked chord — the base
        // every member is read from.
        int chordRootStep, chordRootOctave, firstPitchOctave = 0, stackedBase = 0;
        if (letter is { } l)
        {
            chordRootStep = RelativeOctave.StepIndex(l);
            int rootMarks = pitches[0].OctaveOffset;
            if (isRoot)
            {
                rootSet = true;
                rootStep = chordRootStep;
                if (_octaveAbsolute)
                {
                    anchorOctave = _absoluteBaseOctave + rootMarks + chordOff + groupOctave;
                    chordRootOctave = firstPitchOctave = anchorOctave;
                }
                else
                {
                    anchorOctave = RelativeOctave.Resolve(_lysStep, _lysOctave, chordRootStep, 0)
                        + chordOff + groupOctave;
                    chordRootOctave = anchorOctave;
                    firstPitchOctave = anchorOctave + rootMarks; // its own marks are local
                }
            }
            else
            {
                stackedBase = anchorOctave + (chordRootStep >= rootStep ? 0 : 1);
                chordRootOctave = firstPitchOctave = stackedBase + rootMarks + chordOff;
            }
        }
        else
        {
            // No pitch at all (<1 3 5>): the tonic is the chord's root, as CreateChordItem
            // reads it; as the group's root it becomes the anchor as well.
            chordRootStep = _tonic.Valid ? _tonic.Step : 0;
            if (isRoot)
            {
                rootSet = true;
                rootStep = chordRootStep;
                anchorOctave = (_octaveAbsolute ? _absoluteBaseOctave
                    : RelativeOctave.Resolve(_lysStep, _lysOctave, chordRootStep, 0)) + chordOff + groupOctave;
                chordRootOctave = anchorOctave;
            }
            else
            {
                stackedBase = anchorOctave + (chordRootStep >= rootStep ? 0 : 1);
                chordRootOctave = stackedBase + chordOff;
            }
        }

        var sb = new StringBuilder("<");
        bool first = true;
        int firstStep = -1, firstOctave = 0;
        foreach (var p in pitches)
        {
            if (!first) sb.Append(' ');
            int step = RelativeOctave.StepIndex(p.PitchName[0]);
            int octave;
            if (first)
                octave = firstPitchOctave;
            else if (isRoot && !_octaveAbsolute)
                octave = anchorOctave + (step >= chordRootStep ? 0 : 1) + p.OctaveOffset; // stacked on the chord's root
            else if (isRoot)
                octave = _absoluteBaseOctave + p.OctaveOffset + chordOff + groupOctave;
            else
                octave = stackedBase + p.OctaveOffset + chordOff;                          // absolute on the stacked base
            sb.Append(p.PitchToken.Text).Append(OctaveMarks(ArpeggioMarks(step, octave)));
            foreach (var art in p.Articulations)
                _warnings.Add($"arpeggio chord member {p.PitchName}: {art.GetType().Name} dropped (out of scope)");
            if (first) { firstStep = step; firstOctave = octave; }
            AdvanceLilyPondFrame(step, octave);
            first = false;
        }
        foreach (var degree in chord.Degrees)
        {
            if (!first) sb.Append(' ');
            var (step, alteration, octave) = ChordDegrees.Resolve(
                chordRootStep, chordRootOctave, degree.Number, degree.Alteration,
                degree.OctaveOffset, _keySharps);
            sb.Append(SpellPitch(step, alteration)).Append(OctaveMarks(ArpeggioMarks(step, octave)));
            if (first) { firstStep = step; firstOctave = octave; }
            AdvanceLilyPondFrame(step, octave);
            first = false;
        }
        foreach (var drum in chord.DrumNames)
            _warnings.Add($"arpeggio chord member '{drum.DrumName}': a drum name inside << >> is not exported");
        sb.Append('>');
        // LilyPond's frame after a chord is its FIRST member, not its last.
        if (firstStep >= 0)
            AdvanceLilyPondFrame(firstStep, firstOctave);
        // The caller writes the duration(s) and the chord's own post-events: a chord
        // holding several shares is written once per part, tied.
        return sb.ToString();
    }

    /// <summary>
    /// A <c>voice { … } voice { … }</c> run as LilyPond's simultaneous-voice shorthand.
    /// </summary>
    /// <remarks>
    /// LilyPond's <c>&lt;&lt; { … } \\ { … } &gt;&gt;</c> is not merely "these play together":
    /// the <c>\\</c> separator creates a Voice per branch AND applies \voiceOne, \voiceTwo, …
    /// to them (ly/engraver-init.ly), which is where the forced stem directions come from.
    /// That is the same rule Lily# bakes into the model — MeasureCollector's
    /// ResolveVoiceStemDirections, via VoiceDefaults.GetDefaultStemUp — so the two sides agree
    /// by construction.
    /// <para>
    /// ⚠️ A LONE voice block emits its contents bare, with no wrapper. Both engines leave a
    /// single voice's stems to the pitch rule (ResolveVoiceStemDirections returns early at
    /// <c>voices.Length &lt;= 1</c>; LilyPond applies no voice settings without a <c>\\</c>),
    /// and wrapping it would not change that on either side — but the bare form says so.
    /// </para>
    /// <para>
    /// ⚠️ Before this existed the whole run fell to <see cref="Skip"/> ("ParallelExpression not
    /// exported", 29 of them across the corpus) and every polyphonic book exported as an EMPTY
    /// staff — 11 twins, which the twin sweep then read as layout divergence. That is the
    /// FOURTH hole of this shape; the other three were VariableReference, phrase references and
    /// the relative-octave anchor. docs/HANDOFF.md §1 gate list.
    /// </para>
    /// </remarks>
    private string EmitParallel(ParallelExpressionSyntax par)
    {
        // The two sides read a span completely differently, so this is where the octave
        // frames earn their keep:
        //   Lily# — every branch reads from the frame the span OPENED in, and so does the
        //     music after it: a span is simultaneous music and moves nothing
        //     (MeasureCollector's _parallelSpans).
        //   LilyPond — the branches CHAIN into one another and the span hands out the LAST
        //     one's pitch. Measured with `c4 c c c << { c''1 } \\ { c,,,1 } >> c1`, which
        //     reads C4 C4 C4 C4 / C6 / C3 / C3: branch 2 is octaved against branch 1's end,
        //     and the note after the span against branch 2's.
        // So each branch is emitted with Lily#'s frame set to the span's and LilyPond's set
        // to wherever the previous branch left it; every first pitch then absorbs the
        // difference by itself (EmitMusicPitch), and so does the first pitch after the span.
        int spanStep = _lysStep, spanOctave = _lysOctave;
        int chainStep = _lyStep, chainOctave = _lyOctave;
        // The note-value default parts the same way (session 398, MeasureCollector.MusicWalk's
        // rule): Lily# opens every branch, and the music after the span, at the value in
        // force where the span opened; LilyPond's parser carries the last value WRITTEN, so
        // it reads branch 2's first bare note from branch 1's end and the note after the
        // span from the last branch's end. Each branch therefore starts from the span's
        // value with its first event forced to write it out, and so does the first event
        // after the span. (Until then the nested buffers started at a fresh quarter and the
        // main stream carried the last branch's value — `c8 << { d e } \\ { f g } >> a`
        // read f g a as quavers in LilyPond and the page drew f g as crotchets.)
        string spanValue = _lastWrittenValue;
        int spanDots = _lastWrittenDots;

        var bodies = new List<string>();
        foreach (var (_, block) in par.NamedVoices)
        {
            var buf = OpenNested();
            buf._lysStep = spanStep;
            buf._lysOctave = spanOctave;
            buf._lyStep = chainStep;
            buf._lyOctave = chainOctave;
            buf._lastWrittenValue = spanValue;
            buf._lastWrittenDots = spanDots;
            buf._forceNextDuration = true;
            buf.EmitMusicStream(MusicItems(block).ToList(), "");
            chainStep = buf._lyStep;
            chainOctave = buf._lyOctave;
            _frameTracked &= buf._frameTracked;
            string body = NestedText(buf);
            if (body.Length > 0)
                bodies.Add(body);
        }

        _lysStep = spanStep;
        _lysOctave = spanOctave;
        _lyStep = chainStep;
        _lyOctave = chainOctave;
        _lastWrittenValue = spanValue;
        _lastWrittenDots = spanDots;
        _forceNextDuration = true;

        if (bodies.Count == 0)
            return "";
        if (bodies.Count == 1)
            return bodies[0];
        // ⚠️ In a part only a combinedStaff plays, the blocks are ONE Voice's simultaneous music
        // on the page (PartCombiner.ChooseSilenceWithinPart), and LilyPond spells that without
        // the separator — `\\` would voicify them inside \partCombine (_combinedPart).
        if (_combinedPart)
            return "<< { " + string.Join(" } { ", bodies) + " } >>";
        return "<< { " + string.Join(" } \\\\ { ", bodies) + " } >>";
    }

    private string EmitGrace(GraceExpressionSyntax g)
    {
        string kw = g.IsAcciaccatura ? "\\acciaccatura" : g.IsAppoggiatura ? "\\appoggiatura" : "\\grace";
        var buf = OpenNested();
        // The grace body has its OWN default duration, an eighth, and it is Lily#'s own rule
        // (MeasureCollector.CollectGraceNotes graceDefaultDuration = Fraction.Eighth;
        // LilyPond has no grace-specific default and would take whatever the main stream last
        // wrote). So the body's first event writes its value out unless it states one, and the
        // events after it inherit — which is the same carry on both sides from there on.
        buf._lastWrittenValue = "8";
        buf._lastWrittenDots = 0;
        buf._forceNextDuration = true;
        buf.EmitMusicStream(MusicItems(g.Body).ToList(), "");
        // ⚠️ The OCTAVE frame does NOT leak the way the duration does: the grace body advances
        // it on BOTH sides, so it carries out like a tuplet's. MeasureCollector.CollectGraceNotes
        // writes _octave.CurrentOctave per grace note and never restores it (the save/restore
        // OctaveContext.Snapshot mentions is the parallel span's, not this). Measured, because
        // the comment here used to claim the opposite: `a4 grace { e8 } c4` renders A3 E3 C3,
        // and its twin reads A3 E3 C3 in LilyPond.
        string streamValue = _lastWrittenValue;
        int streamDots = _lastWrittenDots;
        CarryFrameBack(buf);
        string body = NestedText(buf);
        // LilyPond carries the grace body's last duration out to the next event; Lily# does
        // not — the stream's own memory stands, and the next event writes it out. See
        // EmitEventDuration.
        _lastWrittenValue = streamValue;
        _lastWrittenDots = streamDots;
        _forceNextDuration = true;
        return $"{kw} {{ {body} }}";
    }

    // The clef the twin is reading in — what a slash note's middle-line pitch is spelled against.
    private ClefType _lysClef = ClefType.Treble;

    /// <summary>A mid-music <c>clef</c>: written across unchanged. It moves no octave frame on
    /// either side — a Lily# clef is drawing only, and LilyPond's <c>\relative</c> never looks
    /// at a clef (InstrumentDefaults.DefaultAnchorOctave).</summary>
    private string EmitClef(ClefDeclarationSyntax cl)
    {
        _lysClef = Svg.Collector.MeasureCollector.ParseClefType(cl.ClefName.Text.ToLowerInvariant());
        return "\\clef " + LyClefName(cl.ClefName.Text);
    }

    /// <summary>
    /// Emits a cue region as LilyPond's own <c>\new CueVoice { … }</c>, with
    /// <c>\cueClef</c> / <c>\cueClefUnset</c> around it when the region names a clef.
    /// </summary>
    /// <remarks>
    /// This is the 1:1 that made a cue twin possible at all: LilyPond has no per-note cue,
    /// so `lysc ly` used to drop `@cue` and emit a book with no cue in it. The region maps
    /// straight across with nothing to infer. ⚠️ BOTH clefs are written — MEASURED
    /// (audit/lp-geometry/probes/cue-span.ly, book D-NOUNSET) LilyPond leaks the cue clef
    /// into the rest of the staff without the unset.
    /// LILYPOND-REF: ly/engraver-init.ly CueVoice; ly/music-functions-init.ly cueClef /
    ///   cueClefUnset.
    /// </remarks>
    private string EmitCue(CueExpressionSyntax cue)
    {
        var buf = OpenNested();
        // A cue clef is drawing only: neither side's relative frame moves
        // (InstrumentDefaults.DefaultAnchorOctave), so the body is written with its own marks.
        buf.EmitMusicStream(MusicItems(cue.Body).ToList(), "");
        // The body is written once and read once by the relative pass on both sides, so its
        // frame carries out like a tuplet's or a repeat's.
        CarryFrameBack(buf);
        string body = NestedText(buf);
        string region = $"\\new CueVoice {{ {body} }}";
        return cue.ClefKeyword is { } clef
            ? $"\\cueClef {LyClefName(clef.Text)} {region} \\cueClefUnset"
            : region;
    }

    /// <summary>
    /// A clef name as LilyPond's <c>\clef</c> / <c>\cueClef</c> take it: a QUOTED string.
    /// </summary>
    /// <remarks>
    /// ⚠️ The quotes are not decoration. MEASURED on LilyPond 2.26.0, 2026-08-15: written
    /// bare, <c>\clef treble_8</c> is read as <c>\clef treble</c> followed by <c>_8</c> — a
    /// fingering — so LilyPond reports "Unattached FingeringEvent", engraves an ORDINARY
    /// treble clef and prints a stray glyph under the staff. The three books differ:
    /// bare 5643 bytes, quoted 6442 (the real octave-down clef), plain treble 5161. The twin
    /// was writing the bare form at all four sites, so the 6 tracked books that use
    /// <c>treble_8</c> had twins that engraved a DIFFERENT CLEF — the exact "compile to other
    /// music" failure the twin exists to rule out, and it was invisible because the four
    /// clef names that are purely alphabetic do lex correctly bare.
    /// <para>
    /// The octave modifier is part of the NAME, not a separate token: make-clef-set matches
    /// the whole string against <c>^(.*)([_^])([^0-9a-zA-Z]*)([1-9][0-9]*)([^0-9a-zA-Z]*)$</c>
    /// and splits it itself. So the name has to REACH it in one piece — written bare,
    /// LilyPond's reader has already split it, and make-clef-set is handed "treble".
    /// </para>
    /// <para>
    /// Quoting unconditionally rather than only when the name has an underscore: quoted is
    /// LilyPond's documented spelling and is correct for every name (MEASURED — treble, bass,
    /// alto, tenor and treble_8 all pass, in both \clef and \cueClef), so nothing here has to
    /// reason about LilyPond's reader. ONE home for the rule, because four spellings of it is
    /// how this survived in three of them.
    /// </para>
    /// LILYPOND-REF: scm/parser-clef.scm:178-190 make-clef-set — takes clef-name as a string
    ///   and parses the octave modifier out of it.
    /// LILYPOND-REF: ly/music-functions-init.ly:535-538 make-cue-clef-set — cueClef declares
    ///   its argument (type) (string?) and hands it straight to it.
    /// </remarks>
    private static string LyClefName(string name) => "\"" + name + "\"";

    private string EmitRepeat(RepeatExpressionSyntax rep)
    {
        string type = rep.RepeatType.Text;
        string count = rep.Count.Text;
        var buf = OpenNested();
        buf.EmitMusicStream(MusicItems(rep.Body).ToList(), "");
        // The body is WRITTEN once and read once by the relative pass on both sides, however
        // many times it is played, so its frame carries out like a tuplet's.
        CarryFrameBack(buf);
        string body = NestedText(buf);
        return $"\\repeat {type} {count} {{ {body} }}";
    }

    private string Skip(SyntaxNode item)
    {
        _warnings.Add($"{item.Kind} not exported");
        return "";
    }

    // ---- The chord list (layout { chordList true }, HANDOFF §2 K5 ⑤) -----------------------
}
