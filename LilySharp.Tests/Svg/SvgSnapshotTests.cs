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

using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Renderer;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests.Svg;

/// <summary>
/// Snapshot regression tests for SVG output.
/// Renders each sample .lys file and compares against a stored baseline SVG.
///
/// To update baselines after intentional changes:
///   set LILYSHARP_UPDATE_SNAPSHOTS=1
///   dotnet test --filter "FullyQualifiedName~SvgSnapshotTests"
/// </summary>
[Trait("Category", "Visual")]
public class SvgSnapshotTests
{
    private static readonly string SamplesDir = FindSamplesDir();
    private static readonly string SnapshotsDir = FindSnapshotsDir();
    private static readonly bool UpdateSnapshots =
        Environment.GetEnvironmentVariable("LILYSHARP_UPDATE_SNAPSHOTS") == "1";

    private static string FindSamplesDir()
    {
        // Snapshot fixtures live UNDER the test project (LilySharp.Tests/Fixtures),
        // deliberately separate from the user-editable samples/ playground so an
        // edit to a free sample never trips a snapshot. sampleName keeps its
        // "test/" and "showcase/" prefixes, which map to Fixtures/test, etc.
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "LilySharp.Tests", "Fixtures");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Cannot find LilySharp.Tests/Fixtures/ directory");
    }

    private static string FindSnapshotsDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "LilySharp.Tests", "Snapshots");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Cannot find LilySharp.Tests/Snapshots/ directory");
    }

    /// <summary>
    /// Test sample files.
    /// </summary>
    public static IEnumerable<object[]> TestSamples()
    {
        yield return new object[] { "test/notes" };
        // Multi-voice collision forces the DOWN voice's on-line augmentation dot
        // BELOW the line (DotConfiguration dir = -1) instead of the default up.
        yield return new object[] { "test/dot-force-down" };
        // Beamed rests are pushed clear of the beam (Beam::rest_collision_callback).
        yield return new object[] { "test/beamed-rest" };
        // Piano pedal styles (part property `pedal`: text and mixed; the default
        // bracket is exercised by test/pedal-note-anchor).
        yield return new object[] { "test/pedal-text" };
        yield return new object[] { "test/pedal-mixed" };
        // A pedal change renders a "/\" notch (bracket-flare) at the shared note.
        yield return new object[] { "test/pedal-change" };
        // Structure navigation marks incl. composite forms (to coda, ds al coda…).
        yield return new object[] { "test/navigation-marks" };
        // Staff-group types: bracket + spanning barlines (staffGroup) and bracket
        // + disconnected barlines (choirStaff).
        yield return new object[] { "test/staff-group" };
        yield return new object[] { "test/choir-staff" };
        // A run past the church-rest expand-limit (10) prints the big_rest H-bar —
        // the only fixture exercising DrawBigRest (bar/serif thickness vs LP).
        yield return new object[] { "test/multi-measure-rest-long" };
        yield return new object[] { "test/part-header-key" };
        yield return new object[] { "test/part-header-key-per-part" };
        yield return new object[] { "test/chords" };
        yield return new object[] { "test/accidentals" };
        // Cue notes/chords scale their accidentals with the head (LP CueVoice
        // fontSize = -4), as a pair with the cue-scaled placement column.
        yield return new object[] { "test/cue-accidentals" };
        // ...and the cue REGION itself. Registered 2026-08-03: until then
        // cue-accidentals was the only cue drawing in the corpus, and this file
        // had gone unparsed for weeks (its own comment says so) precisely
        // because nothing rendered it.
        // ⚠️ THIS COVERS THE FIRST SCORE ONLY. AssertSnapshotMatch calls
        // SvgGenerator.Generate on the whole tree and gets one system back, so a
        // .lys with two `score` blocks is snapshotted up to its first. Measured
        // here: the baseline holds 8 heads — cue-notes' `cue-melody` staff — and
        // none of `cue-chords`, so the cue CHORDS and the `cue bass { … }` clef
        // form are still unwatched. Covering them needs either a score selector
        // on the harness or a second fixture; naming which one this book is
        // buying is the point.
        yield return new object[] { "test/cue-notes" };
        // A whole-measure cue region, alone and as either block of a voice span.
        // The per-voice flatten walks used to walk a span's cue body TWICE (region
        // + flattened full-size copy): an extra bar in one orientation, a silent
        // full-size overlay of the next measure in the other. The fixture header
        // carries the refutation values (6 bars / doubled cue head).
        yield return new object[] { "test/cue-region-measure" };
        // Arpeggio clears a chord-second's LEFT-reversed head (stem-down), not
        // just the un-displaced column.
        yield return new object[] { "test/arpeggio-second" };
        yield return new object[] { "test/articulations" };
        // Multiple articulations on one note stack outward in script-priority
        // order (staccato innermost), independent of written order — the only
        // fixture that exercises ArticulationEngraver's per-note script stacking.
        yield return new object[] { "test/script-stacking" };
        // A fingering + articulation on one note & side: fingering stays inner,
        // articulation is pushed outer (LayoutEngine fingering/articulation pass).
        yield return new object[] { "test/fingering-articulation" };
        // A volta-bracket alternative with a display label: [1. B "label"] shows
        // the label above the bracket (FormAlternative displayLabel slot).
        yield return new object[] { "test/volta-labels" };
        yield return new object[] { "test/dynamics" };
        yield return new object[] { "test/beaming" };
        yield return new object[] { "test/grace-notes" };
        yield return new object[] { "test/ties-slurs" };
        // Accidentals an octave apart straddling the middle line group into distinct
        // octaves (floored division) — they must not overstrike.
        yield return new object[] { "test/accidental-octave-straddle" };
        // Partial secondary beams (beamlets) on isolated 16ths in dotted rhythms.
        yield return new object[] { "test/beamlets" };
        // …and the shape that book does NOT have: an interior stem whose count exceeds BOTH
        // its neighbours', where the beamlets survive on one side only. The ledger's
        // beam.beamlet.* points hold the counts against LilyPond; this holds the drawing.
        yield return new object[] { "test/beamlet-peaks" };
        // Slur lifts to clear high interior notes (encompass obstacles).
        yield return new object[] { "test/slur-obstacle-clearance" };
        // Line breaker prices lyric widths so long syllables wrap instead of overflowing.
        yield return new object[] { "test/lyric-break-pricing" };
        // Hairpins on both staves of a grand staff hang under their own staff.
        yield return new object[] { "test/multi-staff-hairpins" };
        // Text spanners (rit./accel.) per staff on a grand staff.
        yield return new object[] { "test/multi-staff-text-spanners" };
        // Ottava brackets per staff on a grand staff: the lower staff's 8va sits
        // above ITS staff, not piled above the top staff.
        yield return new object[] { "test/multi-staff-ottava" };
        // Slur / tie / glissando in the SECOND voice of a single-staff polyphony
        // — detected per voice and laid out against voice 2's own notes.
        yield return new object[] { "test/multivoice-spanners" };
        // A chord tie in the second voice: the whole chord's ties are forced to
        // the voice's side (lower voice → below), not the single-voice fan-out.
        yield return new object[] { "test/multivoice-chord-tie" };
        // Second-voice beaming: voice 2's eighths/sixteenths beam per voice, BELOW
        // the notes (stems forced down), while voice 1's quarters stay unbeamed.
        yield return new object[] { "test/multivoice-beams" };
        // A tuplet in voice 1 must not break voice 2's beaming: voice 2's eighths beam
        // 4+4. ⚠️ It was a tuplet-boundary rule that made this askable; that rule was
        // an invention and is gone. What is voice-local now is the beamlet clamp at a
        // tuplet's ends, and the grouping answer comes from voice 2's own durations.
        yield return new object[] { "test/multivoice-tuplet-beams" };
        // The same across STAVES: a tuplet above must not reach the bass eighths, which
        // beam 4+4.
        yield return new object[] { "test/multistaff-tuplet-beams" };
        // A tuplet in the LOWER voice: its "3" bracket sits BELOW (the voice's
        // stem side), and its twelfths group by the quarter while the plain eighths
        // around them run to the half measure — two exception lookups in one voice.
        yield return new object[] { "test/multivoice-voice2-tuplet" };
        // Cross-voice beam collision: the upper voice's beam clears a high note
        // held in the lower voice (raised, not cutting across it).
        yield return new object[] { "test/multivoice-beam-collision" };
        // The covered-STEM half of that supply, which no other book reaches: the same
        // beam three times, under a beamed stem, an unbeamed one, and nothing. The
        // ledger holds the three heights against LilyPond (beam.quant.over-stem.*);
        // this holds the drawing, and is the only corpus book with the lift in it.
        yield return new object[] { "test/beam-over-stem" };
        // Crossing voices: the lower (stem-down) voice sits above the upper voice, so the
        // upper voice's heads move RIGHT to clear its stems (meshing shift). The down group
        // keeps the column — it is the LEFTMOST, and note-collision.cc:441 pins that.
        yield return new object[] { "test/multivoice-crossing-collision" };
        // Two voices on ONE column pack their accidentals into ONE accidental column,
        // and those accidentals do not ride the collision shift — the ONLY corpus book
        // that puts an accidental on a shared column at all (measured: every other
        // polyphonic fixture is byte-identical with and without the packing).
        yield return new object[] { "test/cross-voice-accidental" };
        // A tie on a second-interval chord follows its own reversed head's X
        // displacement, not the chord column.
        yield return new object[] { "test/tie-seconds" };
        // Three ties whose FRONT sits low in the staff — the only corpus book in
        // which generate_extremal_tie_variations moves the FRONT tie, the step a
        // bottom-up greedy solve could not take. tie-seconds already watched the
        // pass's back half; deleting the d = -1 half alone moved nothing until
        // this book existed.
        yield return new object[] { "test/tie-triad-extremal" };
        // A phrasing slur can bind two chords (not just single notes).
        yield return new object[] { "test/slur-chords" };
        // Lead-sheet scores: text rows only (lyrics and/or chords, no staff) draw
        // a measure grid — barlines on the top row, content between them.
        yield return new object[] { "test/lead-sheet-lyrics" };
        yield return new object[] { "test/lead-sheet-chords" };
        yield return new object[] { "test/lead-sheet" };
        // A lead sheet keeps the chord source's repeat / double / final barlines.
        yield return new object[] { "test/lead-sheet-repeat" };
        // Tab ties sit on each tied note's STRING line (above the fret digit), not
        // at the notation pitch height — the two ties are on different strings.
        yield return new object[] { "test/tab-tie" };
        // Rests render on a tab staff, centred on the tab's vertical middle.
        yield return new object[] { "test/tab-rest" };
        // A percent repeat over staff+tab prints ONLY the % sign on each staff — the tab
        // hides its fret digits like the notation staff hides its notes (else bare,
        // stemless digits sat under the % sign).
        yield return new object[] { "test/tab-percent-repeat" };
        // `tab … as numbers`: fret digits only — no stems, beams, dots, rests or
        // tuplet brackets (the paired notation staff above carries the rhythm).
        yield return new object[] { "test/tab-as-numbers" };
        // `tab … with chords …`: a chord part's symbols align above the tab staff.
        yield return new object[] { "test/tab-with-chords" };
        // Per-occurrence lyric verses `[1. …] [2. …]`: A sings verse 1, the A2 reprise verse 2.
        yield return new object[] { "test/lyrics-volta" };
        // A below-staff D.C. drops below the lyric line rather than overprinting it.
        yield return new object[] { "test/nav-below-clears-lyrics" };
        // A tab staff on the indented first system: its lines start at the indent
        // (aligned with the notation staff), not the page margin.
        yield return new object[] { "test/tab-indent" };
        // Articulations above a tab staff clear the fret digits (which protrude
        // past the outer line), not land on the numbers.
        yield return new object[] { "test/tab-articulations" };
        // An explicit .down overrides a fermata's default UP side — it sits below
        // the notes with the flipped glyph, not hugging the beam above.
        yield return new object[] { "test/fermata-down" };
        // Scripts on BEAMED notes take the beam-resolved stem side: a stem-up beam
        // group puts staccatos BELOW every note (even the high ones), not above the
        // beam on the stem side.
        yield return new object[] { "test/beamed-script-side" };
        // A hyphenated instrument preset ("electric-bass") is ONE word: its clef
        // default (bass) resolves from the joined name, not the first "electric"
        // segment (which would fall through to the default treble).
        yield return new object[] { "test/instrument-hyphenated-clef" };
        // A rootless degree chord is order-independent: each degree resolves against
        // the key tonic on its own, so <1' 3 5> (1' = the octave = degree 8) is the
        // same chord as <3 5 8>, whatever order the degrees are written in.
        yield return new object[] { "test/degree-chord-root-octave" };
        // A bare @chord on a rootless degree chord names it from its FIRST degree as
        // root: <2 4 6> in C major is Dm (D-F-A), not a C chord.
        yield return new object[] { "test/degree-chord-name" };
        // Octave marks AFTER the closing '>' shift the WHOLE chord uniformly:
        // <1 3 5>' up an octave, <1 3 5>, down one; <c e g>' shifts a named chord too.
        yield return new object[] { "test/chord-octave-marks" };
        // A fully-beamed tuplet's number on a TAB staff must clear the ACTUAL tab
        // beam edge (the beam floats off the digits, not at the notation height) —
        // otherwise the "3" overprints the tab beam.
        yield return new object[] { "test/tab-tuplet-number" };
        // An explicit `transposition 8vb` frets a bare `c` on the tab exactly as the
        // `instrument bass` preset does (the preset just bundles it) — A string, 3rd fret.
        yield return new object[] { "test/transposition-explicit" };
        // An eighth-note triplet beams as ONE group (a single "3", no bracket) even
        // though its notes carry their written 1/8 duration across a beat boundary.
        yield return new object[] { "test/tuplet-eighth-beam" };
        // A staccato on a beamed tab note sits opposite the BEAM's direction, not the
        // note's own string — so a string-2 note under an up-beam gets its dot below.
        yield return new object[] { "test/tab-staccato-beam-side" };
        // Every note names its string, so this is the one tab book whose beams can be
        // compared with LilyPond's at all: the two engines' string allocators disagree,
        // and a tab beam sits on the STRING. Directions match, heights do not.
        yield return new object[] { "test/tab-string-pinned" };
        // A fermata reaches sideways past the head; the next note's accidental is
        // spaced clear of it (skyline-gated, so only when they overlap in Y).
        yield return new object[] { "test/fermata-note-spacing" };
        // Articulations on notes INSIDE a tuplet body render (ProcessTuplet runs
        // the same post-event collectors as the top-level stream).
        yield return new object[] { "test/tuplet-articulations" };
        // A grace before a chord clears the chord's staggered accidental stack
        // (grace-to-main gap uses the chord's full left extent, not one accidental).
        yield return new object[] { "test/grace-chord-accidental" };
        // A grace before an accidental chord AT THE LINE START reserves grace +
        // accidental in the prefix spring, so it doesn't hang into the clef/time.
        yield return new object[] { "test/grace-accidental-line-start" };
        // A tab staff BELOW a notation staff of the same music: the tab's
        // forced-above fermata/flageolet drop into the inter-staff gap and must
        // clear the notation staff's low noteheads (the gap reserves for them).
        yield return new object[] { "test/tab-articulations-multistaff" };
        // A below-staff marcato on a low note joins the staff's down-skyline, so the
        // lyric row drops below it instead of the 'v' overprinting the syllable.
        yield return new object[] { "test/lyrics-below-marcato" };
        // A note below the tab's lowest string is hidden (no fret/stem/beam) rather
        // than clamped to a wrong open string; the notation staff still shows it.
        yield return new object[] { "test/tab-below-range" };
        // A forced-above script on a BEAMED note clears the beam on both the
        // notation staff and the tab (whose beam floats above the fret digits).
        yield return new object[] { "test/tab-beam-script" };
        // Augmentation dots draw to the right of a tab fret digit (and each chord
        // string row) — the fret number carries no notehead, so the dot is the only
        // dotted-value cue on the tab; the stem/flag show the base duration.
        yield return new object[] { "test/tab-dotted-values" };
        // A stem-coupled tab script tucks INSIDE the staff only on the stem's far
        // side; an explicit .up/.down that forces it onto the stem's own side
        // (`@accent.down` on a top-string, stem-down note) clears the whole staff
        // instead of colliding with the stem.
        yield return new object[] { "test/tab-forced-script-side" };
        // A cross-string beamed group slopes the tab beam along the digit contour
        // (LP-like), keeping the lower digits' stems short and above-scripts clear.
        yield return new object[] { "test/tab-beam-slope" };
        // Tab grace notes render as small fret numbers on the string line, not
        // noteheads at the notation pitch height.
        yield return new object[] { "test/tab-grace" };
        // Bend-after gestures (@fall / @doit) render as trailing curves on both
        // the notation staff and the tab.
        yield return new object[] { "test/bend" };
        // Dead (muted) notes (@dead) render as "×" noteheads / tab "×".
        yield return new object[] { "test/dead-note" };
        yield return new object[] { "test/tuplets" };
        yield return new object[] { "test/tuplets-beamed" };
        yield return new object[] { "test/grandstaff-repeat" };
        yield return new object[] { "test/bass-clef" };
        yield return new object[] { "test/keysig-treble" };
        yield return new object[] { "test/keysig-bass" };
        yield return new object[] { "test/keysig-clefs" };
        // A custom (non-traditional) signature reserves its INK width like the standard
        // key it spells — the reserve/draw one-key-model unification (ledger KCS/KCC).
        yield return new object[] { "test/custom-key" };
        // The ossia's key sits in the system-wide key column, its glyph advances scaled
        // with the small staff (ledger line-start.ossia-key-alignment).
        yield return new object[] { "test/ossia-key" };
        // A tab-only part's own key reserves nothing — a tab staff engraves no signature,
        // so the reservation and the drawing walk select the same staves (ledger
        // line-start.time-to-first-note.tab-keyed).
        yield return new object[] { "test/tab-part-key" };
        // Middle C in all four clefs — guards that alto and tenor C-clefs are
        // positioned distinctly (tenor a third higher than alto).
        yield return new object[] { "test/clef-positions" };
        yield return new object[] { "test/treble8" };
        yield return new object[] { "test/ledger-lines" };
        yield return new object[] { "test/barcheck" };
        // The dashed barline (LilyPond \bar "!"). The ONLY fixture that uses '!'
        // at all — before it, DrawBarline's BarlineType.Dashed arm was pinned by
        // nothing, in a corpus of 190 snapshots. Written spaced, which is the
        // form LYS4009 leaves silent.
        yield return new object[] { "test/dashed-barline" };
        // A 'partial 4' pickup: the opening measure auto-closes after one quarter
        // (no written barline needed), then 4/4 resumes for the full bars.
        // Verified against LilyPond \partial 4.
        yield return new object[] { "test/partial-pickup" };
        // A leading pickup is bar 0, so the second system (forced break) starts at
        // bar 3, not 4 — the BarNumberEngraver pickup offset. Verified vs LilyPond.
        yield return new object[] { "test/partial-barnumber" };
        yield return new object[] { "test/break" };
        // A section with no time of its own opens at the SCORE meter, not the meter a
        // prior section changed to mid-way (section B must NOT inherit A's inner 3/4).
        yield return new object[] { "test/section-meter-resets-to-global" };
        // Syllables align to their notes and the bar widens for them even when it opens
        // with a mid-piece time-signature change (the leading grob used to cluster them).
        yield return new object[] { "test/lyrics-under-midpiece-meter" };
        // A whole-rest opening bar counts in the lyric alignment: a bare leading "| "
        // skips it, so the verse lines up one bar in with no stray connector over the rest.
        yield return new object[] { "test/lyrics-after-rest-bar" };
        yield return new object[] { "test/lyrics" };
        // Two verses: each verse line gets its stanza number ("1." / "2.") at the
        // left of its lyrics, not floating at the top of the page.
        yield return new object[] { "test/lyrics-verses" };
        // Measure 2 dips to C3 (four ledger lines below). The whole lyric line
        // drops uniformly so the TEXT clears the low notes — the up-skyline is
        // built from the real glyph height (ascender vs x-height), so the drop
        // matches the true ink. Guards the lyric/note overlap regression.
        yield return new object[] { "test/lyrics-skyline" };
        yield return new object[] { "test/multi-voice" };
        // Sequential measures around a voice { } { } span — the single-voice measures
        // before and after the parallel block must not be dropped.
        yield return new object[] { "test/voice-mixed" };
        // Intra-staff polyphony on a grand staff: both voices of a voice { } { } in
        // the upper staff render, with the surrounding single-voice measures.
        yield return new object[] { "test/voice-grandstaff" };
        // Two voices each with a dynamic on the same note column — both render
        // (stacked) rather than one hiding the other.
        yield return new object[] { "test/voice-dynamics" };
        // A voice { } { } span mid-stream: its voices' dynamics land in the span's
        // measure, not back in measure 0.
        yield return new object[] { "test/voice-dynamics-mid" };
        // Multi-staff: a low lower-voice dynamic in the TOP staff's voice { } { } must
        // widen the inter-staff gap so it clears the staff below instead of
        // overlapping its lines (dynamics join the outside-staff skyline).
        yield return new object[] { "test/voice-dynamics-multistaff" };
        // Multi-staff: dynamics that belong to the SECOND (bass) staff render
        // under THAT staff, not collapsed under the first (DynamicItem.StaffIndex).
        yield return new object[] { "test/dynamics-lower-staff" };
        // Forced-above dynamics (@x.up): on-staff above vs. below neighbours,
        // note-governed placement over high notes, and two-voice same-column
        // stacking outward. Guards the above-staff dynamic geometry (descender-aware
        // clearance, matched to LilyPond \dynamicUp); no other fixture uses it.
        yield return new object[] { "test/above-dynamics" };
        // Multi-staff: articulations + ornaments on the SECOND (bass) staff render
        // against that staff's notes, not the first (ArticulationItem.StaffIndex).
        yield return new object[] { "test/articulations-lower-staff" };
        // Multi-staff: a tuplet bracket on the SECOND (bass) staff sits over that
        // staff's notes, not the first (TupletBracketItem.StaffIndex).
        yield return new object[] { "test/tuplet-lower-staff" };
        // Multi-staff: a tuplet bracket over STEMLESS whole notes is RESERVED in the
        // inter-staff gap and does not reserve a stem the notes have not got. Neither
        // was true before: nothing seeded a TupletBracket into any vertical skyline, so
        // the lower staff's bracket was drawn across the upper staff's lines, and the
        // engraver added a full stem length regardless of duration. Pinned against real
        // LilyPond by staff.staff.tuplet-bracket-{up,down} in the LP-fidelity corpus.
        yield return new object[] { "test/tuplet-bracket-whole-notes" };
        // Multi-staff: a slur drooping into the inter-staff gap must be RESERVED there, so
        // the staff below clears its bow — SkylineBuilder had no slur in it. Pinned against
        // real LilyPond by staff.staff.slur-{under,over}-notes in the LP-fidelity corpus.
        yield return new object[] { "test/slur-under-whole-notes" };
        // Multi-staff: a BEAM drooping into the inter-staff gap must be RESERVED at its DRAWN
        // geometry, not the fixed 3.5 stem AddNoteBoxToSkylines gives an unbeamed note —
        // SkylineBuilder now suppresses the beamed stems and seeds the beam's outer edge.
        // Pinned against real LilyPond by staff.staff.beam-{under,over}-notes in the corpus.
        yield return new object[] { "test/beam-under-staves" };
        // Multi-staff: an arpeggio on a SECOND (bass) staff chord draws to the
        // left of that chord, not the first staff (ArpeggioItem.StaffIndex).
        yield return new object[] { "test/arpeggio-lower-staff" };
        // Multi-staff: figured bass + chord names on the SECOND (bass) staff
        // render against that staff (FiguredBassItem/ChordNameItem.StaffIndex).
        yield return new object[] { "test/figbass-chordname-lower-staff" };
        // Multi-staff: grace notes on the SECOND (bass) staff render against that
        // staff (GraceNoteItem.StaffIndex / GraceNoteLayout.StaffYOffset).
        yield return new object[] { "test/grace-lower-staff" };
        // SINGLE-staff: arpeggio renders at all (FromScore used to drop it) and to
        // the left of the heads, and grace accidentals are reduced + spaced. These
        // paths were never covered because every other fixture is a grand staff.
        yield return new object[] { "test/single-staff-arpeggio-grace" };
        // Diverse-coverage fixtures (dimensions the grand-staff/loose fixtures
        // missed): tight chromatic + chord accidentals, alto/tenor C-clefs, and
        // two-voice polyphony. Authored in relative mode, octaves verified with
        // `check --pitches`.
        yield return new object[] { "test/dense-chromatic" };
        yield return new object[] { "test/alto-tenor-clefs" };
        yield return new object[] { "test/two-voice-polyphony" };
        // A leading in-music \time collapses into the initial signature (no
        // redundant "C" before the "3/4").
        yield return new object[] { "test/leading-time-change" };
        // Meter-dependent 8th beaming: 3/4 (6), 6/8 (3+3), 5/4 (2+2+2+2+2).
        yield return new object[] { "test/mixed-meters" };
        // Multi-staff: a trill spanner on the SECOND (bass) staff runs above that
        // staff, not pulled to the top by the stacker (TrillSpannerItem.StaffIndex).
        yield return new object[] { "test/trillspan-lower-staff" };
        // Multi-staff: fingerings on the SECOND (bass) staff render at that staff
        // (per-staff FingeringEngraver pass), instead of vanishing.
        yield return new object[] { "test/fingering-lower-staff" };
        // A pedal "Ped." mark anchors at the note it is written on (its column),
        // not the measure start (music-mark AnchorTiming).
        yield return new object[] { "test/pedal-note-anchor" };
        // A pedal keeps its OWN staff's below-baseline; the system-wide lyric floor
        // (for jump marks) must not drop it past a lyric line on another staff.
        yield return new object[] { "test/pedal-below-lyrics" };
        // A CJK lyric fills the em square (taller than any Latin word), so the lyric
        // line must drop to clear notes dipping below the staff.
        yield return new object[] { "test/cjk-lyric-clears-notes" };
        // A high lower-staff chord (reaching the upper staff's range with ledgers)
        // widens the inter-staff gap so it clears the upper staff's LINES, not
        // just its notes (the staff symbol joins the spacing skyline).
        yield return new object[] { "test/grandstaff-high-bass" };
        // Polyrhythm: an upper-voice triplet's "3" bracket sits above its notes
        // (the voice's stems are force-flipped), not below by the other voice.
        yield return new object[] { "test/voice-tuplet" };
        yield return new object[] { "test/collision" };
        yield return new object[] { "test/phrases" };
        yield return new object[] { "test/ornaments" };
        // A chordnames { } block: symbolic chord entry (c / a:m / g:7) auto-named
        // and shown above the staff, timing-aligned to the melody (mid-bar Am/G7).
        // Verified against LilyPond \chords.
        yield return new object[] { "test/chordnames" };
        // Breathing signs: @breath (comma) and @caesura (//) sit at the top of the
        // staff, right of the note they follow. Verified against LilyPond \breathe
        // / \caesura.
        yield return new object[] { "test/breath-marks" };
        yield return new object[] { "test/repeat-volta" };
        yield return new object[] { "test/dollar-ref" };
        yield return new object[] { "test/grammar-test" };
        yield return new object[] { "test/instrument-defaults" };
        yield return new object[] { "test/section-octave-reset" };
        yield return new object[] { "test/instrument-names" };
        yield return new object[] { "test/clef-change" };
        yield return new object[] { "test/keysig-change" };
        // A key change that CANCELS a 3-sharp signature (A major -> F major): the
        // three cancellation naturals kern by vertical-edge overlap (F#/C# wide,
        // C#/G# tight) before the new flat. Guards NaturalKernPadding — the only
        // fixture with a 3-natural cancellation.
        yield return new object[] { "test/keysig-cancel-naturals" };
        yield return new object[] { "test/trill-spanner" };
        yield return new object[] { "test/section-empty-placeholder" };
        yield return new object[] { "test/key-opening-override" };
        yield return new object[] { "test/key-per-staff" };
        yield return new object[] { "test/lv-meterchange" };
        yield return new object[] { "test/courtesy-accidentals" };
        yield return new object[] { "test/editorial-accidental" };
        yield return new object[] { "test/multi-line-spanners" };
        // Absolute octave mode (`octave absolute`): '/, resolve from a fixed C4
        // anchor with no carry, so octave leaps render directly. Relative is the
        // default and unaffected.
        yield return new object[] { "test/octave-absolute" };
        yield return new object[] { "test/octave-base" };
        // Named voices with per-voice bound lyrics; the alto's lyric aligns to its
        // own (differently-rhythmed) notes via timing-based X, not the soprano's
        // columns.
        yield return new object[] { "test/named-voice-lyrics" };
        yield return new object[] { "test/multi-measure-rest" };
        yield return new object[] { "test/multi-measure-rest-single" };
        // A key change at a run's LEFT bound stays IN the run (LilyPond hangs it on the
        // run's opening column) and reserves its own width there: one 5-bar church rest
        // with the signature before it, spanning 17.13 bar-to-bar vs 14.13 without the
        // change. Regression for both the run-grouping and the skyline MMR-rod paths —
        // Lily# used to eject the bar, printing a whole rest + a 4-bar rest. Verified
        // against LilyPond 2.24.4.
        yield return new object[] { "test/mmr-key-change-bound" };
        // The clef counterpart. Same grouping (one 5-bar church rest), but the clef is
        // drawn BEFORE the bar line — break-align-orders puts clef ahead of staff-bar —
        // so unlike the key case the run does NOT get wider: bar line to bar line stays
        // 14.13 against LilyPond's 14.133856, with or without the clef. Guards the
        // drawing side (BoundaryColumn / BoundaryClefX) together with the grouping.
        // Verified against LilyPond 2.24.4.
        yield return new object[] { "test/mmr-clef-change-bound" };
        // Multi-measure rests outside 4/4 (3/4 R2.*3, 2/4 R2*3) collapse like R1*3 does:
        // the rest fills the PREVAILING METER, not a whole note. The second run also opens
        // on a time change, which rides its left bound. Verified against LilyPond 2.24.4
        // (3/4 run spans 12.25 vs LP 12.245; the time signature buys +3.06 vs LP +3.055).
        yield return new object[] { "test/mmr-meter-and-time-change" };
        // Multi-staff: when ONE staff rests (R1*N) but ANOTHER has content over
        // the same measures, the resting staff shows individual whole rests and
        // BOTH staves keep their barlines — no merged MMR symbol (that only
        // happens via \compressMMRests over all-resting measures). Regression
        // for the dropped-barline bug. Verified against LilyPond 2.24.4.
        yield return new object[] { "test/multi-measure-rest-grandstaff" };
        // The opposite case to the one above: when BOTH staves rest the same bars the run
        // DOES collapse, and every staff prints its own church rest with its own count —
        // LilyPond runs the engraver per Staff context. Lily# used to emit a single symbol
        // on the top staff while suppressing the rest glyphs on all of them, leaving the
        // lower staff blank. Verified against LilyPond 2.24.4.
        yield return new object[] { "test/mmr-grandstaff-both-rest" };
        // Covers a stacked-digit time signature (3/8) AND a grand staff whose
        // secondary (bass) staff out-counts the primary in a measure — both
        // were uncovered and both had rendering bugs (time-sig digits too high;
        // surplus secondary noteheads dropped).
        yield return new object[] { "test/timesig-grandstaff" };
        // Mid-piece time signature changes (4/4 -> 3/4 -> 6/8 -> 2/4): digits
        // drawn at each change point, measure length re-armed, validator follows
        // the new meter. Verified against LilyPond \time at matching bars.
        yield return new object[] { "test/timesig-change" };
        // ...and the same changes on a staff that draws NONE of them: a score built only of
        // `tab … as numbers` is LilyPond's bare TabStaff, whose TimeSignature stencil is
        // BLANKED, so it reserves no column for a mid-piece meter either. The corpus's only
        // observer of that rule — the defect's scope is zero books, so nothing else was
        // watching (ledger mid-piece.tab-numbers.*). Poison TimeSignatureChangeItem.Blanked
        // or SpacingRules.ChangeItemHasInk and this book's digits move right from bar 3 on.
        yield return new object[] { "test/tab-numbers-meter-change" };
        // A meter change that lands at a system break: it must join the
        // system-start prefix (clef, key, time) instead of hanging far right of
        // the first note, and the key signature is reprinted at the system head.
        yield return new object[] { "test/timesig-change-linebreak" };
        // ...and the KEY half of the same group, which nothing covered until 2026-08-03:
        // a changed signature prints on BOTH sides of a break, so the line BEFORE one
        // carries a courtesy cancellation + new key (and the new meter after them when that
        // changed too). Moving SpacingRules.BarlineToCourtesyKey from an invented 0.8 to
        // LilyPond's declared 1.0 changed that drawing and not one snapshot moved, which is
        // how the hole was found. Both breaks are explicit, so a spacing change cannot
        // silently stop the fixture from testing what it is for.
        yield return new object[] { "test/key-change-linebreak" };
        // Part-option transpose: pitches re-spelled (c->d), key signature moved
        // (c major -> D major), in-key accidentals suppressed and an out-of-key
        // one (cis -> dis) kept. Verified against LilyPond \transpose c d.
        yield return new object[] { "test/transpose" };
        // Per-staff transpose in a multi-staff score: the transposed (top) staff
        // shows its own key (D major) while the concert (bottom) staff keeps C
        // major. Verified against LilyPond.
        yield return new object[] { "test/transpose-multistaff" };
        // Octave-marked transpose target (transpose: bes,) — a downward interval
        // spanning the octave; key moves G major -> F major. Verified vs LilyPond.
        yield return new object[] { "test/transpose-down" };
        // Top-level (score-wide) transpose default + bare headers: both parts
        // inherit transpose d (C major -> D major). Verified vs LilyPond.
        yield return new object[] { "test/transpose-score" };
        // Mid-piece tempo change: a new metronome mark (quarter = 160) prints at
        // the change point in the music stream. Verified vs LilyPond \tempo.
        yield return new object[] { "test/tempo-change" };
        // Mid-measure tempo on a grand staff with independent rhythms: the lower
        // staff's tempo must resolve via the shared timing columns and land on
        // its own note (beat 2), where the upper staff has no onset — not on the
        // upper voice's same-index note. Verified vs LilyPond \tempo.
        yield return new object[] { "test/tempo-grandstaff" };
        // Hara-kiri via the part property `removeEmpty all` (RemoveAllEmptyStaves):
        // the bass staff hides in rest-only systems, shows when playing (its
        // dynamic at ITS per-system Y), and a SECOND voice keeps it alive while
        // the primary voice rests. Guards the hidden-staff content skip.
        yield return new object[] { "test/hara-kiri" };
        // Ossia staff (reduced-size alternative passage), LP-aligned: the ossia
        // stacks ABOVE the staff it decorates (vertical-align-engraver.cc
        // alignAboveContext insert-before), and its X positions stay on the
        // score-wide spacing columns — barlines and note columns line up with
        // the full-size staff below; only the notation shrinks (magnifyStaff).
        yield return new object[] { "test/ossia" };
        // Ossia with beamed eighths + a dynamic: beams/stems (drawn by the
        // system-level beam pass) and the dynamic must scale with the small
        // staff while staying on the shared spacing columns.
        yield return new object[] { "test/ossia-beams" };
        // structure-level _"text" directive: engraves at the end of the section
        // just played (parsed-but-silent until the collector was connected).
        yield return new object[] { "test/custom-text" };
        // staff NAME with chords CHORDPART: named progression aligned above a staff.
        yield return new object[] { "test/chords-attached" };
        // Real stem length (StemCalculator) in the script side-position support.
        yield return new object[] { "test/scripts-stem-support" };
        // Below-staff scripts push the figured-bass row down (DOWN-skyline augmentation).
        yield return new object[] { "test/figbass-below-script" };
        // Staff-less chord+lyric ROW sheet (row spacing, line-break rewind, header ink).
        yield return new object[] { "test/rows-song-sheet" };
        // Rhythm notation: slash notes (one-line + five-line staves), bare-duration
        // runs, a pitched kick inside a slash bar, the bass pump, open/dotted slashes.
        yield return new object[] { "test/rhythm-slashes" };
        // The `sings` binding: two lyric tracks bound to an UNENGRAVED melody,
        // stacked as rows at that melody's rhythm under another part's staff.
        yield return new object[] { "test/sings-chorus-row" };
        // sfz-family dynamics + staccatissimo / bowing / harmonic scripts.
        yield return new object[] { "test/scripts-dynamics" };
        // @text("...") free expressive text (italic, below by default, .up above).
        yield return new object[] { "test/text-annotation" };
        // Metronome-mark beat units: whole (stemless, riding higher), half,
        // dotted quarter (dot beside the head), eighth (flag) — with bare
        // unquoted markings and pair-vs-pair stacking of adjacent tempo+label.
        yield return new object[] { "test/tempo-beat-units" };
        // Swing feel-equation drawn to the right of "= 120": its ink reaches over
        // the first beamed group, so the stacker must lift the whole tempo mark
        // clear of both the beam and the fermata rising under the swing symbol.
        yield return new object[] { "test/tempo-swing" };
        // A slur spanning a grace note on a tab staff must arch above the fret
        // digits — clearing the encompassed main-note digits AND the grace digit —
        // instead of running a pitch-based curve straight through them.
        yield return new object[] { "test/tab-grace-slur" };
        // A tied chord on a tab staff: each string's tie must sit on its own fret
        // digit (via the chord's exclusive allocation), not pile onto one string.
        yield return new object[] { "test/tab-chord-tie" };
        // Adjacent zigzagging tab chords must reserve their digit width in the
        // shared columns so the columns don't overprint.
        yield return new object[] { "test/tab-chord-spacing" };
        // The break gate prices tab digit floors like the layout (one shared
        // reservation list): six bars of bass-tab eighths break 3+3 instead of
        // packing one system that overruns the page edge.
        yield return new object[] { "test/tab-line-break" };
        // Voice three's literal automatic_shift offset (+0.652, not a +1-head
        // cascade) and the cross-voice column floor: the shifted cis2.'s dot must
        // push the next eighth column instead of printing through its head.
        yield return new object[] { "test/dot-cross-voice-spacing" };
        // An unbeamed eighth CHORD carries its flag (one Flag per Stem, LP
        // stem-engraver) — DrawChord had no flag branch and no fixture saw it.
        yield return new object[] { "test/chord-flag" };
        // Restore-first accidentals: 𝄪→♯ prints ♮♯, 𝄫→♭ prints ♮♭ (LP regression
        // accidental-single-double.ly). Columns LP-exact to two decimals.
        yield return new object[] { "test/accidental-restore-natural" };
        // @snappizz is the font's scripts.snappizzicato at LilyPond's own box and
        // position (LP regression articulation-snappizzicato.ly) — not primitives.
        yield return new object[] { "test/snappizzicato" };
        // A pickup starts mid-bar: its autobeam beat structure is the bar's TAIL
        // (LP regression auto-beam-partial.ly — 4/8 pickup in 6/8 beams 1+3, not 3+1).
        yield return new object[] { "test/autobeam-pickup" };
        // A four-beam (64th) stack spaces its lines at (3·ss+line−thick)/3 = 0.8733,
        // not the 3-and-under 0.81 (LP regression beam-quanting-horizontal.ly).
        yield return new object[] { "test/beam-64th-stack" };
        // A rest inside a manual beam is an INVISIBLE stem: its count clamps to the
        // least-beamed neighbour, the surviving beams run over it and the extras
        // become beamlets (LP regression beam-multiplicity-over-rests.ly).
        yield return new object[] { "test/beamlets-over-rests" };
        // A beamed rest between extreme chords rides the REAL collision shift up to
        // the beam, and the PURE estimate already raised its spacing box so the next
        // chord's flat clears it (LP regression beam-rest-extreme.ly: +3, +3, +3, 0).
        yield return new object[] { "test/beam-rest-extreme" };
        // A spacer under a sustained note makes NO column of its own (LilyPond prunes
        // the empty column as loose) and never drags shortest-playing below the real
        // note — c4 against s8[ s8] spaces as one plain quarter spring (LP regression
        // beam-skip.ly, whose actual claim — no crash on beams over skips — rides along).
        yield return new object[] { "test/beam-skip" };
        yield return new object[] { "test/drum-groove" };
        // Three pages. The only fixture that pins the VERTICAL across page boundaries:
        // page breaking, the stretched gaps on a filled page, and the last page taking
        // the force of the page before it (lily/page-breaking.cc:570-573). Without it a
        // change to the page constants moves every gap on every page and every other
        // snapshot stays green. See the header of the .lys for why three and not two.
        yield return new object[] { "test/multi-page-vertical" };
    }

    /// <summary>
    /// Showcase sample files.
    /// </summary>
    public static IEnumerable<object[]> ShowcaseSamples()
    {
        yield return new object[] { "showcase/01-expressions" };
        yield return new object[] { "showcase/02-ornaments" };
        yield return new object[] { "showcase/03-piano" };
        yield return new object[] { "showcase/04-advanced" };
        yield return new object[] { "showcase/05-special-techniques" };
        yield return new object[] { "showcase/06-meter-changes" };
        yield return new object[] { "showcase/07-lead-sheet" };
        yield return new object[] { "showcase/08-chorale" };
    }

    [Theory]
    [MemberData(nameof(TestSamples))]
    public void TestSample_MatchesSnapshot(string sampleName)
    {
        AssertSnapshotMatch(sampleName);
    }

    [Theory]
    [MemberData(nameof(ShowcaseSamples))]
    public void ShowcaseSample_MatchesSnapshot(string sampleName)
    {
        AssertSnapshotMatch(sampleName);
    }

    private void AssertSnapshotMatch(string sampleName)
    {
        var lysPath = Path.Combine(SamplesDir, sampleName + ".lys");
        Assert.True(File.Exists(lysPath), $"Source file not found: {lysPath}");

        // Render SVG (without font embedding for smaller/stable snapshots).
        // Normalize the SOURCE to LF first: the SVG carries data-pos byte offsets
        // into this source, so a CRLF working-tree checkout (Windows) would shift
        // every offset past a newline relative to an LF checkout (CI/Linux) and
        // break the snapshot cross-platform. Reading it LF-canonical pins data-pos
        // to one value everywhere, independent of how git materialized the file.
        var source = File.ReadAllText(lysPath).Replace("\r\n", "\n");
        var tree = SyntaxTree.Parse(source);
        var options = new SvgRenderOptions { EmbedFont = false };
        // Snapshots are platform-neutral: LF on disk (.gitattributes pins
        // Snapshots/*.svg to eol=lf) and LF in comparison, regardless of the
        // generator's native newline.
        var svg = SvgGenerator.Generate(tree, options).Replace("\r\n", "\n");

        // Snapshot file path: "test/notes" → "test__notes.svg"
        var snapshotFileName = sampleName.Replace("/", "__").Replace("\\", "__") + ".svg";
        var snapshotPath = Path.Combine(SnapshotsDir, snapshotFileName);

        if (UpdateSnapshots || !File.Exists(snapshotPath))
        {
            // Create or update baseline
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            File.WriteAllText(snapshotPath, svg);

            if (!UpdateSnapshots)
            {
                // First run — baseline created. A brand-new baseline still
                // needs eyes: put its rendering in the visual-diff report
                // (baseline == actual, 0 diff) so it can be inspected as a PNG
                // before being committed.
                string visual;
                try
                {
                    visual = "Review the rendering: " + VisualDiffReport.Record(sampleName, svg, svg);
                }
                catch (Exception ex)
                {
                    visual = $"(rendering review unavailable: {ex.GetType().Name})";
                }
                Assert.Fail(
                    $"Snapshot baseline created: {snapshotFileName}. {visual}\n" +
                    "Inspect it, then re-run to verify against the new baseline.");
            }
            return;
        }

        // Compare against baseline (newline-normalized: working trees may
        // hold either ending depending on git config and platform)
        var baseline = File.ReadAllText(snapshotPath).Replace("\r\n", "\n");
        if (svg != baseline)
        {
            // Find first difference for a helpful error message
            var (line, col) = FindFirstDifference(baseline, svg);

            // Rasterize both sides and refresh the reviewable HTML report — the
            // piece that lets a human JUDGE a geometry change instead of only
            // detecting it. Best-effort: a rasterizer failure must not mask the
            // snapshot failure itself.
            string visual;
            try
            {
                visual = "Visual diff report (open in a browser): " +
                         VisualDiffReport.Record(sampleName, baseline, svg);
            }
            catch (Exception ex)
            {
                visual = $"Visual diff report unavailable ({ex.GetType().Name}: {ex.Message})";
            }

            Assert.Fail(
                $"SVG snapshot mismatch for '{sampleName}' at line {line}, col {col}.\n" +
                visual + "\n" +
                $"Approve THIS fixture: pwsh tools/Approve-Snapshots.ps1 -Name {sampleName}\n" +
                $"Approve ALL: set LILYSHARP_UPDATE_SNAPSHOTS=1 and re-run.\n" +
                $"Baseline: {snapshotPath}");
        }
    }

    private static (int line, int col) FindFirstDifference(string expected, string actual)
    {
        int line = 1, col = 1;
        int len = Math.Min(expected.Length, actual.Length);
        for (int i = 0; i < len; i++)
        {
            if (expected[i] != actual[i])
                return (line, col);
            if (expected[i] == '\n')
            {
                line++;
                col = 1;
            }
            else
            {
                col++;
            }
        }
        // One is longer than the other
        return (line, col);
    }
}
