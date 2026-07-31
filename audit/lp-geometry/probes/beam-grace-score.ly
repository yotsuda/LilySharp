\version "2.26.0"
%
% WHY DOES LILYPOND GIVE THE GRACE BEAM THAT SLOPE?
%
% beam-grace.ly measured WHAT LilyPond answers; grace-column-width.ly closed the x frame the
% quanter fits over (to 0.004). beam.quant.grace.* is still SYMMETRIC at +-0.019015 — the
% midpoint exact to nine places and only dy short, 0.319971 against 0.358 — and a 46% wider
% span moved dy by 5.6%, i.e. nowhere near proportionally. So the span was not the term.
%
% LilyPond can be asked directly (HANDOFF 5.3): `debug-beam-scoring` makes the winning
% configuration write its scorecard into Beam.annotation, and `inspect-quants` FORCES a
% configuration to be scored even if LilyPond would not have chosen it
% (lily/beam-quanting.cc:1038-1043 force_score). Putting Lily#'s answer through
% inspect-quants and LilyPond's own answer beside it turns "the slope is short" into "this
% scorer charged this much", term by term — `add` only writes NON-ZERO terms, so a term that
% is missing from a card is a penalty of zero, not a penalty that was not considered.
%
% Output: PROBEGS <name> pos=<positions> ann=<the scorecard, newlines folded to |>
%                        fract=<length-fraction> thick=<beam-thickness> info=<per-stem stem-info>
%
% WHAT IT SAID (2026-08-01, session 59):
%
%   GBSG  grace, LilyPond's own answer   (0.142 . 0.5)   Si 0.65  Fl 0.82  L 0.58   c1/130
%   GBSH  full size, the control         (0.81  . 1.0)   Si 1.21           L 1.13   c1/143
%
% ★ THE GRACE PAYS A TERM THE CONTROL DOES NOT. `Fl` is score_forbidden_quants — a staff
%   LINE falling inside the gap between two beam lines at a beam end — and `add` only writes
%   non-zero terms, so the control is not merely cheaper there, it is charged NOTHING. That
%   is why the control has been exact all along and says nothing about this: the scorer that
%   decides the grace's slope never fires on it. Fl reads beam_translation_, beam_thickness_
%   and line_thickness_ (:1287-1294), the first two of which the grace scales and the third
%   of which it does NOT.
%
% ★ AND LILY#'S ANSWER IS NOT ON LILYPOND'S GRID. GBSL asks for Lily#'s
%   (0.161014715 . 0.480985285) and gets GBSG's card back verbatim, because force_score
%   scores the nearest GENERATED configuration and among the 130 LilyPond generated, the
%   nearest to Lily#'s answer is LilyPond's own. Grid points read out the same way:
%     (0.0 . 0.0)  Si 3.15  Sd 800.00  Fl 1.82  L 2.69      <- Sd = the flat-beam demerit
%     (1.0 . 1.0)  Si 3.15  Sd 800.00  Fl 1.82  L 3.21
%     (0.0 . 1.5)  Si 17.78 Sm 381.66  Fl 1.31  L 2.24
%     (0.30 . 0.35) snaps to (0.142 . 0.5) — nothing of LilyPond's lies between.
%   ⚠️ So the remaining +-0.019015 is NOT "one quant away". Lily#'s pair is LilyPond's pair
%   ROTATED ABOUT THE SAME MIDPOINT (0.321 to nine places on both sides), which is the shape
%   a difference in the projection from the stems to the DRAWN ends makes, not the shape a
%   different quant makes. BeamScoringProblem.Solve ends in AtOuterStems(...), and
%   beam-grace.ly already measured that LilyPond does not scale that overhang with the grace
%   (its drawn extent is half an UNSCALED stem thickness outside each stem) while Lily#'s
%   renderer does. ⚠️ THAT EXPLAINS THE SHAPE BUT NOT YET THE SIZE: half a stem thickness
%   unscaled minus scaled is 0.065 * (1 - magstep(-3)) = 0.019038 in x, and at this slope
%   (0.358 staff positions over 1.417939) that buys only 0.0048 staff positions, not 0.019.
%   Do not stop at the coincidence that 0.019038 and 0.019015 look alike — they are in
%   DIFFERENT UNITS (staff spaces of x against staff positions of y).
%
% ★★★ ANSWERED 2026-08-01 (session 60), by reading Lily#'s own chosen configuration out of
%   BeamScoringProblem BEFORE AtOuterStems: it is (0.142000000 . 0.500000000), LilyPond's
%   answer to nine places. IT IS THE PROJECTION, NOT THE LATTICE, and the whole +-0.019014715
%   is two terms in the grace RENDERER — see the ledger key beam.quant.grace.left. The
%   0.019038 warned against above is genuinely in it, but as term B's x times the drawn slope,
%   which is 0.004. The warning was right and reading the units is what kept the diagnosis
%   honest.
%
% ⚠️ AND THE SAME READING LEFT A SECOND, SEPARATE DIVERGENCE STANDING — the GRID SIZE.
%   LilyPond generated 130 configurations for the grace beam and 143 for the control (both
%   printed on the cards as c1/130 and c1/143). Lily# generates 143 for the control — exact —
%   and 120 for the grace. The counts factor: Lily#'s 120 is 12 left quants x 10 right, and
%   130 would be 13 x 10, so LilyPond admits ONE more left quant, i.e. its lower bound on the
%   left edge sits about 0.02 below Lily#'s -1.1216 (quant_range_, lily/beam-quanting.cc:343-360
%   against GenerateQuantCandidates' 0.5 + (edge_beam_count-1)*beam_translation + thickness/2).
%   It does NOT change this answer — the winner is identical and force_score confirmed nothing
%   of LilyPond's lies between — so it is recorded here as a lead rather than chased: it can
%   only bite in a regime where that outermost candidate wins.
\paper { indent = 0 ragged-right = ##t }
\layout { debug-beam-scoring = ##t }

#(define (fold-lines s)
   (if (string? s) (string-join (string-split s #\newline) " | ") (format #f "~a" s)))

#(define (dump-score name)
   (lambda (grob)
     (let ((stems (ly:grob-array->list (ly:grob-object grob 'stems))))
       (format #t "PROBEGS ~a pos=~a ann=[~a] fract=~a thick=~a info=~a\n" name
               (ly:grob-property grob 'positions)
               (fold-lines (ly:grob-property grob 'annotation))
               (ly:grob-property grob 'length-fraction)
               (ly:grob-property grob 'beam-thickness)
               (map (lambda (s) (ly:grob-property s 'stem-info)) stems)))))

sweep =
#(define-music-function (name music) (string? ly:music?)
   #{ \new Staff \with { \override Beam.after-line-breaking = #(dump-score name) }
      { $music } #})

% GBSG — the corpus regime, score G of beam-grace.ly. LilyPond answers (0.142 . 0.5).
\score { \sweep "GBSG" { \time 4/4 \grace { d'16 e' } f'4 g'2 r4 } }

% GBSL — the SAME music with Lily#'s answer forced. LilyPond would not choose
% (0.161014715 . 0.480985285); this makes it score it anyway, so the two cards differ by
% exactly the terms that separate the two slopes.
\score {
  \new Staff \with {
    \override Beam.after-line-breaking = #(dump-score "GBSL")
    \override Beam.inspect-quants = #'(0.161014715 . 0.480985285)
  }
  { \time 4/4 \grace { d'16 e' } f'4 g'2 r4 }
}

% GBSF — LilyPond's own answer, forced through the same path as GBSL. A control for the
% instrument itself: force_score has to reproduce the card the free run wrote, or the two
% cards are not comparable (HANDOFF 5.0 — the first divergence a new instrument reports is
% usually its own).
\score {
  \new Staff \with {
    \override Beam.after-line-breaking = #(dump-score "GBSF")
    \override Beam.inspect-quants = #'(0.142 . 0.5)
  }
  { \time 4/4 \grace { d'16 e' } f'4 g'2 r4 }
}

% GBSH — the FULL-SIZE control, score H. Its beam is already exact in Lily#, so its card is
% the vocabulary: whatever term is charged here and not there (or charged differently) is
% the grace scaling.
\score { \sweep "GBSH" { \time 4/4 d'16 e' r8 g'2 r4 } }

% --- READING THE GRID ---------------------------------------------------------------
% force_score does NOT score the pair it is given: it walks the GENERATED configurations
% and scores the nearest one (lily/beam-quanting.cc — Beam_scoring_problem::force_score,
% `mindist`). That is why GBSL came back with GBSG's card unchanged, and it is a finding
% rather than a failure: among the 130 configurations LilyPond generated for this beam, the
% one NEAREST to Lily#'s answer is LilyPond's own. Lily# is not one quant away from the
% grid, it is off it.
% So the same lever reads the grid out: ask for a pair, get the nearest grid point back.
% Four probes around the answer map the neighbourhood the port has to reproduce.
\score {
  \new Staff \with { \override Beam.after-line-breaking = #(dump-score "GBSQ-flat0")
                     \override Beam.inspect-quants = #'(0.0 . 0.0) }
  { \time 4/4 \grace { d'16 e' } f'4 g'2 r4 }
}
\score {
  \new Staff \with { \override Beam.after-line-breaking = #(dump-score "GBSQ-flat1")
                     \override Beam.inspect-quants = #'(1.0 . 1.0) }
  { \time 4/4 \grace { d'16 e' } f'4 g'2 r4 }
}
\score {
  \new Staff \with { \override Beam.after-line-breaking = #(dump-score "GBSQ-steep")
                     \override Beam.inspect-quants = #'(0.0 . 1.5) }
  { \time 4/4 \grace { d'16 e' } f'4 g'2 r4 }
}
\score {
  \new Staff \with { \override Beam.after-line-breaking = #(dump-score "GBSQ-shallow")
                     \override Beam.inspect-quants = #'(0.30 . 0.35) }
  { \time 4/4 \grace { d'16 e' } f'4 g'2 r4 }
}
