\version "2.26.0"

music = \relative c''' {
  \time 17/8
  g4. ( f ) g ( a ) g8 ( c8. ) c8. ( g8 ) \bar "|."
}

\score {
    \new Staff { \music }
  \layout { indent = 0\mm }
}
