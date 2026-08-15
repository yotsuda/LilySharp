\version "2.26.0"

music = \relative {
  c''4( \acciaccatura e8 d4 e4 f) |
  c4( \appoggiatura e8 d4 e4 f) |
  c4  \appoggiatura e8 d4 e4 f \bar "|."
}

\score {
    \new Staff { \music }
  \layout { indent = 0\mm }
}
