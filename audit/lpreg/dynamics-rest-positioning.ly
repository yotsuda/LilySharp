\version "2.19.21"

% Frame-aligned twin of input/regression/dynamics-rest-positioning.ly:
% verbatim body, plus the standard comparison paper (indent 0 + ragged-right)
% and \bar "|." (Lily# always draws the final barline).

\paper { indent = 0 ragged-right = ##t }

\relative {
  g'2\p r\p
  g4\f s r4\f s \bar "|."
}
