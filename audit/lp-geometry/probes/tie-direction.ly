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
%% ⚠️ THE LAST TWO BOOKS ARE THE PAIR THAT MATTERS. TDBEAM and TDBEAMD hold the TIE fixed --
%% same two notes, same position, same first stem, same bar shape -- and change only which
%% way the beam on the second note points, by putting the sixteenth it is beamed to below
%% the beam's centre of gravity in one and above it in the other. LilyPond answers them
%% oppositely. Any rule that reads the FIRST note's stem must give them the same answer, so
%% no such rule can be right -- which is the claim, stated as a pair rather than as six
%% numbers (HANDOFF 5.0: the strongest pair is one where everything but the quantity is held
%% fixed).
%%
%%   TDMID   d4~ d2.                position  0, both stems down     LP UP
%%   TDUP    e4~ e2.                position +1, both stems down     LP UP
%%   TDDN    c4~ c2.                position -1, both stems up       LP DOWN
%%   TDFRC   \stemUp d4~ d2.        position  0, both stems UP       LP DOWN
%%   TDBEAM  d4~ d8. a,16 d8 d b,4  position  0, stems DISAGREE      LP DOWN
%%   TDBEAMD d4~ d8. b16  d8 d b,4  position  0, both stems down     LP UP
%%
%% ⚠️ THE BEAM IS TURNED BY ITS OWN PITCHES AND NOT BY \stemDown, deliberately. Lily#'s
%% `@stemDown` annotation does NOT reach a beamed note -- BeamDetector takes the group's
%% direction from the pitches (or from a polyphonic voice) and then stamps it over every
%% member's StemUpOverride -- so a book written that way would silently be a book with an UP
%% beam on the Lily# side and a DOWN beam on LilyPond's, i.e. not a twin at all. MEASURED:
%% \stemDown and a higher beamed sixteenth give LilyPond the same card
%% (`1 (-0.29) u: tipline=0.02 conf=0.02 lhdist=1.01 rhdist=1.01 TOTAL=2.04`), so nothing is
%% lost by choosing the spelling both engines can express. The dropped annotation is a
%% separate defect, recorded in HANDOFF 1.
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
%% false divergences in this repo), from the .lys the Lily# side renders. ONE edit was made
%% by hand: `\stemUp` in TDFRC, because `lysc ly` drops the `@stemUp` annotation
%% ("warning: articulation @stemUp not mapped, dropped") -- an exporter hole, recorded in
%% HANDOFF 1. TDFRC's notes are NOT beamed, so the annotation does reach them on the Lily#
%% side (unlike the beamed case above).
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
  \fixed c' { d,4 ~ d,8. b,16 d,8 d, b,,4 \bar "|." } }
  \layout { debug-tie-scoring = ##t } }

%% ...and the SAME notes as TDBEAM with the beamed eighth's stem TURNED BY HAND. The beam
%% does not simply follow it: with a forced stem in the group LilyPond stops using the
%% farthest-head rule and takes a VOTE (beam.cc:894-924), which here comes out UP, while the
%% forced stem KEEPS its own side (:946-956) -- a KNEE, measured `dir=1 stems=(-1 1)`. The
%% tie's two bounds then both point DOWN, so same-dir-as-stem 8 fires and the tie goes UP.
%%
%% ⚠️ THIS BOOK EXISTS BECAUSE THE ANNOTATION USED TO DO NOTHING. Lily#'s `@stemDown` never
%% reached a beamed note (BeamDetector took the group's direction from the pitches and
%% ResolveBeamStemDirections stamped it over every member), so this and TDBEAM were the same
%% engraving on that side and different ones here. It is the pair that says the wish is read.
\score { \new Staff { \clef bass \time 4/4 \key c \major \dump "TDBEAMF"
  \fixed c' { d,4 ~ \once \override Stem.direction = #DOWN d,8. a,,16 d,8 d, b,,4 \bar "|." } }
  \layout { debug-tie-scoring = ##t } }

%% ---------------------------------------------------------------------------------------
%% HOW WIDE THE TIE COMES OUT, which is the same mechanism seen from the other side.
%%
%% The attachment is Y-DEPENDENT: LilyPond reads the column's chord-outline skyline at the
%% tie's own Y (tie-formatting-problem.cc:73-94 get_attachment), and that outline is built
%% from EVERY box the column has -- each head, the dots, the stem, the flag
%% (:96-287 set_column_chord_outline) -- with a recession box added ABOVE the topmost head
%% and BELOW the bottommost one that stands at the head CENTRE rather than its edge
%% (:243-258). So a tie that clears the heads gets the wide centre-to-centre span and one
%% that runs alongside a head, a neighbour head or a stem gets the narrow one.
%%
%% Lily# knows only THIS tie's own head box. That was invisible while every short tie took
%% the narrow attachment anyway; correcting the height quantity (see BezierBow.MidpointHeight)
%% moved several ties out of their own box and the difference became legible in three
%% directions at once, which is what these books hold.
%%
%%   TWCLR  c4~ c4 d2         a single tie that CLEARS its head -> centre attachment
%%   TWSEC  <c d>2~ <c d>2    a chord of tied SECONDS: the lower tie clears everything,
%%                            the upper one runs past the stem

%% ⚠️ THE Y IS READ AS WELL AS THE WIDTH, and the width alone was not enough. LilyPond's front
%% tie CHANGES ITS CHOSEN POSITION with the rest of the column — the same c (head position −6)
%% takes variation −7 in TWSEC and −8 in TW3 — while its WIDTH stays 3.875445 in both. A greedy
%% solve that fixes the front before the others exist cannot follow that, and a corpus of widths
%% cannot see that it did not: measured 2026-08-03, all six triad width points opened EXACT.
%% control-points[0].y is the tie's own attachment height in its staff frame, which is where the
%% chosen position actually shows up.
#(define ((dump-width tag) g)
   (let ((cps (ly:grob-property g 'control-points)))
     (format #t "\nPROBE ~a WIDTH pos=~a dir=~a w=~,6f y=~,6f card=~s\n"
             tag
             (ly:grob-property (ly:spanner-bound g LEFT) 'staff-position)
             (ly:grob-property g 'direction)
             (- (car (cadddr cps)) (car (car cps)))
             (cdr (car cps))
             (ly:grob-property g 'annotation)))
   '())

widths = #(define-music-function (tag) (string?)
            #{ \override Tie.after-line-breaking = #(dump-width tag) #})

\score { \new Staff { \clef treble \time 4/4 \key c \major \widths "TWCLR"
  \fixed c' { c4 ~ c4 d2 \bar "|." } } \layout { debug-tie-scoring = ##t } }

\score { \new Staff { \clef treble \time 4/4 \key c \major \widths "TWSEC"
  \fixed c' { <c d>2 ~ <c d>2 \bar "|." } } \layout { debug-tie-scoring = ##t } }

%% ---------------------------------------------------------------------------------------
%% THREE TIES, WHICH IS WHERE GREEDY AND JOINT CAN FIRST DISAGREE.
%%
%% LilyPond scores a whole Ties_configuration and varies its ties TOGETHER
%% (tie-formatting-problem.cc:915-1001 generate_configuration / find_best_variation), and the
%% column's two symmetry terms read only `ties->front ()` and `ties->back ()` (:890-908). Lily#
%% solves the column ONE TIE AT A TIME against the finished layouts of the ones below it, so
%% the front tie is already fixed by the time the symmetry term exists and only the back one
%% pays — the approximation is written down at TieFormattingProblem.ScoreColumnSymmetry.
%%
%% ⚠️ TWO TIES CANNOT SEPARATE THE TWO READINGS AS SHARPLY AS THREE. With two, front and back
%% are the whole column, so the greedy order differs from the joint search only in WHO pays.
%% With three there is a MIDDLE tie that is in neither symmetry term, and the joint search may
%% move the outer two to suit it while the greedy one cannot move the front at all. If the
%% middle reading is exact and the outer two are not, the defect is the charging; if the middle
%% one moves too, the whole configuration is being chosen differently.
%%
%%   TW3    <c e g>2~ <c e g>2   ordinary thirds — nothing is squeezed
%%   TW3S   <c d g>2~ <c d g>2   the lower pair is a SECOND, so the middle head is reversed
%%                               and its tie runs past a head the outer two do not see
\score { \new Staff { \clef treble \time 4/4 \key c \major \widths "TW3"
  \fixed c' { <c e g>2 ~ <c e g>2 \bar "|." } } \layout { debug-tie-scoring = ##t } }

\score { \new Staff { \clef treble \time 4/4 \key c \major \widths "TW3S"
  \fixed c' { <c d g>2 ~ <c d g>2 \bar "|." } } \layout { debug-tie-scoring = ##t } }
