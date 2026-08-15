\version "2.26.0"

% tablature-double-stem-tremolo.ly 逐語 + paper 揃え + \bar "|."(Lily# 常時終止線)。

\paper { ragged-right = ##t }

\new TabVoice \relative c' {
  \tabFullNotation
  a2:32
  \bar "|."
}

\layout { indent = 0\mm }
