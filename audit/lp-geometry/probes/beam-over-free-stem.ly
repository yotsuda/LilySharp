\version "2.26.0"
%
% How far does LilyPond lift a beam over an UNBEAMED STEM of another voice?
%
% Owner's report 2026-10-20 (scratch/2beam.lys): a bass-clef bar
% << { b,8 g' g, a' } \\ { b,8. a16 g4 } \\ { d'2 } >>. The third voice's half note is
% shifted beside the first voice's head with its stem up, and in Lily# that stem ran into
% the first voice's beam. LilyPond books the Stem grob's own box as a covered grob
% (beam-collision-engraver.cc acknowledge_stem; only BEAMED stems are dropped) and lifts
% the beam clear of the stem's tip.
%
% A = the real case.  B = the CONTROL without the third voice.
% C = the third voice as a quarter and a rest (the stem is still free; same lift).

\paper { indent = 0 }

#(define (dump-beam name)
   (lambda (grob)
     (format #t "\n~a BEAM positions=~a X-positions=~a\n" name
             (ly:grob-property grob 'positions)
             (ly:grob-property grob 'X-positions))))

#(define (dump-stem name)
   (lambda (grob)
     (format #t "\n~a STEM Y-extent=~a X-extent=~a\n" name
             (ly:grob-extent grob grob Y)
             (ly:grob-property grob 'X-extent))))

% Output (LilyPond 2.26.0):
%   A3 STEM Y-extent=(0.259 . 3.3333333333333335) X-extent=(-0.065 . 0.065)
%   A BEAM positions=(3.5 . 4.81)   then (3.0 . 4.81)
%   B BEAM positions=(2.81 . 4.19)  then (3.0 . 4.81)
%   C BEAM positions=(3.5 . 4.81)   then (3.0 . 4.81)

\score {
  \new Staff \relative c' { \clef bass \time 2/4
    << { \override Beam.after-line-breaking = #(dump-beam "A") b,8 g' g, a' } \\ { b,8. a16 g4 } \\
       { \override Stem.after-line-breaking = #(dump-stem "A3") d'2 } >> }
  \header { piece = "A" }
}

\score {
  \new Staff \relative c' { \clef bass \time 2/4
    << { \override Beam.after-line-breaking = #(dump-beam "B") b,8 g' g, a' } \\ { b,8. a16 g4 } >> }
  \header { piece = "B" }
}

\score {
  \new Staff \relative c' { \clef bass \time 2/4
    << { \override Beam.after-line-breaking = #(dump-beam "C") b,8 g' g, a' } \\ { b,8. a16 g4 } \\
       { d'4 r } >> }
  \header { piece = "C" }
}
