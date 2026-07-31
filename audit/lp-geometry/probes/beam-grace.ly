\version "2.26.0"
%
% WHAT DOES A GRACE NOTE'S BEAM QUANT TO?
%
% Every beam point in the ledger reads a beam at full size. A grace beam is the same quanter
% run with scaled inputs: ly/grace-init.ly sets Voice.fontSize = -3 and, on the Beam itself,
% beam-thickness = 0.384 and length-fraction = 0.8, with the Stem's length-fraction to match.
% ⚠️ THREE DIFFERENT NUMBERS, and folding any two of them costs a session: the HEADS shrink by
% magstep(-3) = 0.7071, the beam's LENGTH-FRACTION is 0.8, and its THICKNESS is DECLARED 0.384.
%
% The beam TRANSLATION is derived — lily/beam.cc:130-145 Beam::get_beam_translation returns
%   (2 * staff_space * fract + line_thickness * fract - beam_thickness) / 2   [beam_count < 4]
%   (3 * staff_space * fract + line_thickness * fract - beam_thickness) / 3   [from four up]
% ⚠️ THE STAFF SPACE AND THE LINE ARE SCALED BY fract; THE THICKNESS IS NOT, because it arrives
% already scaled. LilyPond's comment at :138-141 says so — "we divide the thickness by fract".
% For a grace that is (2*0.8 + 0.1*0.8 - 0.384)/2 = 0.648, i.e. the full-size 0.81 scaled ONCE.
% ⚠️ THIS COMMENT USED TO SAY fract * ((2 + line - thickness) / 2) = 0.6864, and Lily# was
% built from the comment. It cost a quant step: beam_translation_ builds the gap a staff line
% may not fall into (lily/beam-quanting.cc:1287-1294), so the wrong gap moved which
% configuration wins. Ledger grace.beam.stack-gap.
%
% The OTHER input that only a grace can see: lily/beam-quanting.cc:80-87 multiplies
% SECONDARY_BEAM_DEMERIT by exp(-8 * |1 - length-fraction|) — "For stems that are non-standard,
% the forbidden beam quanting doesn't really work, so decrease their importance." A grace pays
% e^-1.6 = 0.2019 of the full charge, extra_demerit 1.0095 against a full-size beam's 5.0.
% ★ READ IT OFF LILYPOND, do not derive it: force a flat quant with \override Beam.inspect-quants
% and its card shows Fs, which adds extra_demerit exactly twice.
%
% This was the one divergence a twin sweep of the whole fixture corpus left standing outside a
% known gate (2026-08-01): every ordinary beam reading in the corpus matched LilyPond exactly,
% and the three books that carry a grace beam — test/grace-notes, test/grace-lower-staff,
% showcase/02-ornaments — all missed it. All three are exact as of 2026-08-01.
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
%   G  (0.142 . 0.5)     dy 0.358
%   I  (1.142 . 1.5)     dy 0.358
%   K  (2.142 . 2.5)     dy 0.358
%   J  (2.858 . 3.142)   dy 0.284   <- the family slope changes HERE
%
% ⚠️★★★ THE FIRST READING OF THIS SWEEP WAS AN ARTEFACT OF A MISMATCHED PAIR, and it is the
% cheapest lesson in the file. The .lys books for J and K were written `grace { c' d' }` and
% `grace { a b }`, WITHOUT the 16 — and a bare grace note is an EIGHTH in Lily#, so both were
% one-beam books measured against these two-beam twins. J landed on LilyPond's answer by luck
% and K did not, which read as "the two engines put the same regime change one step apart".
% They do not. Matched, K is exact and J missed by +0.642 / +0.716, which is what the fixture
% test/grace-lower-staff had been reporting the whole time.
% ⇒ ★★ WHEN A SWEEP INVERTS, CHECK THAT THE TWO SIDES ARE THE SAME MUSIC BEFORE BELIEVING IT.
%
% What J's divergence actually was, once the pair was a pair: LilyPond's own score cards
% agreed on every term but Fl, and both halves of that are in the header above (the 0.648
% translation and the exp(-8*|1-fract|) demerit scaling). Both ported 2026-08-01; all four
% books are exact. Ledger beam.quant.grace.above-staff.* / .near-middle-bracket.*.
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
% K is the one just BELOW the middle line, where the beam clears the staff but only barely.
% ★ IT EARNED ITS KEEP as the bracket after all: it is the register where the forbidden-quant
% term FIRES on both engines and the same configuration still wins both, so it separates "the
% term is scaled wrong" (K does not move) from "the term is computed wrong" (K moves).
\score { \sweep "K" { \time 4/4 \grace { a'16 b' } c''4 g'2 r4 } }
