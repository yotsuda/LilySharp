\version "2.24.4"
\paper { ragged-right = ##f paper-width = 180\mm }
\score {
  \new Staff {
    \time 4/4
    \clef treble
    \key c \major
    c'4( d' e' f') | g'2~ g'4 \( a' \) | a'2.( b'4) |
    c''4( d'' e'' f'') ~ f''2( e''2) | c''2.~( c''4) |
    a'4( b' c'' d'') \bar "|."
  }
  \layout {}
}
