\version "2.26.0"

v = \relative c' {
  \time 4/4
  << { <a' b>2 <a b d e> | <a e' f> <a e' f> | <a b c> <f g a> \bar "||" <f g c> <g a c> | <f g c d> <f g c d> \bar "|." } \\ { <g a>2 <a b> | <g a e'> <g a c e> | <f g a> <a b c> | <g a e'> <g a e'> | <g c d> <g a b> } >>
}

\score {
    \new Staff { \clef treble \v }
  \layout { indent = 0\mm }
}
