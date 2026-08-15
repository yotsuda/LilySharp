\version "2.24.0"
% tuplet-number-alignment score2 twin (LP side).
\paper { indent = 0 ragged-right = ##t }
\relative c''' {
  \tuplet 3/2 { b16 c d }
  \tuplet 3/2 { e16 f g }
}
