\version "2.24.0"
% tuplet-number-alignment score1 twin (LP side).
\paper { indent = 0 ragged-right = ##t }
\relative c''' {
  \tuplet 3/2 { b8 c d }
  \tuplet 3/2 { e8 f g }
}
