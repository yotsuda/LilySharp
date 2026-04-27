\version "2.24.4"
\paper {
  ragged-right = ##f
  paper-width = 100\mm
  paper-height = 200\mm
  top-margin = 5\mm
  bottom-margin = 5\mm
  left-margin = 5\mm
  right-margin = 5\mm
}
\score {
  \new Staff \relative c' {
    \time 4/4
    \clef treble
    \key c \major
    c4 d e f | g4 a b c | d4 c b a | g4 f e d |
    c4 d e f | g4 a b c | d4 c b a | g4 f e d |
    c4 d e f | g4 a b c | d4 c b a | g4 f e d \bar "|."
  }
  \layout {}
}
