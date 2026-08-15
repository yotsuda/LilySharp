\version "2.24.1"

% clef-change-at-end.ly twin (paper-aligned pair for Lily#).
\paper { indent = 0 ragged-right = ##t }

% \bar "|." matches Lily#'s always-final-barline design (pairing rule).
{
  g'1
  \clef "bass"
  \bar "|."
}
