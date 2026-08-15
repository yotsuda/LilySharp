\version "2.24.0"
% rest-collision-note-duration.ly の比較用 twin。
% 主張: 多声譜の休符の縦位置は音符の時価に従う。
\paper { indent = 0 }
\score {
  \relative {
    << { g'1  g2 } \\
       { \repeat unfold 2 {r8 d4 d8 r d4 d8} } >>
  }
  \layout { ragged-right = ##t }
}
