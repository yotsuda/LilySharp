\version "2.16.0"

% Frame-aligned twin of input/regression/chord-tremolo-articulations.ly
% (verbatim body + comparison paper), with ONE narrowing: the hairpin pair
% d32\> e\! is written plain (d32 e) on both sides - Lily# has no spelling
% for the hairpin terminator \!.

\paper { indent = 0 ragged-right = ##t }

\context Voice \relative c' {
  \repeat "tremolo" 4 { d16\f e-. }
  \repeat "tremolo" 4 { d16-> e } | \barNumberCheck #2
  \repeat "tremolo" 4 { d16 e\f }
  \repeat "tremolo" 8 { d32 e } | \barNumberCheck #3
  \repeat "tremolo" 2 { d8\trill e }
  \repeat "tremolo" 2 { d8\sfz e } | \barNumberCheck #4

  \time 2/4
  \repeat "tremolo" 8 { d32^"Markup" e } | \barNumberCheck #5
  c4 c4
}
