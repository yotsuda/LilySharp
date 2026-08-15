\version "2.16.0"

% Frame-aligned twin of input/regression/chord-names-bass.ly
% (verbatim body + comparison paper).

\paper { indent = 0 ragged-right = ##t }

bladidbla = \chordmode {
  f4:maj7/e_":maj7/e" f:maj7/f_":maj7/f" f2:maj7/g_":maj7/g"
  f4:maj7/+e_":maj7/+e" f:maj7/+f_":maj7/+f" f2:maj7/+g_":maj7/+g"
}

<<
  \context ChordNames \bladidbla
  \context Voice \bladidbla
>>
