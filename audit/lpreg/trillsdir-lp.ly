\version "2.24.0"
% trill-spanner-direction.ly の比較用 twin (paper 揃え)
\paper { indent = 0 ragged-right = ##t }
{
  \voiceTwo % sets DOWN by default, but ^ and _ should have precedence
  g\startTrillSpan g^\startTrillSpan g_\startTrillSpan g-\startTrillSpan
}
