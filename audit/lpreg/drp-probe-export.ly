\version "2.26.0"

music = \fixed c' {
  g2 r2\p |
}

\score {
    \new Staff { \music }
  \layout { indent = 0\mm }
}
