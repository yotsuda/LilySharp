\version "2.24.0"
% hara-kiri-percent-repeat.ly の比較用 twin。
% 主張: percent repeat を持つ Staff/TabStaff/DrumStaff/RhythmicStaff は
% \RemoveEmptyStaves でも消されない (percent-repeat-interface ∈ keepAliveInterfaces)。
% 明示 \break → LP 側 ragged-right 無し (第105 規約)。
% RhythmicStaff は音高無視で線上に置く = Lily# 側は lines 1 + b (中央線) で対にする。
\paper { indent = 0 }
\score {
  <<
    \new Staff { c''1 c'' \break c'' c'' }
    \new Staff \repeat percent 4 { c'1 }
    \new TabStaff \repeat percent 4 { c1 }
    \new DrumStaff \drummode { \repeat percent 4 { hh1 } }
    \new RhythmicStaff \repeat percent 4 { c'1 }
  >>
  \layout {
    \context { \Staff \RemoveEmptyStaves }
    \context { \RhythmicStaff \RemoveEmptyStaves }
    \context { \DrumStaff \RemoveEmptyStaves }
    \context { \TabStaff \RemoveEmptyStaves }
  }
}
