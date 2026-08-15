\version "2.26.0"

music = \relative { e'''16. ( e,,32 ) \bar "|." }

\score {
    \new Staff { \music }
  \layout { indent = 0\mm }
}
