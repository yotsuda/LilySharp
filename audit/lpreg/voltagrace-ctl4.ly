\version "2.24.0"
% 対照: 中間位置の grace + repeat volta — LP の bar/grace 順序。
\paper { indent = 0 }
\score {
  \new Voice {
    c'1 \grace f'8 \repeat volta 2 b'1
  }
  \layout { ragged-right = ##t }
}
