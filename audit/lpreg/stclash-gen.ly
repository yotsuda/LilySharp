\version "2.26.0"

up = \fixed c' {
  \time 4/4
  << { d4 } \\ { <c a,>4 } >>
}

lo = \fixed c' {
  \time 4/4
  <a b>4
}

\score {
  <<
    \new Staff \with { instrumentName = "Up" } { \clef treble \up }
    \new Staff \with { instrumentName = "Lo" } { \clef treble \lo }
  >>
  \layout { indent = 15\mm }
}
