\version "2.16.0"

% Frame-aligned twin of input/regression/chord-tremolo-whole.ly
% (verbatim body + comparison paper).

\paper { indent = 0 ragged-right = ##t }

\relative c'''{
  \repeat tremolo 32{ g64 a }
}
