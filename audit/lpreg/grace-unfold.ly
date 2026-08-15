\version "2.24.0"
% grace-unfold-repeat.ly の比較用 twin。原本 + paper 揃え + \bar "|."。
\paper { indent = 0 ragged-right = ##t }
\context Voice \relative c'{
  \repeat unfold  10 {\grace d8 c4 d e f}
  \bar "|."
}
