\version "2.24.0"
% hairpin-clef.ly の比較用 twin。
% 両側置換 2 件: 終端 \! → \f (綴り無し)・^\< → \< (Lily# の hairpin は常に下=
% 意図した設計)。主張「折返し後の broken hairpin が clef のせいでずれた高さに
% 刷られない」は下側 hairpin でも同じ機構 (broken 左 bound の X と support)。
% 明示 \break の本なので LP 側 ragged-right 無し (Lily# 全行 justify 規約)。
\paper { indent = 0 }
\relative {
  c''4\< c c c \break c c c c\f |
  \bar "|."
}
