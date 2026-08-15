\version "2.26.0"

music = \fixed c' {
  % ^"rit" は exporter が @text を書けない穴（warning あり）——手で復元。
  f8^"rit" ( c'8 f' c'' f'' ) r8 r4 |
  c''2 ( c'2 |
  g1\startTrillSpan ) ~ |
  g1\stopTrillSpan |
  g1\f ( |
  g,1 ) |
}

\score {
    \new Staff { \music }
  \layout { indent = 0\mm }
}
