\version "2.24.0"
% grace-direction-polyphony.ly の比較用 twin。
% 原本そのまま + paper 揃え + \bar "|."。無引数 \relative の c''' = C6。
\paper { indent = 0 ragged-right = ##t }
\relative {
  \voiceOne
  c'''4
  \grace d8 c4
  \bar "|."
}
