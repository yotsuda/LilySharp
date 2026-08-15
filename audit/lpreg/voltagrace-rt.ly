\version "2.26.0"

v = \fixed c' {
  \time 4/4
  \grace { f8 }
  \repeat volta 2 {
    b1
  }
}

\score {
    \new Staff { \v }
  \layout { indent = 0\mm }
}
