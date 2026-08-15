\version "2.24.0"
% key-signature-space.ly の比較用 twin。
% 主張: key signature は必要な横スペースを取る (初期 4♭ + 中間変更 5♯ + 全休符譜)。
% 自然折返し本 = ragged-right 有り (第105 規約)。
\paper { indent = 0 }
\score {
  <<
    \new Staff {
      \voiceOne
      \key f \minor
      f'4 f' f' f'
      \key b \major
      e''8 e'' e''4 e''2
    }
    \new Staff {
      R1 \bar "||"
      R1
    }
  >>
  \layout { ragged-right = ##t }
}
