\version "2.26.0"
%
% A MID-BAR REHEARSAL MARK'S OWN COLUMN
%
% A \mark later in the bar is a RehearsalMark in the NON-musical paper column of its moment
% (scm/define-grobs.scm RehearsalMark: non-musical), a column that otherwise stays unused. So
% the mark alone puts a column between the previous note's and its own note's: the previous
% note -> the mark column is the bare duration spring (no Note_spacing wish names the mark
% column — lily/spacing-spanner.cc musical_column_spacing), the mark column -> the note is
% standard_breakable_column_spacing's dt == 0 spring, min_dist + 0.5 (lily/spacing-basic.cc),
% and a long first note before a mark over half a bar on earns full-measure-extra-space
% (lily/spacing-spanner.cc fills_measure). The mark is centred on its column. Lily# drew the
% mark over the bar line and spaced the bar as if it were not there until session 838.
%
% MKC: four bars on a justified line, then a break. Bar 2 `r2. a4` carries "Tacet" on its
%      fourth beat (the rest fills more than half the bar before the mark column), bar 4
%      `c4 d4 e4 f4` carries "X" on its second beat.
%
% Body is what `lysc ly` emitted for the .lys book MKC (LpGeometryProbes.cs), indent 0.
%
% Output: PROBEMKC <grob> x=<refpoint x in the system> ext=<X extent in the system> sys=<rank>

\paper {
  property-defaults.fonts.serif = "LilyPond Serif"
  property-defaults.fonts.sans = "LilyPond Sans Serif"
}

#(define (dump name)
   (lambda (grob)
     (let ((sys (ly:grob-system grob)))
       (format #t "\nPROBEMKC ~a x=~a ext=~a sys=~a\n" name
               (ly:grob-relative-coordinate grob sys X)
               (ly:grob-extent grob sys X)
               (car (ly:grob-spanned-column-rank-interval sys))))))

pVarTwo = \fixed c' {
  \time 4/4
  \mark \markup \box "A" c'1 |
  r2. \mark \markup { \box "Tacet" } a4 |
  c'1 |
  c'4 \mark \markup { \box "X" } d'4 e'4 f'4 |
  \break
  c'1 |
}

\layout {
  \context {
    \Score
    \override RehearsalMark.after-line-breaking = #(dump "MARK")
  }
}

\score {
  \new Staff \with {
    \override Clef.after-line-breaking = #(dump "CLEF")
    \override BarLine.after-line-breaking = #(dump "BAR")
  } { \pVarTwo }
  \layout { indent = 0 \context { \Score printInitialRepeatBar = ##t } }
}
