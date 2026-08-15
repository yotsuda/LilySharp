\version "2.24.0"
% ledger-lines-dynamics.ly の比較用 twin。
% 主張: dynamics 等の outside-staff 物は加線 (ledger line) を避ける。
\paper { indent = 0 }
\score {
  \relative { f'16\pp[ c d e ] r2. | }
  \layout { ragged-right = ##t }
}
