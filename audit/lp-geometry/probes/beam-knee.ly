\version "2.26.0"
%
% WHERE DOES A KNEED BEAM SIT?
%
% The ledger's beam points all read beams whose stems point the SAME way. A knee is the one
% regime where they do not, and it is the one regime where a defect Lily# still carries can
% show: its quanter takes each stem's x from the NOTE COLUMN (BeamScoringProblem.cs:187)
% rather than from the stem, and a stem sits at the column plus an attachment offset that
% depends on its DIRECTION (a notehead's width for an up stem, ~0 for a down one). With every
% member pointing the same way that offset is a constant and cancels out of the span, the
% slope and the least squares alike; under a knee it alternates, and nothing cancels.
%
% Output: PROBEK <name> BEAM pos=<positions> dirs=<per-stem directions>
%
% A is the knee. B is its IDENTITY PAIR — the same music with the knee forbidden by
% auto-knee-gap, which is the strongest control there is (HANDOFF 5.3): one parameter apart,
% so the difference between them is the knee and nothing else. C is a three-stem knee whose
% middle stem is the one whose x offset differs from both ends' — the shape a slope defect
% would move most.
\paper { indent = 0 ragged-right = ##t }

#(define (dump-beam name)
   (lambda (grob)
     (format #t "PROBEK ~a BEAM pos=~a dirs=~a\n" name
             (ly:grob-property grob 'positions)
             (map (lambda (s) (ly:grob-property s 'direction))
                  (ly:grob-array->list (ly:grob-object grob 'stems))))))

sweep =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with { \override Beam.after-line-breaking = #(dump-beam name) }
      { $music } #})

% C4 against C6: fourteen staff positions apart, well past auto-knee-gap (5.5 staff spaces).
\score { \sweep "A" { \time 4/4 c'8 c''' c' c''' r2 } }

\score { \new Staff \with {
    \override Beam.after-line-breaking = #(dump-beam "B")
    \override Beam.auto-knee-gap = #100
  } { \time 4/4 c'8 c''' c' c''' r2 } }

\score { \sweep "C" { \time 4/4 c'8 c''' c' r4 r2 } }

% …and a control with no leap at all, to say the readings above are not a floor.
\score { \sweep "D" { \time 4/4 c'8 e' c' e' r2 } }
