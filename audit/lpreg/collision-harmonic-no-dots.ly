\version "2.19.21"

% collision-harmonic-no-dots.ly twin (paper-aligned pair for Lily#).
\paper { indent = 0 ragged-right = ##t }

\relative {
  <<
    { <fis'\harmonic>2. }
    \\
    { e2. }
  >>
  r4
  \bar "|."
}
