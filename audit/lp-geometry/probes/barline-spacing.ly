\version "2.26.0"
%% LP FIDELITY PROBE — bar-line spacing (Staff_spacing::get_spacing and its neighbours).
%%
%% Produces the numbers in ../lp-geometry.json under the "barline.*" keys. Run it with
%% ../Measure-LilyPondGeometry.ps1, which prints one line per score ready to paste.
%%
%% Each score below has a twin in LilySharp.Tests/LpFidelity/LpGeometryProbes.cs engraving
%% the SAME music. Mind the octave convention: Lily# `c` is LilyPond `c'`. The twin probes
%% name their counterpart in a comment; keep both sides in step or the comparison is
%% meaningless while still looking green.
%%
%% Every score is TWO measures and one system, so "the bar line" is unambiguous.
%%
%% ragged-right is deliberate: it puts every spring at force 0, i.e. at its natural length,
%% so what is measured is the spring's ideal rather than a share of some line's stretch.
%% (Stretch strength is verified separately by justifying the same music — see
%% SpacingInvariantTests.BarlineToFirstNoteSpring_StretchesByHalfTheSpaceAlistDistance.)

\header { tagline = ##f }

%% These dumps go to STDOUT. Keep LilyPond's stderr on its own stream when running this
%% (Measure-LilyPondGeometry.ps1 does): merging the two splices LilyPond's own diagnostics
%% into the MIDDLE of a dump line. Under -dbackend=null it always reports "Unbound
%% variable: output-stencils" at book-handling time, and that once landed inside score MC's
%% third note head, leaving a truncated `PROBE MC HEAD x=` that the parser then discarded —
%% so the probe looked complete while `clef -> next note` was actually being measured to the
%% FOURTH head. The value was never missing; the line was cut in half.

#(define ((gd tag name) g)
   (format #t "\nPROBE ~a ~a x=~a ext=~a\n" tag name
           (ly:grob-relative-coordinate g (ly:grob-system g) X)
           (ly:grob-extent g g X)))

lay =
#(define-scheme-function (tag) (string?)
   #{
     \layout {
       ragged-right = ##t
       line-width = 500\mm
       indent = 0
       \context {
         \Score
         \override BarLine.after-line-breaking         = #(gd tag "BAR")
         \override NoteHead.after-line-breaking        = #(gd tag "HEAD")
         \override Clef.after-line-breaking            = #(gd tag "CLEF")
         \override Rest.after-line-breaking            = #(gd tag "REST")
         \override Accidental.after-line-breaking      = #(gd tag "ACC")
         \override KeySignature.after-line-breaking    = #(gd tag "KEY")
         %% A change to a key with FEWER accidentals engraves the naturals as a separate
         %% KeyCancellation grob, and leaves the KeySignature itself empty. Dumping only
         %% KeySignature would measure to a grob with no ink (extent +inf.0 . -inf.0) while
         %% the glyphs the eye sees belong to the cancellation. Both are dumped; the empty
         %% one is dropped by the script, which says so.
         \override KeyCancellation.after-line-breaking = #(gd tag "KEY")
         \override TimeSignature.after-line-breaking   = #(gd tag "TIME")
       }
     }
   #})

%% A — plain measure start, UP stems after the bar line.
\score { \new Staff { \time 4/4 c'4 d' e' f' | g'4 a' b' c'' } \lay "A" }

%% B — clef change AT the bar line, DOWN stems after it.
\score { \new Staff { \time 4/4 c'4 d' e' f' \clef bass g4 a b c' } \lay "B" }

%% C — no clef, DOWN stems. Together with D this is the 2x2 that proves
%%     next_notes_correction tracks the STEM and not the clef.
\score { \new Staff { \time 4/4 c'4 d' e' f' | a''4 b'' c''' d''' } \lay "C" }

%% D — clef change AT the bar line, UP stems after it. Earns no correction at all.
\score { \new Staff { \time 4/4 c'4 d' e' f' \clef bass c,4 d, e, f, } \lay "D" }

%% E — a single whole note fills the measure, so full-measure-extra-space applies.
\score { \new Staff { \time 4/4 c'1 | c'1 } \lay "E" }

%% F — whole rests.
\score { \new Staff { \time 4/4 r1 | r1 } \lay "F" }

%% G — half notes.
\score { \new Staff { \time 4/4 c'2 c'2 | c'2 c'2 } \lay "G" }

%% X — an accidental opens the second measure. Its leftmost ink is the accidental, which
%%     declares extra-spacing-width (-0.2 . 0.0) rather than the default 0.1.
\score { \new Staff { \time 4/4 c'4 d' e' f' | cis'4 d' e' f' } \lay "X" }

%% CSB/CSA, CFB/CFA — the first probes to reach Accidental_placement's CHORD stacking
%%     (calc_positioning_done -> position_apes). Score X above measures a SINGLE accidental;
%%     these carry a cluster whose two accidentals' glyphs OVERLAP vertically and so are
%%     forced into TWO columns. Both accidentals are dumped as ACC grobs, so the raw per-grob
%%     anchors printed by Measure-LilyPondGeometry.ps1 give the ACC-to-ACC COLUMN GAP — the
%%     quantity that measures the stacking itself. That gap is measured between two accidentals
%%     of the SAME glyph, so whatever left-bearing the glyph's anchor carries cancels in the
%%     difference (the flat's anchor sits 0.12 right of its ink; the sharp's coincides).
%%
%%     THE PAIRS ARE VERTICAL MIRRORS, the P/Q discipline: the accidentals are always placed
%%     to the LEFT of the note column, right-to-left against the heads' LEFT skyline, and the
%%     stems never protrude past the head boxes on that side, so the column gap is independent
%%     of stem direction. A chord below the middle line (stems up) and its exact reflection
%%     above it (stems down) must therefore print the SAME gap; a difference is a
%%     direction-dependence defect on its own, whatever the value.
%%
%%     A THIRD (two staff positions) is used, not a second: a third does NOT reverse a head
%%     across the stem, so the heads share one column and the heads skyline is clean, while an
%%     accidental glyph (~2.6 ss tall) still overlaps its neighbour 1 ss away and must stack.
%%     Trailing notes are a' b' c'' (letters A/B/C) so none of the four chords' altered letters
%%     (D/F, E/G) recurs in the bar and no cancellation natural is engraved as a third ACC.
%%
%% CSB — two SHARPS a third apart BELOW the middle line (dis' -5, fis' -3), stems up.
\score { \new Staff { \time 4/4 c'4 d' e' f' | <dis' fis'>4 a' b' c'' } \lay "CSB" }

%% CSA — the mirror ABOVE the middle line (eis'' +3, gis'' +5), stems down. Must equal CSB.
\score { \new Staff { \time 4/4 c'4 d' e' f' | <eis'' gis''>4 a' b' c'' } \lay "CSA" }

%% CFB — two FLATS a third apart BELOW the middle line (ees' -4, ges' -2), stems up. Flats take
%%     the merge-overlap that sharps do not — the site of Lily#'s invented 0.375 constant, which
%%     accidental-placement.cc does NOT contain (LP's flats interlock via their real glyph
%%     skylines). This pair is where that invention is measured against LP.
\score { \new Staff { \time 4/4 c'4 d' e' f' | <ees' ges'>4 a' b' c'' } \lay "CFB" }

%% CFA — the mirror ABOVE the middle line (des'' +2, fes'' +4), stems down. Must equal CFB.
\score { \new Staff { \time 4/4 c'4 d' e' f' | <des'' fes''>4 a' b' c'' } \lay "CFA" }

%% K — mid-line key change (break-aligned into the boundary column by LilyPond).
\score { \new Staff { \time 4/4 c'4 d' e' f' \key a \major c'4 d' e' f' } \lay "K" }

%% T — mid-line time change (likewise break-aligned).
\score { \new Staff { \time 4/4 c'4 d' e' f' \time 3/4 c'4 d' e' } \lay "T" }

%% --- MID-MEASURE changes -------------------------------------------------------------
%% These sit INSIDE a measure rather than at a bar line, so they are not break-aligned at
%% all: LilyPond gives the change its own musical column between two notes. This is the
%% case COORDINATE_AUDIT.md 4.7 item 1 governs (the change-item branch of the extent
%% helpers), and it is measured NOTE-to-GLYPH rather than from a bar line.

%% MC — mid-measure clef change. The common case by far.
\score { \new Staff { \time 4/4 c'4 d' \clef bass e4 f4 } \lay "MC" }

%% MK — mid-measure key change.
\score { \new Staff { \time 4/4 c'4 d' \key a \major e'4 f'4 } \lay "MK" }

%% MKA — mid-measure key change whose FOLLOWING note carries an accidental. The accidental
%%       is the musical column's leftmost ink, so it enters Paper_column::minimum_distance
%%       and the staff-spacing.cc:213 correction lifts the gap ABOVE the space-alist ideal.
%%       MK cannot see that branch: its next note is a bare head, so the ideal wins there.
\score { \new Staff { \key g \major \time 4/4 c'4 d' \key c \major fis'4 g'4 } \lay "MKA" }

%% NO mid-measure TIME probe. `\time 3/4` inside a 4/4 bar makes LilyPond restructure the
%% measures rather than engrave a change column, and the resulting dump is not the thing we
%% would be comparing against. An uninterpretable probe is worse than no probe: it would
%% look like coverage. If a mid-measure time change ever needs a number, engrave it with
%% \partial / explicit bar checks so both sides agree on where the bars are.
