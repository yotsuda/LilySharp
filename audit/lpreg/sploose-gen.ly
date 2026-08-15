\version "2.26.0"

rh = \fixed c' {
  \tuplet 3/2 { g4 a2 } |
}

lh = \fixed c' {
  fis,,8 cis, \clef treble g8 fis, |
}

\score {
    \new GrandStaff <<
      \new Staff { \clef treble \rh }
      \new Staff { \clef bass \lh }
    >>
  \layout { indent = 0\mm }
}
