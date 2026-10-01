\version "2.26.0"
%
% A GRACE RUN ON A COMPRESSED LINE (session 732). A grace spring is built as Spring (len, increment), so
% its inverse compress strength is its own len - increment (lily/spring.cc:204-210
% set_default_compress_strength, run by the constructor; lily/spacing-basic.cc:163-175); Note_spacing
% then moves the ideal and the minimum but keeps that strength (lily/note-spacing.cc:77-113). On a line
% narrower than its natural width the grace gaps close to their minimum (1.417939 natural -> 1.217939
% at 36, 33 and 30mm, Lab sessions/p732/compress). Lily# held the run rigid until session 732.
% The same music as grace-stretch.ly on a 33mm line. The Lily# book is GSC (LpGeometryProbes.cs).
%
% Output: P GSC <notehead x> ...
#(define (dump-head tag) (lambda (grob) (let ((sys (ly:grob-system grob))) (format #t "\nP ~a ~a ~a\n" tag (ly:grob-relative-coordinate grob sys X) (ly:grob-property (ly:spanner-bound sys LEFT) 'rank)))))
\paper { indent = 0 line-width = 33\mm }
\score { \new Staff \with { \override NoteHead.after-line-breaking = #(dump-head "GSC") } { \time 4/4 c''4 \grace { d''16 e''16 } c''4 \grace { f''8 } e''4 f''4 | \break R1 | } }