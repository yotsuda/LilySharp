\version "2.26.0"
%
% A GRACE COLUMN'S SPRING IS A NOTE SPRING. lily/note-spacing.cc:42-115 Note_spacing::get_spacing gives
% every spring - a grace one too (lily/spacing-basic.cc:163-180 swaps only the duration space) - the
% two columns' SKYLINE distance as its minimum and the optical stem correction on its ideal
% (:111, stem_dir_correction :206-315). Lily# priced a grace pair with flat reaches and no correction
% until session 728, so every grace gap read 1.417939.
%   UPTHIRD  c''16 -> e''16: same direction, heads two positions apart: ideal + 0.25 = 1.647939
%   STEP     grace e'' -> main f': the skylines never meet: ideal - 0.25 (same direction) = 1.147939
%   TOMAINDN grace g' -> main a'': up to down, the stems overlap: 1.726510
% The Lily# books are GKU / GKS / GKD / GKF (LpGeometryProbes.cs).
%
% Output: P <tag> HEAD x=<notehead refpoint x> ...
\paper { indent = 0 ragged-right = ##t }
#(define (dump-slur tag)
   (lambda (grob)
     (let* ((sys (ly:grob-system grob))
            (cps (ly:grob-property grob 'control-points))
            (refx (ly:grob-relative-coordinate grob sys X)))
       (format #t "\nP ~a SLUR x0=~a x3=~a span=~a\n" tag
               (+ refx (car (car cps))) (+ refx (car (list-ref cps 3)))
               (- (car (list-ref cps 3)) (car (car cps)))))))
#(define (dump-head tag)
   (lambda (grob)
     (let ((sys (ly:grob-system grob)))
       (format #t "\nP ~a HEAD x=~a ext=~a fs=~a\n" tag
               (ly:grob-relative-coordinate grob sys X)
               (ly:grob-extent grob sys X)
               (ly:grob-property grob 'font-size)))))
#(define (dump-stem tag)
   (lambda (grob)
     (let ((sys (ly:grob-system grob)))
       (format #t "\nP ~a STEM x=~a dir=~a\n" tag
               (ly:grob-relative-coordinate grob sys X) (ly:grob-property grob 'direction)))))
probe =
#(define-music-function (tag music) (string? ly:music?)
   #{ \new Staff \with {
        \override Slur.after-line-breaking = #(dump-slur tag)
        \override NoteHead.after-line-breaking = #(dump-head tag)
        \override Stem.after-line-breaking = #(dump-stem tag)
      } { $music } #})
\score { \probe "UPTHIRD" { c''4 \grace { c''16 e''16 } f'4 g'4 a'4 | } }
\score { \probe "STEP" { c''4 \grace { d''16 e''16 } f'4 g'4 a'4 | } }
\score { \probe "TOMAINDN" { c''4 \grace { f'16 g'16 } a''4 c''4 d''4 | } }
% FLAGLOW: a flagged grace d'8 before a main c'4 - the flag meets only part of the main column: 1.760352
% (the flat flag reach gave 1.938627; with the main note a third higher LilyPond reads that 1.938627 too).
\score { \probe "FLAGLOW" { c''4 \grace { d'8 } c'4 g'4 a'4 | } }
