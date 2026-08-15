\version "2.16.0"

% chord-X-align-on-main-noteheads.ly twin (paper-aligned pair for Lily#).
% Hairpin pair (\< \> \!) dropped on BOTH sides: Lily# has no \! terminator
% spelling (same frame-trim as chord-tremolo-articulations).
\paper { indent = 0 ragged-right = ##t }

{
  e''4-^ <e'' e''>-^\p <c'' e'' e''>-^\f <a' d'' e''>-^
  <f'' f''>( <e'' e''>) <f'' f''> ~ <f'' f''>
  f'-^ <f' f'>-^\p <f' f' a'>-^\f <f' g' c''>-^
  <e' e'>( <f' f'>) <e' e'> ~ <e' e'>
}
