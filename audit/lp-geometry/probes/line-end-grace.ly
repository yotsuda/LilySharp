\version "2.26.0"
%
% A LINE ENDING BEFORE A BAR THAT OPENS WITH A GRACE RUN
%
% The spring from the bar's last note into the bar line's column is scaled by 0.8 when the
% next column has a grace part (lily/spacing-spanner.cc musical_column_spacing,
% lily/spring.cc Spring::operator*= — max (min_distance, 0.8 × distance)). A spring runs to the
% column's ORIGIN, and at a line end that origin is the end-of-line break-align group's right
% edge: the bar line's own, or past a courtesy key or meter, the group's right-edge member
% (0.5 after the signature). So the scale is taken against a spring O longer than the one to
% the bar line: the bar line stands 0.2 × O further left than the bar-line-framed scale puts
% it. Lily# scaled the bar-line-framed spring until session 842: +0.038 after a plain bar line,
% +0.77 with a courtesy D major, +0.61 with a courtesy 3/4.
%
% LEG1: `c'1 | \break \grace { d'16 e' } f'4 g'2 r4 |` — a plain bar line ends the line.
% LEG2: the same with `\key d \major` opening the second line (a courtesy key at the end).
% LEG3: the same with `\time 3/4` opening the second line (a courtesy meter at the end).
%
% Body is what `lysc ly` emitted for the .lys books LEG1 / LEG2 / LEG3 (LpGeometryProbes.cs),
% indent 0.
%
% Output: PROBELEG <score> <grob> x=<refpoint x in the system>

\paper {
  property-defaults.fonts.serif = "LilyPond Serif"
  property-defaults.fonts.sans = "LilyPond Sans Serif"
  ragged-right = ##t
}

#(define (dump score name)
   (lambda (grob)
     (format #t "\nPROBELEG ~a ~a x=~a\n" score name
             (ly:grob-relative-coordinate grob (ly:grob-system grob) X))))

legOne = \fixed c' {
  \time 4/4
  \key c \major
  \mark \markup \box "A" c'1 |
  \break
  \grace { d'16 e' } f'4 g'2 r4 |
}

legTwo = \fixed c' {
  \time 4/4
  \key c \major
  \mark \markup \box "A" c'1 |
  \break
  \key d \major \grace { d'16 e' } f'4 g'2 r4 |
}

legThree = \fixed c' {
  \time 4/4
  \key c \major
  \mark \markup \box "A" c'1 |
  \break
  \time 3/4 \grace { d'16 e' } f'4 g'2 |
}

\score {
  \new Staff \with {
    \override Clef.after-line-breaking = #(dump "LEG1" "CLEF")
    \override BarLine.after-line-breaking = #(dump "LEG1" "BAR")
  } { \legOne }
  \layout { indent = 0 \context { \Score printInitialRepeatBar = ##t } }
}

\score {
  \new Staff \with {
    \override Clef.after-line-breaking = #(dump "LEG2" "CLEF")
    \override BarLine.after-line-breaking = #(dump "LEG2" "BAR")
  } { \legTwo }
  \layout { indent = 0 \context { \Score printInitialRepeatBar = ##t } }
}

\score {
  \new Staff \with {
    \override Clef.after-line-breaking = #(dump "LEG3" "CLEF")
    \override BarLine.after-line-breaking = #(dump "LEG3" "BAR")
  } { \legThree }
  \layout { indent = 0 \context { \Score printInitialRepeatBar = ##t } }
}
