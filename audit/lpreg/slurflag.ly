\version "2.26.0"

music = \relative {
  b'8 ( c ) \bar "|."
}

\score {
    \new Staff { \music }
  \layout { indent = 0\mm }
}
