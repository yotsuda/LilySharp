\version "2.16.0"

% Frame-aligned twin of input/regression/chord-tremolo-single.ly
% (verbatim body + comparison paper).

\paper { indent = 0 ragged-right = ##t }

\context Voice \relative c' {
  \time 4/4
  \repeat "tremolo" 32 { d32 }

  c4 c4 c4 c4 c4
}
