\version "2.24.4"
\paper { ragged-right = ##f paper-width = 180\mm }
\score {
  \new Staff <<
    \new Voice = "soprano" { \voiceOne
      \time 4/4
      \clef treble
      \key c \major
      e'4 f' g' a' | b'2 c''2 | b'2 a'2 | g'1 \bar "|."
    }
    \new Voice = "alto" { \voiceTwo
      c'4 d' e' f' | g'2 e'2 | d'2 c'2 | b1 \bar "|."
    }
  >>
  \layout {}
}
