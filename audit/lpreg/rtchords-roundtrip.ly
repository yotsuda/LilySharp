\version "2.26.0"

v = \fixed c' {
  \time 4/4
  <d-\repeatTie g>1 |
  <d^\repeatTie g_\repeatTie>1 |
  <d g>\repeatTie |
}

\score {
    \new Staff { \v }
  \layout { indent = 0\mm }
}
