\version "2.16.0"

% Control probe: slur/tie X attachment on PLAIN notes (no unison chords) —
% separates the generic slur/tie X regime from chord-X-align's unison claim.
\paper { indent = 0 ragged-right = ##t }

{
  f''4( e'') f'' ~ f''
  e'4( f') e' ~ e'
}
