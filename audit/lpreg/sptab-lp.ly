\version "2.26.0"

% lysc ly の出力 + 手復元: exporter は第2・3 score を落とす(既知穴の3例目・起票)。
% \header piece は両側落とし(LS --combined はタイトル未描画のため枠を揃える。
% claim の核 = Staff/TabStaff の score 間隔の一致はタイトル無しで立つ)。

m = \fixed c' {
  c4 d e f |
  g1 |
}

\score {
    \new Staff { \m }
  \layout { indent = 0\mm }
}
\score {
    \new TabStaff { \m }
  \layout { indent = 0\mm }
}
\score {
    \new Staff { \m }
  \layout { indent = 0\mm }
}
