\version "2.26.0"

music = \relative c' {
  a'8 ( b'4-\fermata )
}

\score {
    \new Staff { \music }
  \layout { indent = 0\mm }
}
