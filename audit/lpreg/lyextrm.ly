\version "2.24.0"
% lyric-extender-right-margin.ly の比較用 twin。
% 主張: extender は右余白へはみ出ない (tied f~f が break を跨ぐ・長い音節)。
% 明示 \break → LP twin ragged-right 無し (第105 規約)。
\paper { indent = 0 }
\score{
  {
    \relative {
      c'4 d e f ~ | \break
      f4 e d c |
    }

    \addlyrics {
      c d e effffffffffff __
      e d c
    }
  }
}
