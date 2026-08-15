\version "2.26.0"

music = \fixed c' {
  << { g4 } \\ { r8 aeses,8 } >> |
}

\score {
    \new Staff { \music }
  \layout { indent = 0\mm }
}
