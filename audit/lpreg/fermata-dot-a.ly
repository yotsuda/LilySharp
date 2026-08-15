\version "2.23.12"

% Frame-aligned twin of input/regression/fermata-dot-position.ly, block 1 (dots):
% the henze/veryshort/verylong variants have no Lily# spelling, so BOTH sides
% substitute the available family (henzeshort/veryshort -> shortfermata,
% henzelong/verylong -> longfermata; coverage = 3 glyphs of 8, recorded).
% \time 12/8 on both sides so the dotted quarters fill whole measures
% (Lily# needs explicit bars; a 4/4 bar would split a4. mid-note).

\paper { indent = 0 ragged-right = ##t }

\relative c''' {
  \time 12/8
  \tempo 4 = 60
  a4.
  a4.\fermata
  a4.\shortfermata
  a4.\longfermata
  a4.\shortfermata
  a4.\longfermata
  a4.\shortfermata
  a4.\longfermata \bar "|."
}
