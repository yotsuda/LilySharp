\version "2.24.0"
% flag-stem-begin-position.ly の比較用 twin。
% CLAIM: merge された符頭に符尾が正しい始点で届く。
% 枠: 原本の \aikenHeads (f=fa・e=mi) は Lily# に形が無い → 両側とも
% 'triangle (noteheads.s2triangle = Lily# @notehead.triangle と同一グリフ) に置換。
% 原本の 3 つの << \\ >> は連続束なので多声 1 スパンに融合 (タイミング不変・
% collisions 便の規約と同型)。s8 で 4/4 を完結。
\paper { indent = 0 ragged-right = ##t }
{
  \override Staff.NoteHead.style = #'triangle
  << { f'8 f'4:32 e'8 f' s4 s8 } \\ { f'8 f'4 e'8 f' s4 s8 } >>
  \bar "|."
}
