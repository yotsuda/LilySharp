\version "2.23.12"

% Chord-bar probe for script-tie-collision: bars 1-2 (plain tied accent, as the
% octave anchor AND a control) + bars 9-10 (the chords) of the twin, so BOTH
% engines keep them on ONE system and the tie pieces are unbroken — the twin's
% engines break differently there, which confounds the member-script stack.

\paper { indent = 0 ragged-right = ##t }

\relative {
  r2. c'''4~-> | c-> r2. |
  r2. <g-- c-> >4--~ | <g-- c>->~ <g c---_>-> r2 |
}
