\version "2.24.4"
\paper {
  ragged-right = ##f
  paper-width = 180\mm
}
\score {
  \new Staff {
    \time 4/4
    \clef treble
    \key c \major
    c'1 | c'2 c'2 | c'4 c'4 c'4 c'4 | c'8 c' c' c' c'8 c' c' c' |
    c'16 c' c' c' c'16 c' c' c' c'16 c' c' c' c'16 c' c' c' |
    c'1 ~ | c'1 \bar "|."
  }
  \layout {}
}
