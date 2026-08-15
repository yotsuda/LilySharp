\version "2.26.0"

music = \fixed c' {
  \time 2/4 d'16 d' d' d' d' d' cis' d' |
  dis' dis' dis' dis' d' d' d' d' |
  d' d' cis' d' dis' dis' dis' dis' |
  d' d' d' d' d' d' cis' d' |
  dis' dis' dis' dis' d' d' d' d' |
  d' d' cis' d' dis' dis' dis' dis' |
  d' d' d' d' d' d' cis' d' |
  dis' dis' dis' dis' d' d' d' d' |
  d' d' cis' d' dis' dis' dis' dis' |
  d' d' d' d' d' d' cis' d' |
  dis' dis' dis' dis' d' d' d' d' |
  d' d' cis' d' dis' dis' dis' dis' |
  \bar "|."
}

\score {
    \new Staff { \music }
  % 原本の枠: line-width 18cm = Lily# の既定 (210-15-15 mm)。非最終行は既定で justify。
  \layout { indent = 0\mm line-width = 18.\cm }
}
