\version "2.24.0"
% hairpin-key-signature.ly の比較用 twin。
% 両側置換 2 件 (hairpin-clef と同じ): 終端 \! → \f・^\< → 既定の下側 hairpin。
% 明示 \break の本なので LP 側 ragged-right 無し。
\paper { indent = 0 }
\relative {
  \key a \major
  c''4\< c c c \break c c c c\f |
  \bar "|."
}
