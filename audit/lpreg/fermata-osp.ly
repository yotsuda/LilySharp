\version "2.23.12"

% Frame-aligned twin of input/regression/fermata-outside-staff-priority.ly:
% same two scores (trill spanner / trill script under an ottava), with the
% corpus frame (indent 0, ragged-right) so X starts align with the Lily# twin.

\paper { indent = 0 ragged-right = ##t }

{
  \ottava #1
  g''2->\fermata\startTrillSpan
  \ottava #0
  r\stopTrillSpan
}

{
  \ottava #1
  g''2->\fermata\trill
  \ottava #0
  r
}
