\version "2.16.0"

\paper { indent = 0 ragged-right = ##t }

\context Voice \relative {
  \time 6/8
  d''4. g,,
  \stemDown
  <b'' c d e>4.  <f g a b>
  <f a c> <e a c> <b f' c' g'>


  <<
    { f  <b c> r4.  }\\
    { b, <a b> r4. }
  >>
  \bar "|."
}
