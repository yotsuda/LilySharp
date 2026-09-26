\version "2.26.0"
%
% A PHRASING SLUR WRITTEN TOWARD THE STEMS OF A FULL TAB — the regime tab-slur.ly and
% tab-slur-full.ly cannot reach, because there the direction rule always puts the bow on
% the side AWAY from the stems. Here every digit is on the two TOP strings (stems DOWN, one
% beam per beat) and the phrasing slur is written _\( … \), so it must pass BELOW the beams:
% the edge attachment is the beamed stem's end + dir*0.5*staff_space
% (lily/slur-scoring.cc:549-557) and every column's stem enters the encompass
% (:146-158). Under \tabFullNotation (Lily#'s default lone tab) the stems are real ones.
%
% SIXTEENTHS on purpose: an eighth group's stems carry Lily#'s declared tab deviation
% (BeamScoringProblem uniformBeamedLength, owner's decision 2026-09-24), a sixteenth group's
% do not, so the beam this bow hangs from is LilyPond's own.
% Every note names its string, as in tab-slur.ly: the two engines' allocators differ.
%
% Output as tab-slur.ly's, for the PhrasingSlur, in the tab's own spaces above its middle.
% Run with -dbackend=null (fret digits are text; see tab-slur.ly).

\paper { indent = 0 ragged-right = ##t }

\layout {
  \context {
    \Score
    \override PhrasingSlur.after-line-breaking =
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

gtr = \fixed c' {
  \time 2/4
  c'16\2_\( d'\2 e'\1 f'\1 g'\1 a'\1 g'\1 f'\1\) |
}

\score {
  \new TabStaff \with { stringTunings = #guitar-tuning }
    { \tabFullNotation \transpose c c, \gtr }
  \layout {}
}

% Score B: the MIDDLE beat on string 3 hangs its beam lower than the two outer beats'
% (string 1), so the bow attached under the outer beams must still clear the inner stems —
% the encompass half (lily/slur-scoring.cc:146-158), which score A cannot observe (its inner
% stems end inside the bow).
gtrb = \fixed c' {
  \time 3/4
  e'16\1_\( f'\1 g'\1 f'\1 g\3 a\3 b\3 a\3 e'\1 f'\1 g'\1 e'\1\) |
}

\score {
  \new TabStaff \with { stringTunings = #guitar-tuning }
    { \tabFullNotation \transpose c c, \gtrb }
  \layout {}
}

% Score C: UNBEAMED, FLAGGED edges. The two edge eighths stand alone (a quarter between
% them), stems DOWN toward the bow written below, so each edge's stem extent is the stem
% UNITED WITH ITS FLAG (lily/slur-scoring.cc:188-203 get_bound_info) -- the flag widens the
% X extent the stem-attachment rule (:738-760) reads.
gtrc = \fixed c' {
  \time 2/4
  e'8\1_\( f'4\1 g'8\1\) |
}

\score {
  \new TabStaff \with { stringTunings = #guitar-tuning }
    { \tabFullNotation \transpose c c, \gtrc }
  \layout {}
}
