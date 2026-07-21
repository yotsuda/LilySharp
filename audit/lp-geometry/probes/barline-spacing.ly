\version "2.24.4"
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

%% NO mid-measure TIME probe. `\time 3/4` inside a 4/4 bar makes LilyPond restructure the
%% measures rather than engrave a change column, and the resulting dump is not the thing we
%% would be comparing against. An uninterpretable probe is worse than no probe: it would
%% look like coverage. If a mid-measure time change ever needs a number, engrave it with
%% \partial / explicit bar checks so both sides agree on where the bars are.
