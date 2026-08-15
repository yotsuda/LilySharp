\version "2.26.0"

m = \fixed c' {
  <f' a' c''>4. <f' g' d''>4. <f' a' d''>4 |
}

\score {
  <<
    \new Staff { \clef treble_8 \m }
    \new TabStaff \with { stringTunings = #guitar-tuning } { \tabFullNotation \transpose c c, \m }
  >>
  \layout { indent = 0\mm }
}
