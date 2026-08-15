\version "2.26.0"

m = \fixed c' {
  \time 2/4
  << { c'4 c'4 } \\ { b4. b8 } >>
}

\score {
    \new Staff { \clef treble \m }
  \layout { indent = 0\mm ragged-right = ##t }
}

