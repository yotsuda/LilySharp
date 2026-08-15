\version "2.26.0"

m = \fixed c' {
  c4 d e f |
  g1 |
}

\score {
    \new Staff { \m }
  \layout { indent = 0\mm }
}
