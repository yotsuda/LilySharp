\version "2.24.0"
% 対照: partial を外した行頭 grace。−0.2 が partial 由来か行頭 grace 由来かを割る。
\paper { indent = 0 ragged-right = ##t }
\score {
  { \grace b8 b4 b2. b1 \bar "|." }
}
