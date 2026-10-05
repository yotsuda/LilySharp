\version "2.26.0"
%
% A PLAIN WHOLE-BAR REST VOTES FOR THE COMMON SHORTEST DURATION
%
% calc_common_shortest_duration takes the MODE of the bars' shortest durations
% (lily/spacing-spanner.cc), and a bar's shortest is read off the starter durations its
% columns recorded — which add_starter_duration refuses only for a multi-measure-interface grob
% (lily/spacing-engraver.cc). So `r1` votes its whole like any note and only `R1` does not.
% Here three `r1` bars outvote one bar of sixteenths and one of eighths: the mode is the
% whole, capped to base-shortest-duration 3/16. Lily# dropped every full-bar rest from the
% vote until session 839, spaced the book on the sixteenth, and its first line's bar line
% moved with whatever the SECOND line held.
%
% RBV: `r1 | r1 |` on a justified first line, then a line of sixteenths and eighths, then `r1`.
%
% Body is what `lysc ly` emitted for the .lys book RBV (LpGeometryProbes.cs), indent 0.
%
% Output: PROBERBV <grob> x=<refpoint x in the system> sys=<system's first column rank>

\paper {
  property-defaults.fonts.serif = "LilyPond Serif"
  property-defaults.fonts.sans = "LilyPond Sans Serif"
}

#(define (dump name)
   (lambda (grob)
     (let ((sys (ly:grob-system grob)))
       (format #t "\nPROBERBV ~a x=~a sys=~a\n" name
               (ly:grob-relative-coordinate grob sys X)
               (car (ly:grob-spanned-column-rank-interval sys))))))

pVarTwo = \fixed c' {
  \time 4/4
  \mark \markup \box "A" r1 |
  r1 |
  \break
  c'16 d' e' f' g' a' b' c'' c'' b' a' g' f' e' d' c' |
  c'8 d' e' f' g' a' b' c'' |
  \break
  r1 |
}

\score {
  \new Staff \with {
    \override Clef.after-line-breaking = #(dump "CLEF")
    \override BarLine.after-line-breaking = #(dump "BAR")
  } { \pVarTwo }
  \layout { indent = 0 \context { \Score printInitialRepeatBar = ##t } }
}
