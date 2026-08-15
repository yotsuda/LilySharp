\version "2.26.0"
% input/regression/part-combine-relative.ly, verbatim body.
% texidoc: "\partCombine needs to be given pitches in their final octaves, so if \relative is
% used it must be applied inside \partCombine.  The pitches in \partCombine are unaffected by
% an outer \relative, so that the printed output shows the pitches that \partCombine used.
% The expected output of this test is three identical measures."
\include "pcdump.ily"
\paper { indent = 0 ragged-right = ##t }

\new Staff {
  \partCombine \absolute { e'2 f' } { c'2 d' }
  \relative \partCombine { e'2 f' } { c'2 d' } % relative is ignored
  \partCombine \relative { e'2 f } \relative { c'2 d }
}
