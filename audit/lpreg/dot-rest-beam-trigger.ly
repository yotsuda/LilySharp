\version "2.19.21"

\paper { indent = 0 ragged-right = ##t }

\new Staff \relative
{
  <<
    { \time 12/16 c''16[ b a r  b g] }
    \\
    { r8. r }
  >>
  \bar "|."
}
