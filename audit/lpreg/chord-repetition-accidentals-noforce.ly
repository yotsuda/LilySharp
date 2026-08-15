\version "2.18.0"

% Frame-aligned twin of input/regression/chord-repetition-accidentals.ly
% with the FORCED-accidental measure dropped from both sides (f! has no Lily#
% spelling - LYS4009); only the reminder (f?) measure is compared. The chord
% respells to <f'? a d f?> so the standalone measure keeps the original's
% octaves (F4 A4 D5 F5).

\paper { indent = 0 ragged-right = ##t }

\relative {
  <f'? a d f?> q q q
}
