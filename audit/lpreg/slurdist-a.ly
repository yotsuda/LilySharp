\version "2.26.0"

music = \relative c''' {
  \time 6/4
  g4 ( f ) g ( a ) g8 ( c ) c ( g ) \bar "|."
}

\score {
    \new Staff { \music }
  \layout { indent = 0\mm }
}
