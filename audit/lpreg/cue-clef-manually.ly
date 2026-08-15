\version "2.19.21"

\paper { indent = 0 ragged-right = ##t }

Solo = \relative {
  c'4 c c c |

  % Manually written cue notes, not quoted from another lilypond voice:
  <<
    { \voiceTwo R1 \oneVoice }
    \new CueVoice
    {
      \cueClef "bass"
      \voiceOne
      c4 c c c |
      \cueClefUnset
    }
  >>
  c4 c c c \bar "|."
}

\score {
  <<
    \new Staff \Solo
  >>
}
