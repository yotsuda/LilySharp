\version "2.26.0"

v = \fixed c' {
  \time 4/4
  << { b4 f2. } \\ { \grace { a,8 } <b, g>1 } >>
}

\score {
    \new Staff { \clef treble \v }
  \layout { indent = 0\mm }
}
