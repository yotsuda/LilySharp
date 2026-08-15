\version "2.26.0"
% Control for pctend.ly: part one's music ALONE, no \partCombine, no second part.
% Its only job is to say whether the rest -> first-note gap that differs by 0.30 in the
% twin is a combiner effect or a plain note-spacing effect.
\include "pcdump.ily"
\paper { indent = 0 ragged-right = ##t }

\new Staff \relative {
  r2
  \tuplet 3/2 { g'8[ g g] }
  \tuplet 3/2 { g[ g g] } g1
}
