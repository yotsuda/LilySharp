\version "2.26.0"

v = \relative c' {
  \time 4/4
  << { g'4 f f e | e d d c | e'4 d c b | a g f g4 | f e f g | a g2 e'4 | } \\ { c,4 c d d | e e f f | <a c>4 <a c> <a c> <a c> | <a c> <a c> <a c> c,4 | d e d c | d es s4 fis4 | } \\ { s1 | s1 | s1 | s2 s4 e4 | e e e e | e e e cis'4 | } \\ { s1 | s1 | s1 | s1 | s1 | s1 | } \\ { s1 | s1 | s1 | s1 | s1 | s2 s4 ais4 | } >> \break
  << { e'2 e | e, d } \\ { c'2 s2 | s1 } \\ { c,2 c | c c } \\ { g'2 s2 | s1 } >> \bar "||"
  << { e'1 | e | e, | d } \\ { c'1 | s1 | s1 | s1 } \\ { c,1 | c | c | c } \\ { g'1 | s1 | s1 | s1 } >> \bar "||"
  << { g'1 | e2 d | e d \bar "|." } \\ { c,1 | c2 d | e d } \\ { c'2 b | a1 | b } \\ { g1 | e2 f | a1 } >>
}

\score {
    \new Staff { \clef treble \v }
  \layout { indent = 0\mm }
}
