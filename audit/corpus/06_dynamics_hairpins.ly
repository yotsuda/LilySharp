\version "2.24.4"
\paper { ragged-right = ##f paper-width = 180\mm }
\score {
  \new Staff {
    \time 4/4
    \clef treble
    \key c \major
    c'2\p\< d'2 | e'2\f f'2\> | g'1\pp |
    c'2.\mp d'4 \cresc | e'2 f'2\ff |
    g'1\sfz \bar "|."
  }
  \layout {}
}
