\version "2.26.0"

music = \fixed c' {
  \time 2/4 c8 cis'' cis'' cis |
  \bar "|."
}

\score {
    \new Staff { \music }
  \layout { indent = 0\mm }
}
