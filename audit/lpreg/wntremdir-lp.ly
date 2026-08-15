\version "2.24.0"
% whole-note-tremolo-direction twin (LP side).
\paper { indent = 0 ragged-right = ##t }
\relative {
  \time 3/2
  \repeat "tremolo" 16 { b'32 a }
  \repeat "tremolo" 8 { b32 a }
  \repeat "tremolo" 16 { b32 c }
  \repeat "tremolo" 8 { b32 c }
}
