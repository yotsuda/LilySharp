\version "2.26.0"

v = \fixed c' {
  \time 4/4
  << { b4 f2. } \\ { \grace { a,8 } <b, g>1:32 } >>
}

\score {
    \new Staff { \clef treble \v \bar "|." }
  \layout { indent = 0\mm }
}
