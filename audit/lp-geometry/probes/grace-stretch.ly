\version "2.26.0"
%
% A GRACE RUN ON A JUSTIFIED LINE. LilyPond's grace spring is a spring like any other on the line:
% it stretches by force x inverse-stretch-strength, and its strength is increment / 2 ("Grace notes
% should not stretch very much", lily/spacing-basic.cc:163-175). The spring INTO the run is an ordinary
% note spring scaled by 0.8 (lily/spacing-spanner.cc:396-403). Lily# hung the run off its main column
% as a rigid chain until session 729. Two systems on a 100mm line; the first system's heads are read (the second is a whole-bar rest, so every head is the first system's).
% The Lily# book is GST (LpGeometryProbes.cs).
%
% Output: P GST <notehead x> ...
#(define (dump-head tag) (lambda (grob) (let ((sys (ly:grob-system grob))) (format #t "\nP ~a ~a ~a\n" tag (ly:grob-relative-coordinate grob sys X) (ly:grob-property (ly:spanner-bound sys LEFT) 'rank)))))
\paper { indent = 0 line-width = 100\mm }
\score { \new Staff \with { \override NoteHead.after-line-breaking = #(dump-head "GST") } { \time 4/4 c''4 \grace { d''16 e''16 } c''4 \grace { f''8 } e''4 f''4 | \break R1 | } }
