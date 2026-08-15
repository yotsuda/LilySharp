\version "2.23.12"

% Which tenuto×tie pairings does LP lift at the tie-END note?
% A: plain tenuto  B: chord-level tenuto (2 ties)  C: member tenuto on the lower head

\paper { indent = 0 ragged-right = ##t }

{
  r2. c'''4--~ | c'''4-- r2. |
  r2. <g'' c'''>4--~ | <g'' c'''>4-- r2. |
  r2. <g''-- c'''>4~ | <g''-- c'''>4 r2. |
}
