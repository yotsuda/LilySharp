\version "2.24.4"
\score {
  <<
    \new Staff { \time 4/4 \tuplet 3/2 { g'8 a' b' } \tuplet 3/2 { c'' d'' e'' } d''2 }
    \new Staff { \clef bass \time 4/4 g4 a b c' }
  >>
}
