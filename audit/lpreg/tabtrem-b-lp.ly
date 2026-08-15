\version "2.26.0"

% tablature-tremolo.ly 第2 score + paper 揃え + \bar "|."。

\paper { ragged-right = ##t }

music = {
  <c e g c' e'>4:16
  \stemUp
  \repeat tremolo 4 c'16
  \repeat tremolo 2 { c16 d }
  \repeat tremolo 4 { <c d>16 }
  \bar "|."
}

\score {
  \new TabStaff {
    \tabFullNotation
    \music
  }
  \layout { indent = 0\mm }
}
