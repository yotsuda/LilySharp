\version "2.19.21"

\paper { indent = 0 ragged-right = ##t }

\relative {
  c'4 d \hideNotes e4 f |
  \unHideNotes g a \once \hideNotes b c \bar "|."
}
