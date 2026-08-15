\version "2.26.0"

m = \fixed c' {
  \once \override Stem.direction = #UP gis'8 \once \override Stem.direction = #UP a, \once \override Stem.direction = #UP bes' \once \override Stem.direction = #UP a, \once \override Stem.direction = #UP bes' \once \override Stem.direction = #UP a, \once \override Stem.direction = #UP bes' \once \override Stem.direction = #UP a, |
}

\score {
    \new Staff { \m }
  \layout { indent = 0\mm }
}
