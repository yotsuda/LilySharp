\version "2.26.0"
%
% A GRACE RUN OPENING A BAR THAT OPENS WITH A KEY CHANGE
%
% The bar line's column holds the new key signature, and the next column is the FIRST GRACE's,
% so Staff_spacing's min_dist is the key box's reach to the grace column's left side — the
% grace head and its accidental, not the main note's (lily/paper-column.cc minimum_distance).
% The spring into a grace column is scaled by 0.8 against its own minimum
% (lily/spacing-spanner.cc breakable_column_spacing, lily/spring.cc Spring::operator*=), and the
% column rod (min_dist + 0.1, set_column_rods) floors it: with a sharp on the grace that rod is
% what places the grace. Lily# read the MAIN note's reach until session 840 (a natural on it
% pushed the run 0.92 right) and floored the approach-plus-run total instead of the approach.
%
% KG1:  `c'1 | \key d \major \grace { d'16 e' } f'4 g'2 r4 |` — a natural on the main note.
% KG10: the same with a sharp on the first grace (`dis'16`).
%
% Body is what `lysc ly` emitted for the .lys books KG1 / KG10 (LpGeometryProbes.cs), indent 0.
%
% Output: PROBEKG <score> <grob> x=<refpoint x in the system>

\paper {
  property-defaults.fonts.serif = "LilyPond Serif"
  property-defaults.fonts.sans = "LilyPond Sans Serif"
  ragged-right = ##t
}

#(define (dump score name)
   (lambda (grob)
     (format #t "\nPROBEKG ~a ~a x=~a\n" score name
             (ly:grob-relative-coordinate grob (ly:grob-system grob) X))))

kgOne = \fixed c' {
  \time 4/4
  \key c \major
  \mark \markup \box "A" c'1 |
  \key d \major \grace { d'16 e' } f'4 g'2 r4 |
}

kgTen = \fixed c' {
  \time 4/4
  \key c \major
  \mark \markup \box "A" c'1 |
  \key d \major \grace { dis'16 e' } f'4 g'2 r4 |
}

\score {
  \new Staff \with {
    \override Clef.after-line-breaking = #(dump "KG1" "CLEF")
    \override BarLine.after-line-breaking = #(dump "KG1" "BAR")
  } { \kgOne }
  \layout { indent = 0 \context { \Score printInitialRepeatBar = ##t } }
}

\score {
  \new Staff \with {
    \override Clef.after-line-breaking = #(dump "KG10" "CLEF")
    \override BarLine.after-line-breaking = #(dump "KG10" "BAR")
  } { \kgTen }
  \layout { indent = 0 \context { \Score printInitialRepeatBar = ##t } }
}
