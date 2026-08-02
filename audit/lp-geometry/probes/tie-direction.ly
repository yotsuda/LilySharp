\version "2.26.0"

%% WHICH SIDE DOES A TIE CURVE TO?
%%
%% Lily# decides this when it COLLECTS the tie (Svg/Collector/TieDetector.cs): "a single
%% voice curves opposite the stem", read off the FIRST note only. LilyPond decides it when
%% it PLACES the tie, and the answer is the winner of a scored search over configurations:
%%
%%   tie-formatting-problem.cc:1026-1045 set_ties_config_standard_directions + :964-966
%%       the BASE configuration is (position + dir, dir) where dir is sign(position), or
%%       neutral-direction (UP for Tie, define-grobs.scm:3899) on the middle line. Nothing
%%       here reads a stem: Tie::get_default_dir exists but tie.cc:203-208 only calls it for
%%       a BROKEN piece (me->original ()).
%%   tie-formatting-problem.cc:1120-1151 generate_single_tie_variations
%%       variations walk out from the BASE position, both directions, single-tie-region-size
%%       (4) steps -- so for a note on the middle line the candidates are
%%       (P1,u) (P1,d) (P0,d) (P2,u) (P-1,d) (P3,u) (P-2,d) (P4,u).
%%   tie-formatting-problem.cc:974-1001 find_best_variation
%%       strictly-less, so the BASE wins ties.
%%   tie-formatting-problem.cc:665-683 score_aptitude, horizontal distance
%%       10 * convex_amplifier (1.25, 1.0, dist) per END, where dist is from the head's X
%%       extent to the attachment. A configuration inside the head's one-space box attaches
%%       at the head's INNER EDGE and is then inset by note-head-gap 0.2, which lands it
%%       0.2 OUTSIDE the head: 1.01 per end. One that clears the box attaches at the head
%%       CENTRE, which stays inside the head: 0.
%%   tie-formatting-problem.cc:685-718 score_aptitude, direction
%%       same-dir-as-stem-penalty 8 is charged to a configuration whose direction equals the
%%       stem's -- but ONLY when the two bounds AGREE about the stem direction (:705-708).
%%       Left stem down and right stem up fires no branch at all, and the position branch
%%       (:709) is skipped because position 0 is false.
%%
%% ⚠️ THE LAST TWO BOOKS ARE THE PAIR THAT MATTERS, and they are the same music.
%% TDBEAM and TDBEAMD differ ONLY in which way the SECOND note's beam points, and LilyPond
%% answers them oppositely. Any rule that reads the FIRST note's stem must give them the
%% same answer, so no such rule can be right -- which is the claim, stated as a pair rather
%% than as five numbers (HANDOFF 5.0: the strongest pair is one where one side is held
%% fixed).
%%
%%   TDMID   d4~ d2.                position  0, both stems down     LP UP
%%   TDUP    e4~ e2.                position +1, both stems down     LP UP
%%   TDDN    c4~ c2.                position -1, both stems up       LP DOWN
%%   TDFRC   \stemUp d4~ d2.        position  0, both stems UP       LP DOWN
%%   TDBEAM  d4~ d8. a,16 d8 d b,4  position  0, stems DISAGREE      LP DOWN
%%   TDBEAMD ...same, beam forced down                               LP UP
%%
%% ⚠️ TDBEAM IS DECIDED BY 0.02 AND THAT IS NOT A FLAW IN THE PROBE, it is the quantity.
%% MEASURED (debug-tie-scoring): the winner (P0,d) scores lhdist 1.01 + rhdist 1.01 = 2.02
%% and the base (P1,u) scores tipline 0.02 + 2.02 = 2.04. TDBEAMD's margin is 8 (the stem
%% penalty), so the two books bound the mechanism from both sides: a port that ignores the
%% horizontal distance loses TDBEAM, and one that ignores the stems loses TDBEAMD.
%% ⚠️ AND TDBEAM THEREFORE READS THE HORIZONTAL SPACING TOO. The same bar JUSTIFIED instead
%% of ragged reads UP (measured: `1 (0.25) u: vdist=1.21 TOTAL=1.21` -- a wide tie clears
%% the head box, pays no hdist at all, and the base wins). That is not noise: it is why the
%% user's own score prints this bar DOWN in one system and UP in another (repro.lys bars 11
%% and 26, both `d,4~ d,8. a,,16`, measured dir -1 and +1). Both engines must space the bar
%% alike for this point to mean what it says.
%%
%% ⚠️ THE MUSIC CAME OUT OF `lysc ly` (HANDOFF 6 -- hand-written twins have produced three
%% false divergences in this repo), from the .lys the Lily# side renders. TWO edits were
%% made by hand: `\stemUp` in TDFRC and `\stemDown` in TDBEAMD, because `lysc ly` drops the
%% `@stemUp`/`@stemDown` annotations ("warning: articulation @stemUp not mapped, dropped")
%% -- an exporter hole, recorded in HANDOFF 1.
%%
%% ⚠️ `\fixed c'` is LOAD-BEARING and is what `lysc ly` emits: Lily#'s absolute `c` is
%% LilyPond's `c'` (HANDOFF 6). Without it these are D2/E2/C2, seven half-spaces lower,
%% where the position branch alone decides every book and the pair measures nothing
%% (measured 2026-08-03: all five read pos -7..-8, dir -1).
%%
%% ⚠️ `\bar "|."` because LilyPond does not end a score with a final bar line on its own
%% and Lily# always draws one (HANDOFF 6).

#(define ((dump-tie tag) g)
   (format #t "\nPROBE ~a TIE pos=~a dir=~a card=~s\n"
           tag
           (ly:grob-property (ly:spanner-bound g LEFT) 'staff-position)
           (ly:grob-property g 'direction)
           (ly:grob-property g 'annotation))
   '())

dump = #(define-music-function (tag) (string?)
          #{ \override Tie.after-line-breaking = #(dump-tie tag) #})

\paper {
  indent = 0
  ragged-right = ##t
}

\score { \new Staff { \clef bass \time 4/4 \key c \major \dump "TDMID"
  \fixed c' { d,4 ~ d,2. \bar "|." } } \layout { debug-tie-scoring = ##t } }

\score { \new Staff { \clef bass \time 4/4 \key c \major \dump "TDUP"
  \fixed c' { e,4 ~ e,2. \bar "|." } } \layout { debug-tie-scoring = ##t } }

\score { \new Staff { \clef bass \time 4/4 \key c \major \dump "TDDN"
  \fixed c' { c,4 ~ c,2. \bar "|." } } \layout { debug-tie-scoring = ##t } }

\score { \new Staff { \clef bass \time 4/4 \key c \major \dump "TDFRC"
  \fixed c' { \stemUp d,4 ~ d,2. \bar "|." } } \layout { debug-tie-scoring = ##t } }

\score { \new Staff { \clef bass \time 4/4 \key c \major \dump "TDBEAM"
  \fixed c' { d,4 ~ d,8. a,,16 d,8 d, b,,4 \bar "|." } }
  \layout { debug-tie-scoring = ##t } }

\score { \new Staff { \clef bass \time 4/4 \key c \major \dump "TDBEAMD"
  \fixed c' { d,4 ~ \stemDown d,8. a,,16 \stemNeutral d,8 d, b,,4 \bar "|." } }
  \layout { debug-tie-scoring = ##t } }
