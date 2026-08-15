\version "2.24.0"
% rest-avoid-note.ly の比較用 twin。
% 主張: 休符は音符を避け、自 voice の符尾方向へ動く。
% 両側置換: pitched rest (a4\rest / f2\rest) → plain rest (Lily# に綴りなし)。
\paper { indent = 0 }
\score {
  \new Staff <<
    \relative { g''8 g g r r2 } \\
    \relative { r4 c' r2 } \\
    \relative { c''4 c r2 } \\
    \relative { r2 g' }
  >>
  \layout { ragged-right = ##t }
}
