\version "2.26.0"

music = \relative c' {
  c4-\tenuto-\accent c-\accent-\tenuto c-\staccato-\tenuto c-\tenuto-\staccato
}

\score {
    \new Staff { \music }
  \layout { indent = 0\mm }
}
