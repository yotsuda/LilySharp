\version "2.26.0"
%
% THE TWIN OF THE CORPUS BOOK test/beam-over-stem.
%
% beam-over-stem.ly asks the same question one score at a time, which is what the ledger
% points beam.quant.over-stem.{beamed,unbeamed,free} are measured from. This file asks it
% the way the corpus book asks it — three bars in ONE score, sharing a line and a spacing
% solution — because that is what the snapshot draws, and "the same music in three separate
% scores" is not the same input.
%
% Output: PROBEQ <name> bar=<n> positions=<Beam.positions>
\paper { indent = 0 ragged-right = ##t }

#(define (dump-positions name)
   (lambda (grob)
     (format #t "PROBEQ ~a positions=~a\n" name
             (ly:grob-property grob 'positions))))

\score {
  \new Staff \with { \override Beam.after-line-breaking = #(dump-positions "book") }
  <<
    { b'8 b' s2. | b'8 b' s2. | b'8 b' s2. }
    \\
    { s16 d'''16 d''' d''' s2. | s16 d'''4 s8. s2 | s1 }
  >>
}
