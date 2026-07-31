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

% ---- 2026-08-01, session 60: A REGISTER SWEEP, BECAUSE G AND I ARE THE SAME REGISTER ----
% G and I both put the grace low in the staff. J puts it high, so the beam clears the top
% line instead of lying across it — a different regime both for score_forbidden_quants (no
% staff line left to fall in the gap) and for the quant RANGE.
%
% WHAT LILYPOND SAID (all four are graces, same size, same durations, same interval):
%   G  heads -5/-4   (0.142 . 0.5)     dy 0.358
%   I  heads -3/-2   (1.142 . 1.5)     dy 0.358
%   K  heads -2/-1   (2.142 . 2.5)     dy 0.358
%   J  heads +1/+2   (2.858 . 3.142)   dy 0.284   <- the family slope changes HERE
%
% ★★★ AND LILY# CHANGES ONE STEP EARLIER. J is exact to nine places; K is not, and Lily#
% gives K exactly J's slope (dy 0.284) one grid step down: (1.858 . 2.142) against
% (2.142 . 2.5). So the two engines agree on both sides of their own boundary and put the
% boundary in different places — which is a far sharper statement than a residual, and it
% was found by opening J as the divergent point and K as its control and being WRONG about
% which was which. Ledger beam.quant.grace.near-middle-bracket.* / .above-staff.*.
\score { \sweep "J" { \time 4/4 \grace { c''16 d'' } e''4 g''2 r4 } }

% ⚠️ THE FULL-SIZE CONTROL THAT H IS FOR G CANNOT EXIST HERE, and finding that out is
% worth writing down. Ordinary sixteenths at c''/d'' take DOWN stems (measured: LilyPond
% answers (-3.0 . -2.81) with dirs (-1 -1)), because a full-size beam only points up when
% its notes sit LOW, and then the beam is back inside the staff. A grace's stems are forced
% up whatever the pitch (scm/music-functions.scm:633-637), so "grace above the staff with
% up stems" has no natural full-size counterpart at all. Forcing \stemUp would make one,
% at the price of the forced-direction shorten (beam.cc:1061-1091, the 1/12 that closed
% beam.quant.mixed-count.peak-32.forced-stem) landing inside the control.
%
% ★ IT IS NOT NEEDED. The sweep's own members control each other: G, I, J and K are all
% graces at the same scale with the same durations and the same interval, differing only in
% register, so the grace scaling is common to all four and cannot be what separates them.
% K is the one just BELOW the middle line, where the beam clears the staff but only barely
% — meant as the bracket that locates the boundary, and it turned out to BE the divergence.
\score { \sweep "K" { \time 4/4 \grace { a'16 b' } c''4 g'2 r4 } }
