\version "2.26.0"
%
% WHAT DOES A GRACE NOTE'S BEAM QUANT TO?
%
% Every beam point in the ledger reads a beam at full size. A grace beam is the same quanter
% run with three of its inputs scaled: ly/grace-init.ly sets Voice.fontSize = -3 and, on the
% Beam itself, beam-thickness = 0.384 and length-fraction = (magstep -3), with the Stem's
% length-fraction scaled to match. The beam TRANSLATION is derived, not declared —
% lily/beam.cc:558-571 Beam::get_beam_translation returns
%   fract * ((2 * staff_space + line_thickness - beam_thickness) / 2)   [beam_count < 4]
% so the scaling reaches the quant grid through the thickness AND through the fraction.
%
% This is the one divergence a twin sweep of the whole fixture corpus left standing outside a
% known gate (2026-08-01): every ordinary beam reading in the corpus matches LilyPond exactly,
% and the three books that carry a grace beam — test/grace-notes, test/grace-lower-staff,
% showcase/02-ornaments — all miss it by the SAME two numbers.
%
% Output: PROBEGR <name> BEAM pos=<positions> thick=<beam-thickness> fract=<length-fraction>
%                        dirs=<...> info=<per-stem stem-info>
%   stem-info is (ideal_y shortest_y), lily/stem.cc:1123-1133 — the pair the quanter fits.
%
% G is the corpus regime: bar 1 of test/grace-notes, whose twin exports as
%   \relative c' { \grace { d16 e } f4 g2 r4 }
% H is the CONTROL and the point of the pair: the same two pitches as ORDINARY sixteenths.
% Their beam is already exact in Lily# (test/beaming's sixteenth groups all match), so the
% LP-side difference between G and H is exactly the grace scaling and nothing else — and
% whatever Lily# puts between them is the defect's whole size.
% I is the TRANSLATION control: the same grace beam a third higher. LilyPond translates it
% by 1.0 staff space and keeps the slope, and Lily# already reproduces that offset in
% showcase/02-ornaments — so a fix that changes the offset has broken something else.
\paper { indent = 0 ragged-right = ##t }

#(define (dump-beam name)
   (lambda (grob)
     (let* ((stems (ly:grob-array->list (ly:grob-object grob 'stems)))
            (sys (ly:grob-system grob)))
       (format #t "PROBEGR ~a BEAM pos=~a thick=~a fract=~a dirs=~a info=~a stemx=~a xext=~a\n" name
               (ly:grob-property grob 'positions)
               (ly:grob-property grob 'beam-thickness)
               (ly:grob-property grob 'length-fraction)
               (map (lambda (s) (ly:grob-property s 'direction)) stems)
               (map (lambda (s) (ly:grob-property s 'stem-info)) stems)
               ;; The x each stem stands at, and the beam's own drawn extent — the frame
               ;; the quanter's x_span_ is measured in (lily/beam-quanting.cc:419).
               (map (lambda (s) (ly:grob-relative-coordinate s sys X)) stems)
               (ly:relative-group-extent (list grob) sys X)))))

sweep =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with { \override Beam.after-line-breaking = #(dump-beam name) }
      { $music } #})

% test/grace-notes, phrase graceBasic.
\score { \sweep "G" { \time 4/4 \grace { d'16 e' } f'4 g'2 r4 } }

% The same two pitches, ordinary sixteenths — the size the quanter is already exact at.
\score { \sweep "H" { \time 4/4 d'16 e' r8 g'2 r4 } }

% The same grace beam a third up: LilyPond translates, it does not re-quant.
\score { \sweep "I" { \time 4/4 \grace { f'16 g' } a'4 g'2 r4 } }
