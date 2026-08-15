\version "2.26.0"

v = \fixed c' {
  \time 4/4
  << { g'8 g' g' r8 r2 | } \\ { a,4\rest c r2 | } \\ { c'4 c' f'2\rest | } \\ { r2 g | } >>
}

\score {
    \new Staff { \v }
  \layout { indent = 0\mm }
}
