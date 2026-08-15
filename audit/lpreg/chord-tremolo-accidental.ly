\version "2.17.2"

% Frame-aligned twin of input/regression/chord-tremolo-accidental.ly
% (verbatim body + comparison paper).

\paper { indent = 0 ragged-right = ##t }

{
  \repeat tremolo 16 { c''32 d'' }
  \repeat tremolo 16 { c''32 <dis''> }
  \repeat tremolo 16 { c''32 <dis'' fis''> }
  \repeat tremolo 8 { c''32 d'' }
  \repeat tremolo 8 { c''32 <dis''> }
  \repeat tremolo 8 { c''32 <dis'' fis''> }
  \repeat tremolo 4 { c''32 d'' }
  \repeat tremolo 4 { c''32 <dis''> }
  \repeat tremolo 16 { b''32 <cis'''> }
}
