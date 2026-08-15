\version "2.23.12"

% Frame-aligned twin of input/regression/script-tie-collision.ly.
% \break dropped on BOTH sides (line breaking is frame-neutral to the per-note
% claim); bare-duration repeats (4->) written as explicit pitches on both sides.

\paper { indent = 0 ragged-right = ##t }

\relative {
  r2. c'''4~-> | c-> r2. |
  r2. c4-> | c-> r2. |
  r2. c4~-> | c r2. |
  r2. c4~ | c4-> r2. |
  r2. <g-- c-> >4--~ | <g-- c>-> ~ <g c---_>-> r2 |
  r2. c4~ | c4-> ~ c-> r2 |
  r2. c4~-> | c ~ c-> r2 |
  r2. c4~-> | c-> ~ c r2 |
  r2. c4-> | \bar "|."
}
