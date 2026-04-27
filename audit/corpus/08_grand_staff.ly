\version "2.24.4"
\paper { ragged-right = ##f paper-width = 180\mm }
\score {
  \new PianoStaff <<
    \new Staff = "right" {
      \time 4/4
      \clef treble
      \key c \major
      c''4 e'' g'' c''' | b''2 a''2 | g''1 \bar "|."
    }
    \new Staff = "left" {
      \clef bass
      \key c \major
      c4 e g c' | g2 f2 | c1 \bar "|."
    }
  >>
  \layout {}
}
