\version "2.26.0"

%% A LOW TIE'S TOP PAYS FOR A LEDGER LINE'S POSITION (HANDOFF R9(c), session 572).
%%
%% score_configuration's "line center" term asks Staff_symbol_referencer::on_line, whose
%% allow_ledger defaults to true (lily/staff-symbol.cc:372-396), so below the staff every
%% EVEN position counts as a line. Lily# asked the five-line predicate (|pos| <= 4) and never
%% charged a down tie under the staff for a top sitting on a ledger's position.
%%
%% Four bars joined with \noBreak, so the line is compressed and the pos -4 tie is short
%% (1.180300). There the base (-5, -0.25) pays line center for a top near -6 and loses to
%% (-6, 0.00), in all four bars:
%%
%%   PROBE TIE loc=(1 . 3/8) pos=-4 dir=-1 cps=((0.852100 . -3.0) ... (2.032400 . -3.0))
%%     card="-6 (0.00) d: vdist=2.73 rhdist=1.79 TOTAL=4.51"
%%
%% Lily# before the fix drew the tip at -2.75 (the base). The same bar in a wider line keeps
%% the base in both engines (a 1.97 tie: line center 0.52, TOTAL 1.73) — the ledger term is a
%% penalty, not a rule. The music came out of `lysc ly`; the dump and the final bar line were
%% added by hand.

#(define (dump-tie g)
   (format #t "\nPROBE TIE loc=~a pos=~a dir=~a cps=~a card=~s\n"
           (grob::rhythmic-location (ly:spanner-bound g LEFT))
           (ly:grob-property (ly:spanner-bound g LEFT) 'staff-position)
           (ly:grob-property g 'direction)
           (ly:grob-property g 'control-points)
           (ly:grob-property g 'annotation))
   '())

bassline = \fixed c' {
  \time 4/4
  \key d \major
  \mark \markup \box "Main" \clef "bass" g,,8 g,,16 g,, r8 g,,8 ~ g,, a, a,16 a, a, a, |
  \noBreak
  g,,8 g,,16 g,, r8 g,,8 ~ g,, a, a,16 a, a, a, |
  \noBreak
  g,,8 g,,16 g,, r8 g,,8 ~ g,, a, a,16 a, a, a, |
  \noBreak
  g,,8 g,,16 g,, r8 g,,8 ~ g,, a, a,16 a, a, a, |
}

\score {
    \new Staff \with { \override Tie.after-line-breaking = #dump-tie } { \bassline \bar "|." }
  \layout { debug-tie-scoring = ##t indent = 0\mm \context { \Score printInitialRepeatBar = ##t } }
}
