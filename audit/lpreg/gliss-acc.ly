\version "2.24.0"
% glissando-accidental.ly の比較用 twin。
% 原本の \relative を絶対に展開 (数字は gliss-acc.lys のコメント参照)。
% 内容は原本と同一音: a'1 cis'' as' / <f a>(F3A3) <f'' a''>(F5A5) /
% <fis a> <fis'' a''> / <fis ais> <fis'' ais''> / <f ais> <f'' ais''>。
\paper { indent = 0 ragged-right = ##t }
{
  a'1\glissando cis''\glissando as'
  <f a>\glissando <f'' a''>\glissando
  <fis a>\glissando <fis'' a''>\glissando
  <fis ais>\glissando <fis'' ais''>\glissando
  <f ais>\glissando <f'' ais''>
  \bar "|."
}
