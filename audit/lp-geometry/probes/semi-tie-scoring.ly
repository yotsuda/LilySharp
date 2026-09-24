\version "2.26.0"

%% LAISSEZ-VIBRER AND REPEAT TIES GO THROUGH THE TIE SCORER (HANDOFF R9(f), session 573).
%%
%% Semi_tie_column::calc_positioning_done runs the SAME Tie_formatting_problem as a tie column
%% (from_semi_ties): the host heads are the bound on the tie's head-direction side, the open
%% side a flat outline at extremal - head_dir * 1.5, and the details are the grob's two
%% (height-limit 1.0, ratio 0.333) over Tie_details' C++ fallbacks. Lily# drew these at a fixed
%% 0.4 ss off the head centre with its own bow shape until then.
%%
%% Readings (control-points: [0].y, [3].x - [0].x, [1].y - [0].y), Lily# now equal to 6 digits:
%%   LV c''2   pos 1   1.200000  1.788700  0.478835   (clears the head: attaches at its centre)
%%   LV d''2   pos 2   1.341174  1.100000  0.332393   (centred between lines: 3 (-0.16))
%%   LV f''2   pos 4   2.500000  1.100000  0.332393   (no head-edge hug on a semi-tie)
%%   LV c''2.  pos 1   1.200000  2.688700  0.606508   (the dot is in the outline)
%%   RTA <c'' ees''>2 lower tie, pos 8: 3.500000 2.370000 -0.567872
%% The music came out of `lysc ly`; the dumps were added by hand.

#(define (dump-lv g)
   (let* ((head (ly:grob-object g 'note-head))
          (loc (grob::rhythmic-location head)))
     (format #t "\nLV bar=~a pos=~a dir=~a cps=~a card=~s\n" (car loc)
             (ly:grob-property head 'staff-position) (ly:grob-property g 'direction)
             (ly:grob-property g 'control-points) (ly:grob-property g 'annotation)))
   '())

melody = \fixed c' {
  \time 4/4
  c'2\laissezVibrer r2 |
  d'2\laissezVibrer r2 |
  e'2\laissezVibrer r2 |
  f'2\laissezVibrer r2 |
  g'2\laissezVibrer r2 |
  a'2\laissezVibrer r2 |
  b'2\laissezVibrer r2 |
  c''2\laissezVibrer r2 |
  d''2\laissezVibrer r2 |
  g4\laissezVibrer r2. |
  e''4\laissezVibrer r2. |
  <c' e' g'>2\laissezVibrer r2 |
  c'2.\laissezVibrer r4 |
}

\score {
    \new Staff \with { \override LaissezVibrerTie.after-line-breaking = #dump-lv } { \clef "treble" \melody }
  \layout { debug-tie-scoring = ##t indent = 0\mm \context { \Score printInitialRepeatBar = ##t } }
}

%% ---- repeat ties on accidentals (book RTA) ----
melodyRta = \fixed c' {
  \time 4/4
  fis''2\repeatTie r2 |
  bes'2\repeatTie r2 |
  <c'' ees''>2\repeatTie r2 |
}

\score {
    \new Staff \with { \override RepeatTie.after-line-breaking = #dump-lv } { \clef "treble" \melodyRta }
  \layout { debug-tie-scoring = ##t indent = 0\mm \context { \Score printInitialRepeatBar = ##t } }
}
