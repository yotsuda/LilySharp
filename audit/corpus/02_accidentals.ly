\version "2.24.4"
\paper { ragged-right = ##f paper-width = 180\mm }
\score {
  \new Staff {
    \time 4/4
    \clef treble
    \key c \major
    cis'4 dis' eis' fis' |
    <cis' es' g' bes'>1 |
    <c' cis' d' dis'>1 |
    <fes' f' fis'>2 <bes' b' bis'>2 |
    cis''!4 c''? cis''4 c''!4 \bar "|."
  }
  \layout {}
}
