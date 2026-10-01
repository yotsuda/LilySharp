\version "2.26.0"
%
% THE SLUR'S SPACING ROD ON THE MAIN GRID. Slur carries (minimum-length . 1.5) and
% (springs-and-rods . ly:spanner::set-spacing-rods) (scm/define-grobs.scm:3176-3178), so its two bound
% columns stand at least 1.5 apart (lily/spanner.cc:429-473). On a 30mm line every spring is driven to
% its minimum, and a 64th on c'''' followed by one on c' has NO skyline overlap: without a slur LilyPond
% stands the two heads at the SAME x (SRN), with a slur from the one to the other 1.5 apart (SRR).
% Bodies are what `lysc ly` emitted for the .lys books SRR / SRN (LpGeometryProbes.cs), \cadenza free.
%
% Output: P <tag> HEAD x=<notehead refpoint x>
\paper { indent = 0 line-width = 30\mm }
#(define (dump-head tag)
   (lambda (grob)
     (let ((sys (ly:grob-system grob)))
       (format #t "\nP ~a HEAD x=~a\n" tag (ly:grob-relative-coordinate grob sys X)))))
probe =
#(define-music-function (tag music) (string? ly:music?)
   #{ \new Staff \with { \override NoteHead.after-line-breaking = #(dump-head tag) } { $music } #})
% A slur between two 64ths far apart in pitch, in a line squeezed by many notes.
\score { \probe "SRR" { \time 5/16 c'64[ c''''64]( c'64[) c''''64] c'64[ c''''64] c'64[ c''''64] c'64[ c''''64] c'64[ c''''64] c'64[ c''''64] c'64[ c''''64] c'64[ c''''64] c'64[ c''''64] } }
\score { \probe "SRN" { \time 5/16 c'64[ c''''64] c'64[ c''''64] c'64[ c''''64] c'64[ c''''64] c'64[ c''''64] c'64[ c''''64] c'64[ c''''64] c'64[ c''''64] c'64[ c''''64] c'64[ c''''64] } }
