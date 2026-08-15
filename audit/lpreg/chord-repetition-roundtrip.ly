\version "2.26.0"

v = \relative c' {
  \time 4/4
  <c e g>8\p ( q ) q4-\staccatissimo q8. ( q16 q4-\staccatissimo ) |
}

\score {
    \new Staff { \clef treble \v }
  \layout { indent = 0\mm }
}
