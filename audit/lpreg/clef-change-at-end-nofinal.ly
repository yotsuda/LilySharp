\version "2.24.1"

% clef-change-at-end.ly twin (paper-aligned pair for Lily#), WITHOUT \bar "|.".
% Lily# no longer stamps a final barline on the last measure, so the pair that
% matches its design is the one where neither side writes one: LilyPond closes a
% complete final measure with a plain thin bar.
\paper { indent = 0 ragged-right = ##t }

{
  g'1
  \clef "bass"
}
