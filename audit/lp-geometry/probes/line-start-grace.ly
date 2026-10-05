\version "2.26.0"
%
% A GRACE RUN OPENING A LINE: WHERE DOES ITS FIRST GRACE STAND?
%
% The first grace column is the prefatory column's right neighbour, so the prefix's
% Staff_spacing wish ends THERE (min_dist to the grace head), and because that column has a
% grace part the whole spring is scaled by 0.8 (lily/spacing-spanner.cc:519-527), column
% origin to column origin; Spring::operator*= keeps it at its minimum, and the column rod
% (min_dist + 0.1) floors it. Lily# hung the run off the main note's spring until session 832
% and drew the first grace 0.80 (first line) and 1.08 (a continuation line) too far left.
%
% LSG1: one line in 4/4 — the meter's wish 8.585 x 0.8 = 6.868 falls below the rod, so the
%       first grace stands at the meter's ink right + 0.8 + 0.1 + 0.1.
% LSG2: a continuation line (clef only) — the clef's plain 5.8 x 0.8 = 4.64 from the column.
% LSG3: mid-line, a bar opening with a grace — the bar line before it stands at the grace's
%       moment, so the whole note's spring INTO it is scaled too (spacing-spanner.cc:396-403):
%       6.06 x 0.8 = 4.848.  LSG4: the same after a half note, 4.115 x 0.8.
%
% Bodies are what `lysc ly` emitted for the .lys books LSG1/LSG2 (LpGeometryProbes.cs),
% ragged-right added (the twin carries no paper block).
%
% Output: PROBELSG <tag> <grob> x=<refpoint x in the system> fs=<font-size>

\paper {
  property-defaults.fonts.serif = "LilyPond Serif"
  property-defaults.fonts.sans = "LilyPond Sans Serif"
}

#(define (dump tag name)
   (lambda (grob)
     (let ((sys (ly:grob-system grob)))
       (format #t "\nPROBELSG ~a ~a x=~a fs=~a\n" tag name
               (ly:grob-relative-coordinate grob sys X)
               (ly:grob-property grob 'font-size 0)))))

#(define (probe tag)
   #{
     \override Staff.Clef.after-line-breaking = #(dump tag "CLEF")
     \override Staff.TimeSignature.after-line-breaking = #(dump tag "TIME")
     \override NoteHead.after-line-breaking = #(dump tag "HEAD")
     \override Staff.BarLine.after-line-breaking = #(dump tag "BAR")
   #})

\score {
  \new Staff { \clef "treble" \fixed c' {
    $(probe "LSG1")
    \time 4/4
    \key c \major
    \grace { d'16 e' } f'4 g'2 r4 |
  } }
  \layout { indent = 15\mm ragged-right = ##t \context { \Score printInitialRepeatBar = ##t } }
}

\score {
  \new Staff { \clef "treble" \fixed c' {
    $(probe "LSG2")
    \time 4/4
    \key c \major
    c'1 |
    \break
    \grace { d'16 e' } f'4 g'2 r4 |
  } }
  \layout { indent = 15\mm ragged-right = ##t \context { \Score printInitialRepeatBar = ##t } }
}

\score {
  \new Staff { \clef "treble" \fixed c' {
    $(probe "LSG3")
    \time 4/4
    \key c \major
    c'1 |
    \grace { d'16 e' } f'4 g'2 r4 |
  } }
  \layout { indent = 15\mm ragged-right = ##t \context { \Score printInitialRepeatBar = ##t } }
}

\score {
  \new Staff { \clef "treble" \fixed c' {
    $(probe "LSG4")
    \time 4/4
    \key c \major
    c'2 c' |
    \grace { d'16 e' } f'4 g'2 r4 |
  } }
  \layout { indent = 15\mm ragged-right = ##t \context { \Score printInitialRepeatBar = ##t } }
}
