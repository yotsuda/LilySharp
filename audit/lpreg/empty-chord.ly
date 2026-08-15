\version "2.19.21"

% Frame-aligned twin of input/regression/empty-chord.ly:
% verbatim body except \enddecr -> \pp on BOTH sides (Lily# has no bare
% terminator spelling; the terminator kind is not part of the claim),
% plus the standard comparison paper and \bar "|.".

\paper { indent = 0 ragged-right = ##t }

\relative {
  r4 e'8( g <>) ^"sul D" \f \> \repeat unfold 8 { c-. } <>\sfz
  <>\downbow \repeat unfold 2 { c g } c1\> <>\pp \bar "|."
}
