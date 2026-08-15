\version "2.24.0"
% repeat-volta-initial-grace.ly の比較用 twin。
% 主張: 冒頭 grace + volta repeat の開始リピート線が期待位置に出る。
\paper { indent = 0 }
\score {
  \new Voice {
    \grace f'8 \repeat volta 2 b'1
  }
  \layout { ragged-right = ##t }
}
