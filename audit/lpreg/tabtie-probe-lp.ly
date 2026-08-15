\version "2.26.0"

% tabtie-probe の LP 対照(同じ縮約音楽)。明示 break 本=ragged-right 無し。

\new TabStaff {
  f2~ f4 e4
  c'1~ \break
  c'2~ c'2
  \bar "|."
}

\layout { indent = 0\mm }
