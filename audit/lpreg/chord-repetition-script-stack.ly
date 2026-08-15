\version "2.19.21"

% Frame-aligned twin of input/regression/chord-repetition-script-stack.ly
% (verbatim body + comparison paper).
% NOTE: Lily# renders no chord-level fingering (silently dropped - ticketed),
% so the pair narrows the stack to a script + text: original -1-2-3 replaced
% by -. on both events, keeping the stacking-order claim observable.

\paper { indent = 0 ragged-right = ##t }

\relative {
  <c' e g>2-. q_"q"-.
}
