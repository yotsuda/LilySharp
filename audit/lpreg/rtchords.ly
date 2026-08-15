\version "2.24.0"
% repeat-tie-chords.ly の比較用 twin。
% 主張: \repeatTie は和音の個々の音にも効く (member 単位 + ^/_ 向き強制 + 和音レベル)。
% 自然折返し本 = ragged-right 有り。
\paper { indent = 0 }
\score {
  \relative {
    <d'-\repeatTie g>1
    <d^\repeatTie g_\repeatTie>1
    <d g>\repeatTie
    \bar "|."
  }
  \layout { ragged-right = ##t }
}
