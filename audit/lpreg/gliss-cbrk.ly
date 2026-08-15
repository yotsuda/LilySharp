\version "2.24.0"
% glissando-chord-linebreak.ly の比較用 twin。
% 原本の \relative c'' を絶対展開 (<c e>=C5E5・d=D5)。変数 \theNotes は手展開。
% 明示 break の本: Lily# は全行 justify なので LP 側は ragged-right 無し (規約の逆)。
\paper { indent = 0 }
{
  <c'' e''>4 <c'' e''> <c'' e''>\glissando d''
  \break
  <c'' e''>4 <c'' e''> <c'' e''>\glissando d''
  \bar "|."
}
