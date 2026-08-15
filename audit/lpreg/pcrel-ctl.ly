\version "2.26.0"
% Control for pcrel.ly: the book WITHOUT its second measure, i.e. only the two measures Lily#
% can spell (an octave mode is a stream switch here, not a wrapper, so there is nothing that
% could play the part of an OUTER \relative for the combiner to ignore).
% Its job is to give the twin a matching frame: spacing is a property of the whole score, so a
% two-measure Lily# page cannot be compared column-for-column against a three-measure LilyPond
% page.
\include "pcdump.ily"
\paper { indent = 0 ragged-right = ##t }

\new Staff {
  \partCombine \absolute { e'2 f' } { c'2 d' }
  \partCombine \relative { e'2 f } \relative { c'2 d }
}
