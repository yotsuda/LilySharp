\version "2.24.0"
% metronome-parenthesized.ly の比較用 twin。
% 主張: 空テキストの \tempo は括弧付きの速度表示を生む。
\paper { indent = 0 ragged-right = ##t }
\relative {
  \tempo 4=60
  c''1
  \tempo "" 4=80
  c1
}
