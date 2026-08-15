\version "2.24.0"
% repeat-tremolo-chord-rep.ly の比較用 twin。
% 主張: tremolo は和音リピート (q) でも働く。
% 自然折返し本 = ragged-right 有り。
\paper { indent = 0 }
\score {
  \relative {
    <c' e g>1
    \repeat tremolo 4 q16
    \repeat tremolo 4 { q16 }
    \repeat tremolo 4 { c16 q16 }
    \bar "|."
  }
  \layout { ragged-right = ##t }
}
