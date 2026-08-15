\version "2.24.0"
% lyric-melisma-melisma.ly の比較用 twin。
% 主張: melisma に当たる音節 (looong) は左揃え。
% 両側置換: \melisma/\melismaEnd → slur (Lily# に手動 melisma 綴りなし。
% LP は slur でも melismaBusy = lyric-engraver.cc:180-183 の同機構)。
% 自然折返し本 → ragged-right 有り。
\paper { indent = 0 ragged-right = ##t }
\relative {
  c'4 c c16( d e f) g4
}
\addlyrics { ha ha looong __ ho }
