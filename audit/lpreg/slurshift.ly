\version "2.26.0"
% lilypond-src/input/regression/slur-shift-region.ly の双子（原文どおり）。
% claim: slur の shift region は extra encompass 要素（tuplet 番号）を
% 収めるため自動で高くなる。
\relative {
  c''2( \tuplet 3/2 { g4 e c) }
}
