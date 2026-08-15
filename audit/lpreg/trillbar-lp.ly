\version "2.26.0"

% trill-spanner-to-barline.ly 逐語 + paper 揃え + \bar "|."。

\paper { ragged-right = ##t }

{ c'1\startTrillSpan 1\stopTrillSpan \bar "|." }

\layout { indent = 0\mm }
