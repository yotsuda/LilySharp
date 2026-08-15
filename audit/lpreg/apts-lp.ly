\version "2.24.0"
\paper { indent = 0 ragged-right = ##t }
test = {
  c'1
  << { c'4 d' e' f' } \\ { g,1 } >>
  c'1
}
\score {
  <<
    \new Staff { \clef "treble_8" \test }
    \new TabStaff { \test }
  >>
}
