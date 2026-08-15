\version "2.26.0"

\header {
  title = "Multi-line Spanners"
  composer = "Lily#"
}

melody = \relative c' {
  \tempo 4 = 120
  \time 4/4
  \key c \major
  \relative c' { c4 ( d e f | g4 a b c | \break d4 c b a ) | g4 f e d | } \relative c' { c2 e2 ~ | \break e4 f g a | } \relative c' { c4\p d\cresc e f | \break g4 a b c\f | }
}

\score {
    \new Staff { \melody }
  \layout { indent = 0\mm }
}
