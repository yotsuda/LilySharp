\version "2.26.0"

up = \fixed c' {
  \time 4/4
  << { d4 } \\ { <c a,>4 } >>
}

lo = \fixed c' {
  \time 4/4
  <a b>4 \bar "|."
}

\score {
  <<
    \new Staff { \clef treble \up }
    \new Staff { \clef treble \lo }
  >>
  \layout { indent = 0\mm }
}
