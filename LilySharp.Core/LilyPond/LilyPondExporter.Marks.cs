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

// Attachments: articulations and post-events (SplitAttachments, MapArticulation), the @mark
// families (EmitMark: fingering, pedal, free text, diagrams, text spans, stroke fingers),
// navigation marks. Split out of LilyPondExporter.cs as a partial class (2026-10-02); same
// instance state, no behavior change.
public sealed partial class LilyPondExporter
{
    // A note's attachments split into those that must precede the note (a
    // rehearsal \mark, a \deadNote prefix, a forced stem direction) and those
    // that trail it (string numbers, ties, dynamics, articulation scripts).
    private (string Prefix, string Suffix) SplitAttachments(IEnumerable<SyntaxNode> arts)
    {
        var prefix = new StringBuilder();
        var suffix = new StringBuilder();
        foreach (var a in arts)
        {
            switch (a)
            {
                // ⚠️ THE ONE MARK THAT TRAILS ITS NOTE. `\mark` is standalone music written
                // BEFORE the note it stands over, which is why marks default to the prefix;
                // `\nonArpeggiato` is a POST-EVENT (scm/define-music-types.scm:436-441 gives
                // its syntax as `note-\nonArpeggiato`), so written in the prefix it would be
                // an unattached post-event that LilyPond drops with a warning — a twin that
                // COMPILES but engraves different music, which is the one failure mode a twin
                // generator must not have.
                case MusicMarkSyntax mk when NonArpeggiato(mk) is { } na:
                    suffix.Append(na);
                    break;
                // …and the second one. `-2` is a post-event exactly like \nonArpeggiato, for
                // the same reason and with the same consequence if it were written first.
                case MusicMarkSyntax mk when Fingering(mk) is { } fg:
                    suffix.Append(fg);
                    break;
                // A FEATHERED BEAM is a property of the Beam grob, not a post-event, so it is
                // written before the note that opens the beam — the grob is created at that
                // moment and takes the `\once` value. The same shape as the stem-direction
                // override four cases down, and the same reason it is `\once`: it belongs to
                // THIS beam and must not leak into the next one.
                // LILYPOND-REF: beam.cc:1200-1201 calc_stem_positions — the Beam grob reads
                //   grow-direction off itself, which is why overriding it at the beam's first
                //   moment is what reaches it;
                // LILYPOND-REF: beam.cc:1134-1145 calc_stem_y — the fan the property produces,
                //   ported to the page in SharedRenderer.Beams.
                // ⚠️ `\featherDurations` is DELIBERATELY NOT written: it scales the printed
                // durations, which Lily# does not do — `@feather` fans the drawing and leaves
                // the rhythm alone, so writing it would make the twin play music the page
                // does not.
                // A NOTEHEAD STYLE is a property of the NoteHead grob, set before the note like
                // the feathered beam below — `\once`, so it reaches every head of a chord at
                // this moment and nothing after. The page's glyphs are LilyPond's own
                // (MusicGlyphs.Notehead: noteheads.s2cross, s2diamond, …). Until
                // 2026-10-02 every @notehead was "dropped (out of scope)".
                // LILYPOND-REF: lily/note-head.cc internal_print — glyph "noteheads.s" +
                //   min(duration-log, 2) + the style symbol.
                case MusicMarkSyntax nh when Semantics.AnnotationValues.Notehead(nh) is { } headStyle:
                    prefix.Append("\\once \\override NoteHead.style = #'")
                          .Append(headStyle == "x" ? "cross" : headStyle)
                          .Append(' ');
                    break;
                case MusicMarkSyntax fm when Semantics.AnnotationValues.Feather(fm) is not 0 and var dir:
                    prefix.Append("\\once \\override Beam.grow-direction = #")
                          .Append(dir > 0 ? "RIGHT" : "LEFT")
                          .Append(' ');
                    break;
                // '@!phrasingSlur' is a POST-event (`\)`), not a mark written before the note:
                // before it, it would end the phrasing slur on the PREVIOUS note.
                case MusicMarkSyntax { IsSpanEnd: true } pe
                    when Semantics.AnnotationValues.IsPhrasingSlurName(pe.Name):
                    suffix.Append("\\)");
                    break;
                // A pedal is a SPAN EVENT on its note — a post-event, so it trails the note
                // as \sustainOn does. Until 2026-09-23 every pedal was "dropped (out of
                // scope)" and the twin of a piano book had no pedal line at all.
                case MusicMarkSyntax mk when PedalPostEvent(mk) is { } pedal:
                    suffix.Append(pedal);
                    break;
                // Free expressive text (`@text("dolce")`) is a TEXT SCRIPT on its note — a
                // post-event, so it trails the note, the written side as the event's direction
                // and the neutral `-` otherwise (TextScript's own default is DOWN, the page's
                // too). Until 2026-09-23 every one was "dropped (out of scope)"; see FreeText
                // for why it is not written as a dynamic.
                case MusicMarkSyntax mk when FreeText(mk) is { } freeText:
                    suffix.Append(freeText);
                    break;
                case MusicMarkSyntax mk when FretDiagram(mk) is { } fretDiagram:
                    suffix.Append(fretDiagram);
                    break;
                // A right-hand finger (`@pluck(p)`) is LilyPond's \rightHandFinger, an event
                // function that makes a post-event, so it trails the note; the side is the
                // staff's strokeFingerOrientations (StrokeFingerSet), not a sign on the event.
                case MusicMarkSyntax mk when StrokeFinger(mk) is { } strokeFinger:
                    suffix.Append(strokeFinger);
                    break;
                // The text spanner: the word it prints is a property of the grob, written
                // BEFORE the note that opens it, and the span events trail their notes.
                case MusicMarkSyntax mk when TextSpanEvents(mk) is { } ts:
                    if (ts.Prefix is { } tsPrefix) prefix.Append(tsPrefix).Append(' ');
                    suffix.Append(ts.Suffix);
                    break;
                // An ottava is standalone music BEFORE the note it starts (or stops) on — the
                // argument forms (`@ottava(bassa)`) and the terminator (`@!ottava`) here, the
                // bare word (`@ottava`) three cases down. Until 2026-09-23 all of them were
                // "dropped (out of scope)" and the twin drew every 8va at written pitch.
                case MusicMarkSyntax mk when OttavaCommand(mk.MarkName, mk.IsSpanEnd) is { } ott:
                    prefix.Append(ott).Append(' ');
                    break;
                // An @chord that chooses a diagram: the diagram is the note's markup, and the
                // name goes wherever EmitMark sends an @chord (the part's ChordNames stream).
                case MusicMarkSyntax mk when ChordDiagramMarkup(mk) is { } chordDiagram:
                    suffix.Append(chordDiagram);
                    string cm = EmitMark(mk);
                    if (cm.Length > 0) prefix.Append(cm).Append(' ');
                    break;
                case MusicMarkSyntax mk:
                    string m = EmitMark(mk);
                    if (m.Length > 0) prefix.Append(m).Append(' ');
                    break;
                // The same spanner opened by a WORD (`@rit`, `@accel`, `@rall`, a bare
                // `@textSpan`), which the parser hands over as an articulation.
                case ArticulationSyntax art when TextSpanEvents(art.NameToken.Text) is { } tsw:
                    if (tsw.Prefix is { } tswPrefix) prefix.Append(tswPrefix).Append(' ');
                    suffix.Append(tsw.Suffix);
                    break;
                case ArticulationSyntax art when OttavaCommand(art.NameToken.Text, spanEnd: false) is { } ottw:
                    prefix.Append(ottw).Append(' ');
                    break;
                // `@breath` / `@caesura`: NOT post-events. LilyPond's \breathe and \caesura
                // are music functions standing in the stream after the note (the syntax
                // scm/define-music-types.scm gives is `note \breathe`), so they trail
                // everything attached to the note — the suffix AND the post-events the parser
                // hands over as the note's siblings (`~`, `(`, `[`), which is why they are
                // held back for EmitMusicStream (_trailingMusic) rather than appended here.
                // LILYPOND-REF: ly/music-functions-init.ly:421-424 breathe = define-music-function
                //   → make-music 'BreathingEvent; :432-435 caesura, the same define-music-function
                //   shape → make-music 'CaesuraEvent.
                // LILYPOND-REF: scm/define-music-types.scm BreathingEvent (lines 145-150) — types
                //   (event breathing-event), no post-event: it cannot be written `c4-\breathe`.
                case ArticulationSyntax art when BreathingSign(art) is { } sign:
                    if (_trailingMusic.Length > 0) _trailingMusic.Append(' ');
                    _trailingMusic.Append(sign);
                    break;
                // `@courtesy` / `@editorial` are spelt ON THE PITCH (`cis?4`, `cis!4`) — see
                // AccidentalMarks, read by the note and chord-member emitters — so nothing is
                // written here for the courtesy, and the editorial's suggestion switch is set
                // before the note. `\once` is one timestep, which is the note's.
                // LILYPOND-REF: lily/accidental-engraver.cc:262-267 create_accidental — with
                //   suggestAccidentals true the accidental is make_suggested_accidental, an
                //   AccidentalSuggestion above the head instead of an Accidental at its left.
                case ArticulationSyntax art when AccidentalMark(art) is { } acc:
                    if (acc == '!') prefix.Append("\\once \\set suggestAccidentals = ##t ");
                    break;
                case ArticulationSyntax art when IsDeadNote(art):
                    prefix.Append("\\deadNote ");
                    break;
                // `a4@rest` is LilyPond's own `a4\rest` — a post-event, so it goes in the
                // suffix right where the reader expects it. Without this the twin wrote
                // the NOTE, which is not the same music: LilyPond would engrave a head
                // where the book prints a rest.
                // LILYPOND-REF: ly/music-functions-init.ly — \rest as a post-event.
                case ArticulationSyntax art when IsPitchedRest(art):
                    suffix.Append("\\rest");
                    break;
                case ArticulationSyntax art when StemDirectionOverride(art) is { } up:
                    prefix.Append(up ? "\\once \\override Stem.direction = #UP "
                                     : "\\once \\override Stem.direction = #DOWN ");
                    break;
                default:
                    suffix.Append(EmitAttachment(a));
                    break;
            }
        }
        return (prefix.ToString(), suffix.ToString());
    }

    private static bool IsDeadNote(ArticulationSyntax a)
        => a.NameToken.Text.Equals("dead", StringComparison.Ordinal);

    /// <summary>
    /// An ottava annotation as LilyPond's <c>\ottava #n</c>, or null for any other name:
    /// <c>@ottava</c> → <c>#1</c>, <c>@ottava(bassa)</c> → <c>#-1</c>, <c>@quindicesima</c>
    /// → <c>#2</c>, its bassa → <c>#-2</c>, and any terminator of the family
    /// (<c>@!ottava</c>, <c>@!quindicesima</c>) → <c>#0</c>. The names are read by the same
    /// table the page reads (<see cref="Svg.Model.MusicMarkItem.ParseMarkName"/>).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/music-functions-init.ly:1342-1349 ottava = define-music-function
    ///   (octave) → make-music 'OttavaEvent 'ottava-number octave — positive n is n octaves
    ///   up, negative down, 0 no octavation; standalone music, written BEFORE the note.
    /// LILYPOND-REF: lily/ottava-engraver.cc:81-88 listen_ottava — middleCOffset = −7 × n,
    ///   the display transposition Lily# applies in OttavaTransposer; :123-136 process_music
    ///   — ANY ottava event finishes the open bracket, and a non-zero one starts the next,
    ///   which is the pairing OttavaBracketEngraver.PairOttavaBrackets ports.
    /// ⚠️ WHERE THE BRACKET ENDS is one column apart: Lily#'s bracket runs to the END OF THE
    /// MEASURE before the terminator's (OttavaBracketEngraver.BracketFrom, whole measures);
    /// LilyPond's ends at the last note column before <c>\ottava #0</c>. Same notes under
    /// the bracket in both; the twin's hook sits at that note where the page's sits at the
    /// bar line.
    /// </remarks>
    private static string? OttavaCommand(string name, bool spanEnd)
        => Svg.Model.MusicMarkItem.ParseMarkName(name) switch
        {
            Svg.Model.MusicMarkType.OttavaUp => spanEnd ? "\\ottava #0" : "\\ottava #1",
            Svg.Model.MusicMarkType.OttavaDown => spanEnd ? "\\ottava #0" : "\\ottava #-1",
            Svg.Model.MusicMarkType.QuindicesUp => spanEnd ? "\\ottava #0" : "\\ottava #2",
            Svg.Model.MusicMarkType.QuindicesDown => spanEnd ? "\\ottava #0" : "\\ottava #-2",
            _ => null,
        };

    /// <summary><c>@breath</c> → <c>\breathe</c>, <c>@caesura</c> → <c>\caesura</c>, else
    /// null. Standalone music after the note; see the SplitAttachments case.</summary>
    private static string? BreathingSign(ArticulationSyntax a)
        => a.Type switch
        {
            ArticulationType.Breath => "\\breathe",
            ArticulationType.Caesura => "\\caesura",
            _ => null,
        };

    /// <summary>
    /// The character LilyPond writes after the pitch for <c>@courtesy</c> (<c>?</c>) or
    /// <c>@editorial</c> (<c>!</c>), or null for any other articulation.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/parser.yy:3718 pitch_or_music — `pitch exclamations questions …`;
    ///   :3767-3770 set_property — `?` sets cautionary AND force-accidental, `!` only the latter.
    /// LILYPOND-REF: lily/accidental-engraver.cc:233-236 process_acknowledged — a forced
    ///   accidental is printed when the rules would not print one, which is Lily#'s own rule
    ///   for both annotations (MeasureCollector.ItemFactory: KeySignatureAccidentalName when
    ///   nothing would print);
    ///   :293-296 make_standard_accidental — cautionary makes an AccidentalCautionary, the
    ///   parenthesized one the page draws for @courtesy.
    /// The `!` alone prints nothing new: with suggestAccidentals set before the note (the
    /// SplitAttachments case) the forced accidental is the SUGGESTION above the head.
    /// </remarks>
    private static char? AccidentalMark(ArticulationSyntax a)
        => a.Type == ArticulationType.None
            ? a.NameToken.Text switch
            {
                "courtesy" => '?',
                "editorial" => '!',
                _ => null,
            }
            : null;

    /// <summary>Every accidental mark among <paramref name="arts"/>, in LilyPond's order
    /// (<c>!</c> before <c>?</c>), as the string that follows the pitch.</summary>
    private static string AccidentalMarks(IEnumerable<SyntaxNode> arts)
    {
        bool forced = false, cautionary = false;
        foreach (var a in arts)
            if (a is ArticulationSyntax art && AccidentalMark(art) is { } c)
            {
                if (c == '!') forced = true; else cautionary = true;
            }
        return (forced ? "!" : "") + (cautionary ? "?" : "");
    }

    // One house for the spelling (Semantics.PitchedRest), because there are four readers of
    // it and two of them did not have one: MusicXML exported a sounding note and MIDI played
    // it. This was the only output that got it right.
    // ⚠️ Moving here TIGHTENS this reader — it used to match any articulation spelled "rest"
    // and now requires ArticulationType.None, which is what the collector has always
    // required. That is a move toward agreement rather than a change of behaviour, and it is
    // measured: the twin of all 566 tracked books is byte-identical across it.
    private static bool IsPitchedRest(ArticulationSyntax a) => Semantics.PitchedRest.IsMarker(a);

    /// <summary>
    /// <c>@stemUp</c> / <c>@stemDown</c> as a nullable direction, or null for anything else.
    /// </summary>
    /// <remarks>
    /// Written as <c>\once \override Stem.direction</c> rather than <c>\stemUp</c>, because
    /// Lily#'s annotation belongs to ONE note while LilyPond's command is a context setting
    /// that runs until <c>\stemNeutral</c>. <c>\once</c> is the same scope — one timestep —
    /// and needs no closing command, so a run of annotated and un-annotated notes comes out
    /// note for note.
    /// <para>
    /// LILYPOND-REF: ly/property-init.ly <c>stemUp</c> — <c>\override Stem.direction = #UP</c>;
    /// lily/beam.cc:898-903 Beam::get_default_dir is what then reads it, which is why the
    /// twin needs it at all: without this the beamed books of
    /// audit/lp-geometry/probes/tie-direction.ly would engrave a DOWN beam in LilyPond and
    /// an UP one in Lily#.
    /// </para>
    /// </remarks>
    private static bool? StemDirectionOverride(ArticulationSyntax a)
        => a.NameToken.Text switch
        {
            "stemUp" => true,
            "stemDown" => false,
            _ => null,
        };

    /// <summary><c>@mark("Intro")</c> → LilyPond's boxed rehearsal mark.</summary>
    /// <remarks>
    /// <para>
    /// The label is read from the annotation's argument by
    /// <see cref="Semantics.AnnotationValues.Rehearsal"/>, not sliced out of the dotted
    /// name here — this was the FOURTH copy of that slice, and it stripped its quotes
    /// with <c>Trim('"')</c> where the collector strips one balanced pair
    /// (docs/VALUE_SITE_AUDIT.md §9.5.3 ⑵).
    /// </para>
    /// <para>
    /// ⚠️ <b>A behaviour change, declared</b> — the same shape as <c>@finger("3")</c>
    /// and <c>@diagram(zzz)</c> before it: the twin wrote the label UNQUOTED, and
    /// <c>\box</c> takes ONE markup argument, so a label with a space said different
    /// music than Lily# draws. Measured on LilyPond 2.26.0: <c>\box a b</c> boxes only
    /// <c>a</c> and prints <c>b</c> outside the box (box width 1.9331), while
    /// <c>\box "a b"</c> boxes the whole label (4.0159) — which is what Lily# draws.
    /// Quoting is a no-op for every label a book writes: <c>\box A</c> and
    /// <c>\box "A"</c> render byte-identical SVG, as do <c>\box D.S.</c> and
    /// <c>\box "D.S."</c> (both measured on 2.26.0), and the two <c>@mark(</c> sites in
    /// the corpus and fixtures are <c>@mark("A")</c> and <c>@mark("B")</c>.
    /// </para>
    /// LILYPOND-REF: ly/music-functions-init.ly:1159-1171 mark = define-music-function (label)
    ///   — the label of <c>\mark</c>, which becomes a RehearsalMarkEvent (or an
    ///   AdHocMarkEvent when it is a markup rather than a number).
    /// LILYPOND-REF: scm/define-markup-commands.scm:1049-1053 — define-markup-command
    ///   (box layout props arg) declares <c>(markup?)</c>: ONE markup, which is why an
    ///   unquoted two-word label boxes only its first word.
    /// </remarks>
    private string EmitMark(MusicMarkSyntax mk)
    {
        if (Semantics.AnnotationValues.Rehearsal(mk, out _) is { } label)
            return $"\\mark \\markup {{ \\box \"{Escape(label)}\" }}";
        string name = mk.MarkName;
        if (NonArpeggiato(mk) is { } na)
            return na;
        if (Fingering(mk) is { } fg)
            return fg;
        // An inline @chord rides its part's ChordNames stream (EmitInlineChordTracks), not
        // the music: the note is written bare here, the symbol stands in that context.
        // ⚠️ A phrase's music is written outside any part (_currentPartName is null), so its
        // marks are matched by their own position in that stream: until 2026-10-02 every
        // @chord in a phrase was reported "dropped" while the twin printed it.
        if (mk.Name == "chord" && ((_currentPartName != null && _inlineChordVars.ContainsKey(_currentPartName))
                                   || _shared.InlineChordMarks.Contains(mk.SourceStart)))
            return "";
        // …and so does @figuredBass, in its part's FiguredBass stream (EmitFiguredBassTracks).
        if (mk.Name == "figuredBass" && _shared.FigureMarks.Contains(mk.SourceStart))
            return "";
        // '@!phrasingSlur' — LilyPond's `\)`, the PhrasingSlurEvent STOP.
        if (mk.IsSpanEnd && Semantics.AnnotationValues.IsPhrasingSlurName(mk.Name))
            return "\\)";
        // Spelt as WRITTEN: MarkName steps over the '!' of a terminator, so a bare
        // "@rit dropped" for a '@!rit' would name a mark the reader did not write.
        _warnings.Add($"@{(mk.IsSpanEnd ? "!" : "")}{name} dropped (out of scope)");
        return "";
    }

    /// <summary>
    /// <c>@arpeggio(bracket)</c> as LilyPond's post-event for it, or null for any other mark.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS NOT <c>\arpeggioBracket</c>, and that is the whole reason it can be written
    /// at all. <c>\arpeggioBracket</c> (ly/property-init.ly:99-108) is a pair of OVERRIDES that
    /// change what an ordinary <c>\arpeggio</c> draws, so a twin would need a
    /// <c>\once \override</c> pair in the PREFIX and an <c>\arpeggio</c> in the SUFFIX — and
    /// every other mark here contributes to one side only, which is why this annotation was
    /// dropped as unwritable until 2026-08-03.
    /// <para>
    /// LilyPond's spelling for the THING rather than for the appearance needs no prefix at all:
    /// LILYPOND-REF: ly/property-init.ly:69 — <c>nonArpeggiato = #(make-music
    ///   'NonArpeggiatoEvent)</c>, whose syntax (scm/define-music-types.scm:436-441) is
    ///   <c>note-\nonArpeggiato</c>, a post-event like <c>\arpeggio</c> itself.
    /// LILYPOND-REF: lily/arpeggio-engraver.cc:91-98 <c>listen_non_arpeggiato</c> — that event
    ///   sets <c>Arpeggio_type::NON_ARPEGGIATED</c>, and
    /// LILYPOND-REF: lily/arpeggio-engraver.cc:132-148 <c>process_music</c> — which then makes
    ///   a <b>ChordBracket</b> item, the grob <c>ArpeggioItem.Bracket</c> means.
    ///   <c>\arpeggioBracket</c> instead keeps an <b>Arpeggio</b> grob and re-dresses it.
    /// </para>
    /// <para>
    /// LilyPond's own docstring (ly/property-init.ly:103-104) prefers this one for exactly this
    /// case: "For a bracket designating a non-arpeggiated chord, it is better to use
    /// <c>\nonArpeggiato</c> than to use <c>\arpeggio</c> and alter the appearance."
    /// </para>
    /// </remarks>
    private static string? NonArpeggiato(MusicMarkSyntax mk)
        => Semantics.AnnotationValues.IsArpeggioBracket(mk) ? "\\nonArpeggiato" : null;

    /// <summary>
    /// <c>@finger(2)</c> as LilyPond's fingering post-event <c>-2</c>, or null for any other
    /// mark.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/parser.yy:3461-3467 fingering — an UNSIGNED after a direction sign becomes a FingeringEvent carrying `digit`
    /// <para>
    /// ⚠️ THE GRAMMAR, NOT THE ENGRAVER, and the first version of this citation got it
    /// wrong: it named <c>fingering-engraver.cc</c>, which is what CONSUMES a FingeringEvent.
    /// What this line asserts is how LilyPond SPELLS one, so the address has to be where the
    /// spelling is defined. An exporter's citations point at LilyPond's syntax; an
    /// engraver's point at its arithmetic.
    /// </para>
    /// <para>
    /// ⚠️ WHY THIS EXISTS AT ALL, AND IT IS THE FAILURE MODE THE CLASS REMARK NAMES: until
    /// 2026-08-05 (session 96) this fell through to <c>EmitMark</c>'s "out of scope" branch,
    /// so a fixture carrying a fingering exported as a bare note and the twin had NO
    /// Fingering grob — a twin that COMPILES and is DIFFERENT MUSIC. It was caught while
    /// building audit/lp-geometry/probes/notehead-ink-frame.ly, whose FNG book needed the
    /// `-2` inserted by hand; the warning is what caught it, which is why every drop here
    /// raises one.
    /// </para>
    /// ⚠️ A POST-EVENT, so it belongs in the SUFFIX beside <c>\nonArpeggiato</c> — see the
    /// remark in <see cref="SplitAttachments"/> for what putting one in the prefix costs.
    /// ⚠️ The DIRECTION is deliberately left to LilyPond (`-2`, not `^2`/`_2`): Lily#'s own
    /// engraver takes the default orientation too (fingeringOrientations '(up down), so a
    /// lone fingering goes up regardless of stem), and forcing a side here would make the
    /// twin state something the fixture did not.
    /// </remarks>
    private static string? Fingering(MusicMarkSyntax mk)
        // The SAME set the collector reads, so anything Lily# engraves reaches the twin:
        // any non-negative integer.
        // ⚠️ THIS GATE SAID 1-5 FOR ONE COMMIT, and that was a narrowing with nothing behind
        // it — it would have dropped @finger(6) from the twin while Lily# drew it, which is
        // the very defect this method exists to close, just over a smaller range. MEASURED
        // rather than argued (scratch probe on 2.26.0, dumping the Fingering grob's `text`):
        // LilyPond engraves `-0`, `-5`, `-6` AND `-12` as fingerings reading 0/5/6/12, so
        // its grammar's UNSIGNED really does take them all and there is nothing to protect
        // against here.
        // ⚠️ "The SAME set" was an ASPIRATION until 2026-08-15: this read used to slice the
        // dotted MarkName and Trim('"') it, so it alone accepted `@finger("3")` and emitted
        // a `-3` for a fingering Lily# does not draw. Both now read the argument through
        // Semantics.AnnotationValues.Finger, which is where that set lives.
        => Semantics.AnnotationValues.Finger(mk) is { } finger
            ? "-" + finger.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;

    /// <summary>
    /// A pedal annotation as LilyPond's span event, or null for any other mark:
    /// <c>@sustain</c> / <c>@!sustain</c> → <c>\sustainOn</c> / <c>\sustainOff</c>, the
    /// sostenuto pair likewise, <c>@unaCorda</c> → <c>\unaCorda</c> and its release —
    /// <c>@!unaCorda</c> or the word <c>@treCorde</c> — <c>\treCorde</c>. The names are
    /// read by the same table the page reads (<see cref="Svg.Model.MusicMarkItem.ParseMarkName"/>),
    /// so a spelling the page accepts is one the twin writes.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/spanners-init.ly:94-101 make-span-event 'SustainEvent / 'UnaCordaEvent /
    ///   'SostenutoEvent START|STOP — sustainOn / sustainOff, unaCorda / treCorde,
    ///   sostenutoOn / sostenutoOff, each a post-event on the note.
    /// </remarks>
    // The pedal STARTS written while that pedal is already down (RePedalStarts), by source
    // offset: each is written as a release and a start.
    private HashSet<int> _rePedals = new();

    /// <summary>
    /// Every pedal start (<c>@sustain</c>, <c>@sostenuto</c>, <c>@unaCorda</c>) written while
    /// the same pedal is still down in the same music — the re-pedals the page engraves as
    /// pedal changes — by source offset. One walk in source order per music body (a part
    /// block, else its section or phrase), before any of it is written: the writers ask per
    /// node and some ask more than once, so the answer cannot be a running state.
    /// </summary>
    private static HashSet<int> RePedalStarts(SyntaxNode root)
    {
        var rePedals = new HashSet<int>();
        var down = new Dictionary<SyntaxNode, HashSet<string>>();
        foreach (var node in root.DescendantNodes())
        {
            string? start = null, release = null;
            if (node is ArticulationSyntax art)
            {
                switch (art.NameToken.Text)
                {
                    case "sustain": start = "sustain"; break;
                    case "sostenuto": start = "sostenuto"; break;
                    case "unaCorda": start = "unacorda"; break;
                    case "treCorde": release = "unacorda"; break;
                }
            }
            else if (node is MusicMarkSyntax mark)
            {
                string? pedal = Svg.Model.MusicMarkItem.ParseMarkName(mark.Name) switch
                {
                    Svg.Model.MusicMarkType.SustainOn => "sustain",
                    Svg.Model.MusicMarkType.SostenutoOn => "sostenuto",
                    Svg.Model.MusicMarkType.UnaCordaOn => "unacorda",
                    Svg.Model.MusicMarkType.UnaCordaOff => "unacorda",
                    _ => null,
                };
                if (pedal != null && (mark.IsSpanEnd
                        || Svg.Model.MusicMarkItem.ParseMarkName(mark.Name) == Svg.Model.MusicMarkType.UnaCordaOff))
                    release = pedal;
                else
                    start = pedal;
            }
            if (start == null && release == null)
                continue;
            SyntaxNode body = node;
            for (var p = node.Parent; p != null; p = p.Parent)
                if (p is PartBlockSyntax or SectionDeclarationSyntax or PhraseDeclarationSyntax)
                {
                    body = p;
                    break;
                }
            if (!down.TryGetValue(body, out var pedals))
                down[body] = pedals = new HashSet<string>(StringComparer.Ordinal);
            if (release != null)
                pedals.Remove(release);
            else if (!pedals.Add(start!))
                rePedals.Add(node.SourceStart);
        }
        return rePedals;
    }

    private static string? PedalPostEvent(MusicMarkSyntax mk)
    {
        var type = Svg.Model.MusicMarkItem.ParseMarkName(mk.Name);
        bool end = mk.IsSpanEnd;
        return type switch
        {
            Svg.Model.MusicMarkType.SustainOn => end ? "\\sustainOff" : "\\sustainOn",
            Svg.Model.MusicMarkType.SostenutoOn => end ? "\\sostenutoOff" : "\\sostenutoOn",
            Svg.Model.MusicMarkType.UnaCordaOn => end ? "\\treCorde" : "\\unaCorda",
            Svg.Model.MusicMarkType.UnaCordaOff => "\\treCorde",
            _ => null,
        };
    }

    /// <summary>
    /// <c>@text("dolce")</c> as LilyPond's text script on the note —
    /// <c>-\markup { \italic "dolce" }</c>, <c>^</c> for <c>.up</c>, <c>_</c> for
    /// <c>.down</c> — or null for any other mark, and for a <c>@text</c> that wrote no
    /// quoted string (the page draws nothing for those either, and they keep their warning).
    /// The string is the one the page reads (<see cref="Semantics.AnnotationValues.Text"/>).
    /// </summary>
    /// <remarks>
    /// ⚠️ A TEXT SCRIPT, NOT A DYNAMIC. The page hangs this on the DynamicText line but it is
    /// not a dynamic level: a hairpin runs THROUGH it to the real closing dynamic
    /// (HairpinEngraver skips <see cref="Svg.Model.DynamicItem.IsExpressiveText"/>). LilyPond's
    /// dynamic spelling for a word — <c>#(make-dynamic-script (markup …))</c>, an
    /// AbsoluteDynamicEvent — would END the hairpin on the word: a twin that compiles and is
    /// different music. The TextScriptEvent is what Dynamic_engraver does not listen to, so
    /// the hairpin reaches the same dynamic in both engines (measured on 2.26.0: the
    /// fixture's m2 hairpin runs from 1 to the 7/4 column, the <c>\f</c>, past "poco" at 5/4).
    /// The braces make the markup's end unambiguous whatever post-event follows.
    /// <para>
    /// Self-acknowledged difference that remains: LilyPond places a TextScript by
    /// side-position against the staff (padding 0.3, staff-padding 0.5, outside-staff-priority
    /// 450) where the page rides the dynamic line's placement; both print plain italic.
    /// </para>
    /// LILYPOND-REF: lily/parser.yy:3435-3440 gen_text_def — a full_markup becomes a
    ///   TextScriptEvent carrying <c>text</c>; :3269-3278 post_event_nofinger — script_dir
    ///   direction_reqd_event, the written <c>^</c>/<c>_</c> set as the event's direction.
    /// LILYPOND-REF: scm/define-grobs.scm:3800-3807 TextScript outside-staff-priority 450, direction
    ///   DOWN (the page's default side for @text).
    /// LILYPOND-REF: scm/define-markup-commands.scm:4237-4251 define-markup-command (italic …)
    ///   — font-shape italic, the face the page draws (DynamicEngraver.LabelStyle, expressive).
    /// </remarks>
    private static string? FreeText(MusicMarkSyntax mk)
        => Semantics.AnnotationValues.Text(mk) is { } text
            ? (mk.ForcedAbove switch { true => "^", false => "_", null => "-" })
              + "\\markup { \\italic \"" + Escape(text) + "\" }"
            : null;

    /// <summary>
    /// <c>@diagram(xx0232)</c> as LilyPond's fret-diagram markup, over the note as the page draws
    /// it (<c>_</c> for <c>@diagram(…).down</c>), or null for any other mark. The spec is written
    /// low string first, one character a string — LilyPond's terse string is the same order,
    /// one <c>;</c>-terminated entry a string, <c>o</c> for open. Until 2026-09-26 every one was
    /// "dropped (out of scope)" (Lab probes/complex-lys/06).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: scm/fret-diagrams.scm:1232-1270 fret-diagram-terse — "x;x;o;2;3;2;" is the
    /// D chord; x mute, o open, a number a fret.
    /// <para>
    /// <c>fonts { diagram step ±n }</c> scales the page's whole diagram by 2^(n/6)
    /// (FretFrameGeometry.Scale), and the markup's <c>size</c> is that same factor — the
    /// FretBoard <c>font-size</c> the font block also writes does not reach a markup diagram
    /// (MEASURED, Lab sessions/p646 fr3: the LP page was fr2's to the pixel).
    /// LILYPOND-REF: scm/fret-diagrams.scm make-fret-diagram — size scales the grid.
    /// </para>
    /// </remarks>
    private string? FretDiagram(MusicMarkSyntax mk)
        => Semantics.AnnotationValues.Frame(mk) is { } spec
            ? FretDiagramMarkup(spec, above: mk.ForcedAbove != false)
            : null;

    /// <summary>
    /// The chord diagram an <c>@chord</c> draws (owner's decisions 2026-09-28) — the shape the
    /// page draws: the one WRITTEN for the tuning the score's layout names, else the part's
    /// fretted instrument's, else the guitar's (<see cref="Semantics.ChordAnnotation.Drawn"/>) —
    /// as the same fret-diagram markup <c>@diagram</c> writes, over the note; null for a name
    /// with no shape for that tuning, and under <c>chordDiagrams none</c> (the page draws none
    /// then either).
    /// </summary>
    /// <remarks>
    /// ★ THE NAME STAYS ABOVE IT for the reason the page's does: the name rides the part's
    /// ChordNames context (<see cref="EmitInlineChordTracks"/>), a line of its own standing over
    /// the staff, and the markup is a TextScript of the staff — so LilyPond stacks the diagram
    /// between the staff and the name, the owner's ChordNames-over-FretBoards picture.
    /// <para>
    /// In a <c>chordDiagrams … all</c> score every chord name draws — its written shape, else the
    /// default of its chord — and the chord of a NAME-LESS form (a bare <c>@chord</c>, named from
    /// its notes; a symbol-less shape, from its frets) is the page's: read off the page model's
    /// inline symbol at the mark (<see cref="PageChordAt"/>), as the twin's inline ChordNames
    /// stream already is, so the two cannot name it differently.
    /// </para>
    /// </remarks>
    private string? ChordDiagramMarkup(MusicMarkSyntax mk)
    {
        // A name alone draws under `all`, and where the layout's shape table lists the chord
        // (per the section the mark is written in, ChordShapeTable; 2026-09-29).
        var table = _layoutPlan.ChordDiagramTable;
        bool namesAlone = _layoutPlan.ChordDiagramsAll || table != null;
        return _layoutPlan.ChordDiagramTuningFor(_currentPartName is { } part && _shared.Root is { } root
                ? Semantics.ChordDiagramScores.FrettedWordOfPart(root, part) is { } w ? Tablature.Tunings.Parse(w) : null
                : null) is { } tuning
           && Semantics.ChordAnnotation.Of(mk) is { } words
           && (!words.IsBare || namesAlone)
           && words.Drawn(tuning, _layoutPlan.ChordDiagramsAll,
                   words.Symbol == null && namesAlone ? PageChordAt(mk) : null,
                   table, table != null ? Semantics.ChordDiagramScores.SectionNameOf(mk) : null,
                   _layoutPlan.Chords.Capo) is { } chosen
            ? FretDiagramMarkup(chosen.FrameSpec, above: true)
            : null;
    }

    /// <summary>The chord the page named at an inline <c>@chord</c> (its model's symbol at the
    /// mark's source position), or null when the page was not collected or named none.</summary>
    private Music.ChordStructure? PageChordAt(MusicMarkSyntax mk)
        => _page?.ChordNames.FirstOrDefault(c => !c.UseTiming && c.SourcePosition == mk.SourceStart)?.Structure;

    private string FretDiagramMarkup(string spec, bool above)
    {
        // One entry a string, low string first: x, o, or the fret — read by the page's own
        // reader, so a chosen voicing's frets 10–15 are written as numbers; a predefined
        // shape's barre as its parentheses. No fingers: a markup diagram's finger-code is none
        // and the page draws none on it either.
        string terse = TerseOf(spec);
        string size = _fontPlan.WrittenStep(Rendering.TextRole.FretFrame) is { } step && step != 0
            ? "\\override #'(size . "
              + Math.Pow(2, step / 6.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ") "
            : "";
        // The page stands diagrams side by side and widens the bar to fit them
        // (SpacingRules.ApplyFretFrameSpacing) — LilyPond's \textLengthOn, tweaked onto the
        // diagram alone so an ordinary ^"text" keeps overhanging as it does on the page.
        // LILYPOND-REF: ly/property-init.ly textLengthOn.
        return "-\\tweak extra-spacing-width #'(-0.0 . 0.4) -\\tweak extra-spacing-height #'(-inf.0 . +inf.0) "
            + (above ? "^" : "_")
            + "\\markup " + size + "\\fret-diagram-terse \"" + terse + "\"";
    }

    /// <summary>
    /// <c>@pluck(p|i|m|a)</c> as LilyPond's right-hand fingering <c>\rightHandFinger #1..#4</c>,
    /// or null for any other mark. The letter is the one the page reads
    /// (<see cref="Semantics.AnnotationValues.Pluck"/>); the digit is LilyPond's index into the
    /// StrokeFinger grob's own <c>digit-names</c> ("p" "i" "m" "a" "x"), so the twin prints the
    /// letter the page prints. Until 2026-09-23 every one was "dropped (out of scope)".
    /// </summary>
    /// <remarks>
    /// The SIDE is not on the event: LilyPond positions stroke fingers by the staff's
    /// <c>strokeFingerOrientations</c>, which <see cref="StrokeFingerSet"/> sets to <c>'(down)</c>
    /// for a plucking part (the default <c>'(right)</c> is beside the head, which the page never
    /// draws; the page's stem-opposite side is written there). Self-acknowledged difference
    /// that remains: LilyPond's StrokeFinger is italic at font-size -4, the page's letter is
    /// its own TabTechnique face and em.
    /// LILYPOND-REF: ly/music-functions-init.ly:2154-2161 rightHandFinger = define-event-function
    ///   (finger) → make-music 'StrokeFingerEvent 'stroke-finger-digit, a post-event.
    /// LILYPOND-REF: scm/define-grobs.scm:3544-3568 StrokeFinger (stroke-finger-interface) —
    ///   digit-names #("p" "i" "m" "a" "x"), font-shape italic, font-size -4.
    /// LILYPOND-REF: scm/output-lib.scm:1440-1448 stroke-finger::calc-text — the event's stroke-finger-digit
    ///   indexes digit-names (a stroke-finger-text wins when given).
    /// </remarks>
    private static string? StrokeFinger(MusicMarkSyntax mk)
        => Semantics.AnnotationValues.Pluck(mk) switch
        {
            "p" => "\\rightHandFinger #1",
            "i" => "\\rightHandFinger #2",
            "m" => "\\rightHandFinger #3",
            "a" => "\\rightHandFinger #4",
            _ => null,
        };

    /// <summary>
    /// A text-spanner annotation as LilyPond writes one, or null for any other mark. The
    /// START (<c>@rit</c>, <c>@accel</c>, <c>@rall</c>, <c>@textSpan("…")</c>, bare
    /// <c>@textSpan</c>) is <c>\startTextSpan</c> on the note, preceded — when the spanner
    /// prints a word — by a <c>\once \override</c> of the grob's left bound text, which is
    /// what LilyPond's own <c>\startTextSpan</c> leaves for the writer to set; the
    /// terminator (<c>@!rit</c>, <c>@!textSpan</c>, …) is <c>\stopTextSpan</c>. The word is
    /// the page's own (<see cref="Semantics.AnnotationValues.TextSpan"/> for the argument,
    /// <see cref="Svg.Model.MusicMarkItem.TextSpanSugarText"/> for the three sugar words).
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: ly/spanners-init.ly:44-45 startTextSpan / stopTextSpan = make-span-event
    ///   'TextSpanEvent START / STOP;
    /// LILYPOND-REF: scm/define-grobs.scm TextSpanner — bound-details.left.text is the
    ///   printed word and font-shape is italic, which is the face the page draws, so the
    ///   word goes in as a plain string (no \upright).
    /// ⚠️ <c>\once</c> is sound for a spanner: a grob copies the context's property alist
    ///   when it is created, and Text_spanner_engraver creates the TextSpanner at the
    ///   start note's own timestep (lily/text-spanner-engraver.cc process_music), the one
    ///   timestep the \once covers.
    /// </remarks>
    private static (string? Prefix, string Suffix)? TextSpanEvents(MusicMarkSyntax mk)
    {
        if (Svg.Model.MusicMarkItem.ParseMarkName(mk.Name) != Svg.Model.MusicMarkType.TextSpanStart)
            return null;
        if (mk.IsSpanEnd)
            return (null, "\\stopTextSpan");
        return TextSpanStart(Semantics.AnnotationValues.TextSpan(mk)
            ?? Svg.Model.MusicMarkItem.TextSpanSugarText(mk.Name));
    }

    /// <summary>The START by its bare word — an articulation's name — or null when the word
    /// opens no text spanner.</summary>
    private static (string? Prefix, string Suffix)? TextSpanEvents(string word)
        => Svg.Model.MusicMarkItem.ParseMarkName(word) == Svg.Model.MusicMarkType.TextSpanStart
            ? TextSpanStart(Svg.Model.MusicMarkItem.TextSpanSugarText(word))
            : null;

    private static (string? Prefix, string Suffix) TextSpanStart(string? printed)
        => (printed is null
                ? null
                : "\\once \\override TextSpanner.bound-details.left.text = \"" + Escape(printed) + "\"",
            "\\startTextSpan");

    private string EmitAttachment(SyntaxNode a) => a switch
    {
        StringNumberAnnotationSyntax sn => sn.StringNumberToken.Text, // "\4" — LilyPond-valid
        TieSyntax => "~",
        SlurSyntax s => s.IsOpen ? "(" : ")",
        BeamMarkerSyntax bm => bm.IsStart ? "[" : "]",
        DynamicSyntax d => EmitDynamic(d),
        ArticulationSyntax art => MapArticulation(art),
        MusicMarkSyntax mk => EmitMark(mk),
        _ => "",
    };

    private string MapArticulation(ArticulationSyntax a)
    {
        // Name-based marks whose Type is None (resolved downstream in Lily#). Matched as
        // written: names are case-sensitive (owner's decision 2026-09-27).
        switch (a.NameToken.Text)
        {
            // A fall drops off the note, a doit is the same event with the interval rising;
            // the amount is the page's own (BendAfterGeometry.DeltaStep — the ONE home, so
            // the twin measures what the page draws).
            // LILYPOND-REF: ly/music-functions-init.ly:357-361 bendAfter = define-event-function
            //   (delta) → make-music 'BendAfterEvent 'delta-step delta, a post-event.
            case "fall": return "\\bendAfter #-" + LilySharp.Core.Svg.Layout.BendAfterGeometry.DeltaStep;
            case "doit": return "\\bendAfter #+" + LilySharp.Core.Svg.Layout.BendAfterGeometry.DeltaStep;
            case "dead": return "\\deadNote";      // normally intercepted as a prefix
            // ⚠️ NOT A SCRIPT, so it must answer here and never reach the `dir + glyph` tail
            // below: LilyPond's arpeggio is an EVENT on the chord (`<c e g>1\arpeggio`), and
            // `-\arpeggio` is not the same thing. Lily#'s ChordItem.HasArpeggio is a plain
            // bool with no direction, so there is nothing for a direction prefix to carry.
            // ⚠️ AND `<< … >>` IS A DIFFERENT CONSTRUCT — that is ArpeggioSyntax, a written-out
            // broken chord, and it has its own emitter. This is the stacked chord plus wavy
            // line. Confusing the two is what made the twins agree falsely.
            // LILYPOND-REF: lily/arpeggio-engraver.cc:73-80 Arpeggio_engraver::listen_arpeggio
            //   — an EVENT is listened for, not a script acknowledged, which is why the
            //   spelling is `<c e g>1\arpeggio`. ly/property-init.ly:67 is where the command
            //   itself is `#(make-music 'ArpeggioEvent)`.
            case "arpeggio": return "\\arpeggio";
            // Added 2026-08-05 (session 98). These five answer here and never reach
            // the `dir + glyph` tail below — for an UNFORCED spelling the tail would
            // prepend `-`, asserting a side the fixture never stated (the \arpeggio
            // remark above is the same mistake bought once already). MEASURED before
            // adding, on 2.26.0 (scratch probe, after-line-breaking dump per book):
            // each bare spelling engraves exactly ONE grob of its kind — \glissando a
            // Glissando, \startTrillSpan…\stopTrillSpan ONE TrillSpanner,
            // \laissezVibrer a LaissezVibrerTie, \repeatTie a RepeatTie.
            // LILYPOND-REF: ly/property-init.ly:378 glissando = #(make-music 'GlissandoEvent)
            // LILYPOND-REF: ly/spanners-init.ly:48-49 startTrillSpan / stopTrillSpan
            //   = #(make-span-event 'TrillSpanEvent START/STOP)
            // LILYPOND-REF: ly/declarations-init.ly:103-104 laissezVibrer / repeatTie
            //   = #(make-music 'LaissezVibrerEvent / 'RepeatTieEvent)
            case "glissando": return "\\glissando";
            // The pedals' STARTS. A start is one word with no argument, so it arrives here as
            // an articulation while its terminator (`@!sustain`) is a MusicMarkSyntax that
            // SplitAttachments answers through PedalPostEvent — the same table, both ends.
            // Not scripts: no direction sign, so they answer here and never reach the tail.
            // LILYPOND-REF: ly/spanners-init.ly:94-101 make-span-event — sustainOn, sostenutoOn,
            //   unaCorda, treCorde are span events, post-events on the note.
            // A start while that pedal is already DOWN is a pedal change — the page engraves
            // it as one (SYNTAX_REFERENCE §Pedal Markings, a second `@sustain` re-pedals) — and
            // LilyPond draws the change only for an explicit release first: a bare second
            // \sustainOn continues the bracket with no notch (MEASURED 2026-09-25 on 2.26.0,
            // samples/nocturne.lys: 52 bracket lines with the release, 28 without).
            case "sustain": return _rePedals.Contains(a.SourceStart) ? "\\sustainOff\\sustainOn" : "\\sustainOn";
            case "sostenuto": return _rePedals.Contains(a.SourceStart) ? "\\sostenutoOff\\sostenutoOn" : "\\sostenutoOn";
            case "unaCorda": return _rePedals.Contains(a.SourceStart) ? "\\treCorde\\unaCorda" : "\\unaCorda";
            case "treCorde": return "\\treCorde";
            // A text spanner's START written as a word (`@rit`, a bare `@textSpan`): the span
            // event; the word it prints is set before the note by SplitAttachments
            // (TextSpanEvents), which catches these before they reach here.
            case "rit" or "accel" or "rall" or "textSpan": return "\\startTextSpan";
            // The phrasing slur's start; its end is a terminator (EmitMark). A forced side is
            // the event's direction, as for the half-ties below; unforced stays bare.
            // LILYPOND-REF: ly/declarations-init.ly:87-88 "\\(" / "\\)" = make-span-event
            //   'PhrasingSlurEvent START / STOP.
            case "phrasingSlur":
                return a.ForcedAbove switch { true => "^\\(", false => "_\\(", null => "\\(" };
            case "startTrillSpan": return "\\startTrillSpan";
            case "stopTrillSpan": return "\\stopTrillSpan";
            // The half-tie events DO carry a meaningful written direction — ^/_ is
            // copied onto the tie (laissez-vibrer-engraver.cc:99-103, inherited by
            // Repeat_tie_engraver) and repeat-tie-chords.ly writes `d^\repeatTie` —
            // so a FORCED side must survive into the twin; unforced stays bare
            // (never `-`, same reason as above).
            case "laissezVibrer":
                return a.ForcedAbove switch
                {
                    true => "^\\laissezVibrer",
                    false => "_\\laissezVibrer",
                    null => "\\laissezVibrer",
                };
            case "repeatTie":
                return a.ForcedAbove switch
                {
                    true => "^\\repeatTie",
                    false => "_\\repeatTie",
                    null => "\\repeatTie",
                };
        }

        // The TAB technique letters — `@hammerOn` H, `@pullOff` P, `@tap` T. LilyPond has no
        // grob for them (ArticulationEngraver's remark: a player writes them as markup), so
        // the twin writes what a player writes: a TEXT SCRIPT on the note,
        // `-\markup { \italic "H" }` — the post-event FreeText writes for `@text`, with the
        // letter the page prints (ArticulationEngraver.TabTechniqueLetterOf, the one home).
        // Until 2026-09-23 all three were "not mapped, dropped" (8 warning lines over the
        // fixtures, all in test/tab-technique-letters.lys).
        // ⚠️ THE SIDE IS THE PAGE'S OWN AND CANNOT BE WRITTEN: the page sets a letter opposite
        // the stem (ArticulationEngraver: forceAbove || !stemUp), while a TextScript has ONE
        // default side, DOWN, and a written ^/_ is a forced side. So an unforced letter is
        // `-`, LilyPond's own default, and a stem-down note prints it BELOW where the page
        // prints it above — self-acknowledged, as @text's Y and @pluck's side are. The size
        // too: the page sets the letter at TabTechniqueFontSize (1.5 ss, italic), LilyPond's
        // \italic at the markup's default size. Both engines print it on the TabStaff as well:
        // TabVoice keeps Text_engraver (it removes the fingering, trill and accidental
        // engravers, not the text one) — MEASURED on 2.26.0 (LilySharp-Lab sessions/p546/lp):
        // the fixture's twin engraves 16 TextScripts, 8 on the 5-line Staff and 8 on the
        // 6-line TabStaff, every one direction −1, where the page draws the first bar's
        // three T and the H below (stems up) and the second bar's four above (stems down).
        // LILYPOND-REF: lily/parser.yy:3435-3440 gen_text_def — a full_markup becomes a
        //   TextScriptEvent carrying `text`; :3269-3278 post_event_nofinger — script_dir.
        // LILYPOND-REF: scm/define-grobs.scm:3800-3807 TextScript outside-staff-priority 450,
        //   direction DOWN.
        // LILYPOND-REF: ly/engraver-init.ly:408 Text_engraver — consisted in \Voice.
        // LILYPOND-REF: ly/engraver-init.ly:1172-1190 TabVoice \remove Fingering_engraver,
        //   New_fingering_engraver, Pitched_trill_engraver, Accidental_engraver — and no other.
        if (Svg.Layout.ArticulationEngraver.TabTechniqueLetterOf(a.Type) is { } letter)
            return (a.ForcedAbove switch { true => "^", false => "_", null => "-" })
                   + "\\markup { \\italic \"" + letter + "\" }";

        // Common LilyPond articulations. `@name.up/.down` → -^ / _^ direction.
        string glyph = a.Type switch
        {
            ArticulationType.Staccato => "\\staccato",
            ArticulationType.Staccatissimo => "\\staccatissimo",
            ArticulationType.Accent => "\\accent",
            ArticulationType.Marcato => "\\marcato",
            ArticulationType.Tenuto => "\\tenuto",
            ArticulationType.Fermata => "\\fermata",
            ArticulationType.Trill => "\\trill",
            ArticulationType.Mordent => "\\mordent",
            ArticulationType.Turn => "\\turn",
            ArticulationType.Prall => "\\prall",
            // Added 2026-08-05 (session 96). These four are TRUE SCRIPTS — they take a
            // direction, so they belong in this tail and not in the early switch that
            // \arpeggio needed. MEASURED before adding, on 2.26.0: `-\upbow`, `-\downbow`,
            // `-\flageolet`, `-\portato` and the forced `^`/`_` forms each engrave exactly
            // ONE Script grob, and portato's own default side is DOWN where the other three
            // are UP — which is why the neutral `-` is the right thing to write for an
            // unforced fixture: it lets LilyPond apply its own default instead of the twin
            // asserting a side the fixture never stated.
            // LILYPOND-REF: ly/script-init.ly:28,33,46,79 downbow/flageolet/portato/upbow — each is `name = #(make-articulation 'name)`, i.e. a post-event a direction sign may precede
            ArticulationType.UpBow => "\\upbow",
            ArticulationType.DownBow => "\\downbow",
            ArticulationType.Flageolet => "\\flageolet",
            ArticulationType.Portato => "\\portato",
            // Three more true scripts (added 2026-09-23): the page draws each from the SAME
            // Emmentaler glyph LilyPond's script table names, so the twin's word is the
            // table's — `@reverseTurn` is scripts.reverseturn, `@pralltriller` is
            // scripts.prallprall (ArticulationItem: OrnPrallPrall), `@snapPizz` is
            // scripts.snappizzicato. Until then all three were "not mapped, dropped".
            // LILYPOND-REF: ly/script-init.ly, lines 53, 55 and 60 — prallprall / reverseturn /
            //   snappizzicato, each `name = #(make-articulation 'name)`, a post-event a
            //   direction sign may precede.
            // LILYPOND-REF: scm/script.scm:300-302 default-script-alist (prallprall …
            //   script-stencil feta "prallprall"); :316-318 default-script-alist (reverseturn);
            //   :379-381 default-script-alist (snappizzicato) — the glyph each script draws.
            ArticulationType.InvertedTurn => "\\reverseturn",
            ArticulationType.PrallTriller => "\\prallprall",
            ArticulationType.SnapPizz => "\\snappizzicato",
            _ => "",
        };
        if (glyph.Length == 0)
        {
            _warnings.Add($"articulation @{a.NameToken.Text} not mapped, dropped");
            return "";
        }
        string dir = a.ForcedAbove switch { true => "^", false => "_", null => "-" };
        return dir + glyph;
    }

    // Navigation marks (segno/coda/fine/D.C./D.S. …), each as the grob the page models it on.
    // ⚠️ NOT `\mark`: that is ONE RehearsalMark per moment, and a section boundary is exactly
    // where a label and a navigation mark share one — LilyPond kept the first and discarded
    // the other ("conflict with event: ad-hoc-mark-event"), so `form { A fine B }` lost the
    // label B and `segno A` lost the segno (MEASURED, session 480: Lab sessions/p480/nav-form*).
    // LILYPOND-REF: scm/define-grobs.scm:3083-3114 SegnoMark, :1001-1032 CodaMark (priority
    //   1400, inside RehearsalMark — MusicMarkEngraver.GetOutsideStaffPriority),
    //   :1898-1927 JumpScript (1350, italic, direction DOWN, self-alignment-X RIGHT);
    //   ly/music-functions-init.ly:2178 \segnoMark, :442 \codaMark, :911 \jump.
    // Label 1, not \default: the page draws one sign however many there are, and \default
    // counts (the second \codaMark \default is the doubled 𝄌𝄌).
    // ⚠️ CodaMark is begin-of-line-invisible where SegnoMark is not, so a coda at a break
    // would move to the END of the previous line; the page keeps the sign on the new line
    // (MusicMarkEngraver.CalculateXPosition's LILYSHARP-OWN arm, the owner's choice), and the tweak
    // keeps the twin there. JumpScript's own begin-of-line-invisible is left alone: the page
    // too ends the previous line with a boundary "Fine" / "To Coda" / "D.S." (MEASURED,
    // Lab sessions/p480/nav-break.lys).
    private const string OnTheNewLine = "\\tweak break-visibility #end-of-line-invisible ";

    private string EmitNavMark(NavigationMarkSyntax nav)
    {
        // The glyph marks are music-font stencils on both sides and do not follow the
        // navigation role's plan (the page draws them at the music em).
        switch (nav.MarkType)
        {
            case NavigationMarkType.Segno: return "\\segnoMark 1";
            case NavigationMarkType.Coda: return OnTheNewLine + "\\codaMark 1";
        }
        string? word = nav.MarkType switch
        {
            NavigationMarkType.Fine => "Fine",
            NavigationMarkType.ToCoda => "To Coda",
            NavigationMarkType.DaCapo => "D.C.",
            NavigationMarkType.DaCapoAlFine => "D.C. al Fine",
            NavigationMarkType.DaCapoAlCoda => "D.C. al Coda",
            NavigationMarkType.DalSegno => "D.S.",
            NavigationMarkType.DalSegnoAlFine => "D.S. al Fine",
            NavigationMarkType.DalSegnoAlCoda => "D.S. al Coda",
            _ => null,
        };
        if (word == null)
            return "";
        // The word's own markup carries the plan's step and style for `navigation`
        // (default: italic, JumpScript's font-shape), so a `fonts { navigation bold }`
        // reaches the twin without touching RehearsalMark, whose grob the boxed labels share.
        string body = "\"" + word + "\"";
        string markup = MarkupForRole(Rendering.TextRole.Navigation, body, "\\italic")
                        ?? "\\markup { \\italic " + body + " }";
        // JumpScript hangs BELOW by default, which is where the page puts the jump-FROM
        // instructions (MusicMarkEngraver.IsJumpInstruction); "Fine" and "To Coda" it draws above.
        bool below = nav.MarkType is NavigationMarkType.DaCapo or NavigationMarkType.DaCapoAlFine
            or NavigationMarkType.DaCapoAlCoda or NavigationMarkType.DalSegno
            or NavigationMarkType.DalSegnoAlFine or NavigationMarkType.DalSegnoAlCoda;
        return (below ? "" : "\\tweak direction #UP ") + "\\jump " + markup;
    }
}
