\version "2.24.4"
\paper { ragged-right = ##f paper-width = 180\mm }
\score {
  \new Staff {
    \time 4/4
    \clef treble
    \key c \major
    c'4-.->^"forte" d'-_ e'-^ f'-+ |
    g'4--^\fermata a'->\trill b'4-> c''-> |
    \mark \default
    c'4_\markup{ \italic "espress." } d'4-^ e'4-. f'4-> |
    \mark \default
    g'1\fermata \bar "|."
  }
  \layout {}
}
