\version "2.16.0"

% Frame-aligned twin of input/regression/chord-tremolo.ly
% (verbatim body + comparison paper).

\paper { indent = 0 ragged-right = ##t }

\context Voice \relative c' {
  \time 4/4
  \repeat "tremolo" 16 { d32 e }
  \repeat "tremolo" 8 { d16 e }
  \repeat "tremolo" 4 { d8 e }

  \time 3/4
  \repeat "tremolo" 12 { d32 e }
  \repeat "tremolo" 6 { d16 e }
  \repeat "tremolo" 3 { d8 e }

  \time 2/4
  \repeat "tremolo" 8 { d32 e }
  \repeat "tremolo" 4 { d16 e }
  \repeat "tremolo" 2 { d8 e }

  \time 1/4
  \repeat "tremolo" 4 { d32 e }
  \repeat "tremolo" 2 { d16 e }

  c4 c4 c4 c4 c4
}
