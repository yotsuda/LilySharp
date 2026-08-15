\version "2.23.12"

% Frame-aligned twin of input/regression/figured-bass-empty-figures.ly
% (corpus frame: indent 0, ragged-right).

\paper { indent = 0 ragged-right = ##t }

<<
  \new Staff
  {
    \clef bass
    <g, e g c'>2 <g, d f b>
    <c f g c'>~ <c e g c'>
    <c, a, e d'>2 <c, g, es c'>
    \bar "|."
  }
  \new FiguredBass \figuremode {
    <_ 6 4> <7 5 3>
    <5 4> <_ 3>
    <9 _ 6> <8 _- 5>
  }
>>
