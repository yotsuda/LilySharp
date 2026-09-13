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
%
% WHAT IT SAYS (commit 8bf5bb1a; re-run with -dbackend=null in session 376, when this record
% was added — the header had never carried it):
%   (-2.0 . -2.0)   voice 2's sixteenth beam in bar 1 (not the question)
%   (5.81 . 5.81)   bar 1, the b8 b beam over a BEAMED stem
%   (5.81 . 5.81)   bar 2, over an UNBEAMED stem
%   (3.0  . 3.0)    bar 3, nothing to clear
% ⇒ the same three answers the separate scores of beam-over-stem.ly give, so one line and one
%   spacing solution do not change the quanter's answer here. No ledger point on purpose: the
%   quantity is already beam.quant.over-stem.*; what this book added is that the snapshot
%   test/beam-over-stem steps on it.
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
