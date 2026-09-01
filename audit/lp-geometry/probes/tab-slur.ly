\version "2.26.0"
%
% WHERE DOES A SLUR SIT ON A TAB STAFF?
%
% LilyPond does it in TWO stages and neither is a tab-specific curve:
%
%   1. the ORDINARY slur scorer runs, over bare fret digits. The TabStaff zeroes every
%      entry of Stem.details and sets no-stem-extend (ly/engraver-init.ly:1248-1256)
%      under the comment "make the Stems as short as possible to minimize their
%      influence on the slur::calc-control-points routine", then hides the stencil;
%   2. slur::move-closer-to-tab-note-heads (ly/engraver-init.ly:1275 →
%      scm/lily/tablature.scm:144-157) subtracts staff-space * direction * 0.35 from
%      ALL FOUR control points — a rigid translation, so stage 1 chooses the shape.
%
% The staff space is 1.5 (engraver-init.ly:1207), so stage 2 is 0.525 and stage 1 runs
% with every staff_space_-scaled quantity of lily/slur-scoring.cc at 1.5: the base
% attachment lift (:557), the grid step (:798), the minimum length (:729), the
% height-limit (:714) and both staff-line avoidances (:650/:655,
% slur-configuration.cc:61/69). A four-string tab's LINES are at odd positions
% (3, 1, -1, -3), which is what makes those last two different rather than merely
% scaled.
%
% ⚠️ BOTH DIRECTIONS, because the direction rule is the other half of the port and it
% reads the STRING, not the notated pitch (lily/slur.cc:60-68 calc_direction — UP as soon
% as one encompassed column's stem points DOWN). Bar 1 sits on the two HIGH strings
% (positions 1, 3, 3, 1 — stems down) and must bow UP; bar 2 is its EXACT REFLECTION on
% the two LOW ones (-1, -3, -3, -1 — stems up) and must bow DOWN. A probe with only the
% first would leave the rule untested and the sign of the 0.525 unobserved.
% ⚠️ THE REFLECTION IS THE POINT AND IT HAD TO BE BUILT. The first cut of this probe wrote
% bar 2 as 4,3,3,4 — a slur-down book, but not bar 1 mirrored (its ENDPOINTS sat on the
% outer string where bar 1's sit on the inner), so LilyPond answered two unrelated numbers
% and the pair asserted nothing about symmetry. Reflected, LilyPond must answer exact
% negatives, and a constant absorbed on one side cannot hide.
%
% The music is LilySharp.Tests/Fixtures/test/tab-slur-pinned's, and every note names its
% STRING (\4 \3 \2 \1) for the reason beam-tab.ly gives: the two engines' string
% allocators do not agree, and a slur hangs off the digit, so a book that leaves the
% choice open compares two fingerings rather than two slurs. Bass tuning E1 A1 D2 G2 and
% a tab frets the SOUNDING pitch, so every note here is fret 5 on its own string.
%
% ⚠️ PLAIN \new TabStaff, NOT \tabFullNotation: the two stages above are what the DEFAULT
% tab does, and \tabFullNotation reverts the stem overrides that stage 1 depends on.
%
% Output, per Slur, in the TAB STAFF'S OWN SPACES above its middle (the frame
% beam-tab.ly also reports in — divide the page-unit control point by staff-space).
% `span` is control-points[3].x - [0].x in the same unit: the bow's own length, which is
% what BezierBow's height is a function of, so a rise that differs can be told apart from
% a rise that differs BECAUSE the two engines spaced the columns differently.
%   PROBET TABSLUR dir=<1|-1> ss=<staff-space> span=<len> y0=<P0> y1=<C1> y2=<C2> y3=<P3>

\paper { indent = 0 ragged-right = ##t }

\layout {
  \context {
    \Score
    \override Slur.after-line-breaking =
      #(lambda (grob)
         (let* ((ss (ly:staff-symbol-staff-space grob))
                (staff (ly:grob-object grob 'staff-symbol))
                (cps (ly:grob-property grob 'control-points))
                ;; control-points are relative to the slur's own Y refpoint, which on a
                ;; single-staff score IS the staff symbol's; subtract it anyway, so the
                ;; reading cannot silently depend on that.
                (base (if (ly:grob? staff)
                          (- (ly:grob-relative-coordinate grob (ly:grob-common-refpoint grob staff Y) Y)
                             (ly:grob-relative-coordinate staff (ly:grob-common-refpoint grob staff Y) Y))
                          0)))
           (format #t "\nPROBET TABSLUR dir=~a ss=~a span=~a y0=~a y1=~a y2=~a y3=~a\n"
                   (ly:grob-property grob 'direction)
                   ss
                   (/ (- (car (list-ref cps 3)) (car (list-ref cps 0))) ss)
                   (/ (+ base (cdr (list-ref cps 0))) ss)
                   (/ (+ base (cdr (list-ref cps 1))) ss)
                   (/ (+ base (cdr (list-ref cps 2))) ss)
                   (/ (+ base (cdr (list-ref cps 3))) ss))))
  }
}

bl = \fixed c' {
  \time 4/4
  \key c \major
  g,4\2( c\1 c\1 g,\2) |
  d,4\3( a,,\4 a,,\4 d,\3) |
}

\score {
  \new TabStaff \with { stringTunings = #bass-four-string-tuning }
    { \transpose c c, \bl }
  \layout {}
}
