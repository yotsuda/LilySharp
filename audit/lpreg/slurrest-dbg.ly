\version "2.26.0"
% Demerit dump for the two open residuals of slur-rest-direction:
%  fig4 (c,( r c') up slur — LP start climbs 1 grid to -1.045)
%  fig5 (r16( r r) all-rest slur — LP lands at 2.55, LS at 1.53)
\paper { debug-slur-scoring = ##t }
\relative c
{
    \clef bass
    \time 2/4
    c16 ( r c') r
    r ( r r) r r4
}
