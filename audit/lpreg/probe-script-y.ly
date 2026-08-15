\version "2.16.0"

% script-Y family probe: forced-up marcato / accent / staccato on notes the
% script can sit INSIDE the staff for (chord-scripts residual, Δ0.70), plus
% low notes where it naturally sits above, a trill (articulations Δ0.45),
% and below-staff forced-down cases.
\paper { indent = 0 ragged-right = ##t }

{
  c''4^^ c''4^> c''4^. c''4\trill
  g'4^^ e'4^^ c'4^^
  c''4_^ e''4_^
}
