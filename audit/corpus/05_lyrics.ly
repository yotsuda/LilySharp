\version "2.24.4"
\paper { ragged-right = ##f paper-width = 180\mm }
\score {
  <<
    \new Staff = "voice" \relative c'' {
      \time 4/4
      \clef treble
      c4 d e f | g4 a b c | c2 b4 a | g2 r2 \bar "|."
    }
    \new Lyrics \lyricsto "voice" {
      Twink -- le, twink -- le, lit -- tle star,
      How I won -- der what you are. __
    }
  >>
  \layout {}
}
