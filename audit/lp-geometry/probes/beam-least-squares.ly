\version "2.26.0"
%
% DOES LILYPOND LEAST-SQUARE EVERY BEAM?  No — and the branch it takes instead is the one
% Lily# never had.
%
% lily/beam-quanting.cc:551-580 least_squares_positions computes the ideal Y of the FIRST
% and LAST stems and, when they are EQUAL, does no fit at all: the beam is flat at that
% ideal and least-squares-dy (musical_dy_) is 0. That zero is not cosmetic — it is what
% score_slope_direction reads (:1174-1200: any tilt against a zero damped_dy costs
% DAMPING_DIRECTION_PENALTY, 800) and what score_slope_musical reads (:1204-1210: 400 per
% staff space of tilt beyond |musical_dy|). A beam that took the fit instead comes out
% tilted and STAYS tilted, because the two scorers that would have flattened it are asleep.
%
% There is a sub-branch (:569-575): two stems whose ideal both land ON the middle line have
% equal ideals for a second reason, and flat there reads as squashed, so LilyPond gives them
% an artificial slope of half a beam thickness in the direction the chord moves. That is why
% least-squares-dy comes back as exactly 0.48 (= beam-thickness) for the two-stem books.
%
% Output: LSQ <name> pos=<positions> lsdy=<least-squares-dy> knee=<knee>
%
% What it prints (2.26.0):
%   E  (0.81 . 0.81)   lsdy 0.0     — same outer pitch, no fit, FLAT
%   F  (0.81 . 2.0)    lsdy 1.997   — control: outer pitches differ, so the fit runs
%   G  (-5.5 . -5.5)   lsdy 0.0     — same outer pitch with the middle far away: still flat,
%                                     which is what says the middle notes never entered a fit
%   H  (-3.0 . -3.0)   lsdy 0.0     — two stems on the middle line, but their ideal is not 0,
%                                     so the artificial slope does NOT fire: the guard is
%                                     ideal[LEFT] == 0, not "both heads on the middle line"
%   I  (-0.19 . 0.0)   lsdy 0.48    — the artificial slope, rising
%   J  (0.19 . 0.0)    lsdy -0.48   — and falling
%   K  three beams: (-0.19 . 0.0) 0.48 / (0.0 . 0.19) 0.48 / (0.0 . 0.0) 0.0
%                                   — the twin of LilySharp.Tests/Fixtures/test/beamlets.lys
%                                     (Lily# `c''` is LilyPond `c'''`). Its third group has
%                                     THREE stems, so it takes the plain flat branch and the
%                                     artificial slope leaves it alone: one book, both paths.
%   C3 (0.19 . 0.19)   lsdy 0.0     — beam-knee.ly's score C. Its outer stems are the same
%                                     note, so it is flat for THIS reason and not a kneed one.
\paper { indent = 0 ragged-right = ##t }

#(define (dump name)
   (lambda (grob)
     (format #t "LSQ ~a pos=~a lsdy=~a knee=~a\n" name
             (ly:grob-property grob 'positions)
             (ly:grob-property grob 'least-squares-dy)
             (ly:grob-property grob 'knee))))

probe =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with { \override Beam.after-line-breaking = #(dump name) }
      { $music } #})

\score { \probe "E same-outer"     { \time 4/4 c'8 d' e' c' r2 } }
\score { \probe "F diff-outer"     { \time 4/4 c'8 d' e' g' r2 } }
\score { \probe "G same-outer-hi"  { \time 4/4 c'8 a'' a'' c' r2 } }
\score { \probe "H middle-two"     { \time 4/4 b'8 b' r4 r2 } }
\score { \probe "I art-rising"     { \time 4/4 c'''8. d'''16 r2. } }
\score { \probe "J art-falling"    { \time 4/4 d'''8. c'''16 r2. } }
\score { \probe "K beamlets-twin"  { \time 4/4 c'''8. d'''16 e'''16 f'''8. g'''8 a'''16 b'''16 c''''8 } }
\score { \probe "C3 knee3"         { \time 4/4 c'8 c''' c' r4 r2 } }
