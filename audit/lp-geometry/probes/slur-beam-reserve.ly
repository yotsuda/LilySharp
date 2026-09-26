\version "2.26.0"
%
% A SLUR THAT HANGS FROM A BEAM, WITH A TEXT RIDING ABOVE IT — the shape of
% input/regression/empty-chord.ly's first bar. The slur ends on the empty chord, which binds to
% the next column (a stem-DOWN c), so it bows UP, over the stem-up beam of e g: its left end
% attaches at that beamed stem's end + 0.5 staff space (lily/slur-scoring.cc:549-557). The text
% is placed over the slur, so its height reads the slur's.
%
% Lily# lays each staff's slurs out TWICE: once before spacing, for the skyline that reserves
% them (MultiStaffLayouter.StaffSlurLayouts), and once to draw. Until session 639 the first
% was handed no beams, so there the bow attached to an unbeamed stem tip and rose higher than
% the drawn one -- and the text, placed over the reserved bow, stood 0.32 higher.
%
% Output: PROBE TEXT ref = the TextScript's reference point (its baseline) above the staff's
% middle line, in staff spaces.

\paper { indent = 0 ragged-right = ##t }
#(define (dump tag)
   (lambda (grob)
     (let* ((sys (ly:grob-system grob))
            (st (ly:grob-object grob 'staff-symbol))
            (y (ly:grob-extent grob sys Y))
            (ref (ly:grob-relative-coordinate grob sys Y))
            (sy (if (ly:grob? st) (ly:grob-relative-coordinate st sys Y) 0)))
       (format #t "\nPROBE ~a ref=~a y=(~a ~a)\n" tag (- ref sy) (- (car y) sy) (- (cdr y) sy)))))
\layout { \context { \Score
  \override Slur.after-line-breaking = #(dump "SLUR")
  \override TextScript.after-line-breaking = #(dump "TEXT")
  \override Beam.after-line-breaking = #(dump "BEAM")
} }
\relative { r4 e'8( g <>) ^"sul D" c8 c c c }
