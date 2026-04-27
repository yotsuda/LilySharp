\version "2.24.4"
\paper { ragged-right = ##f paper-width = 180\mm }
\score {
  \new Staff {
    \time 4/4
    \clef treble
    \key c \major
    c'8 c'' c' c'' c'8 c'' c' c'' |
    c'16 c'' c' c'' c' c'' c' c'' c'16 c'' c' c'' c' c'' c' c'' |
    \times 2/3 { c'8 c'' c' } \times 2/3 { c'8 c'' c' } \times 2/3 { c'8 c'' c' } \times 2/3 { c'8 c'' c' } |
    \times 4/6 { c'8 d' e' f' g' a' } \times 4/6 { c''8 d'' e'' f'' g'' a'' } \bar "|."
  }
  \layout {}
}
