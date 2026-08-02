\version "2.26.0"
%% LP FIDELITY PROBE — the MUSICA FICTA (suggestion) accidental's own printed extent.
%%
%% Run:  cmd /c "lilypond.exe -dno-print-pages -o out editorial-accidental.ly < NUL"
%% (the dump goes to stdout; there is no page geometry to parse, so the two
%% Measure-*.ps1 scripts are not needed for this one).
%%
%% WHAT IS BEING MEASURED, and why an extent rather than a placement:
%%
%% LILYPOND-REF: scm/define-grobs.scm:101 accidental-suggestion-interface's grob declares
%%   (font-size . -2) there (the AccidentalSuggestion entry runs :96-123, and its
%%   (stencil . ly:accidental-interface::print) is the ordinary accidental glyph).
%% LILYPOND-REF: lily/font-select.cc:115-186 select_font — that font-size asks for
%%   20 * 2^(-2/6) = 15.874pt, which lands on the SIXTEEN design
%%   (lily/font-select.cc:41-70 best_rounded_design_size, ratio 15.874/15.87), and the glyph
%%   is then read from THAT file (lily/open-type-font.cc:390-408
%%   get_indexed_char_dimensions) and scaled once.
%%
%% So the printed X-extent of this grob is exactly `design16.box * magstep(-2)`, and it
%% SEPARATES the two ways of building it, which is the whole point of the entry:
%%
%%                      20 design scaled      16 design at magstep(-2)
%%   sharp   width      0.873070              0.873021        (differ by 0.000049)
%%   flat    width      0.730204              0.746588        (differ by 0.016384)
%%   natural width      0.529147              0.529334        (differ by 0.000187)
%%
%% PREDICTION, written before running (HANDOFF §5.0-2): LilyPond prints the RIGHT-hand
%% column. The FLAT is the falsifier — 0.0164 is four orders of magnitude above this
%% ledger's 1e-6 tolerance, so the two spellings cannot be confused, and a reading of
%% 0.730204 would mean LilyPond scales one design after all and this whole island is
%% wrong. ⚠️ The sharp and the natural are NOT decisive on their own (0.00005 / 0.0002);
%% they are here because a per-glyph difference that changes SIGN between glyphs is what
%% optical sizing looks like, and a uniform scale cannot produce it.
%%
%% ⚠️ The Y extent is dumped too and is NOT a second reading of the same thing: a flat's
%% box is asymmetric about the baseline, so it catches a scale applied to X alone.

%% ALSO DUMPED: the suggestion's ORIGIN against its note head's, which is the reading a
%% ledger point can take on both sides (Lily# draws glyphs at their origin too, and the
%% suggestion's origin is where the centring arithmetic puts it —
%% LILYPOND-REF: scm/define-grobs.scm:104 (parent-alignment-X . CENTER) with :110
%% (X-offset . ly:self-alignment-interface::aligned-on-x-parent)). It bundles the head's
%% own width with the accidental's, which is what makes it an END-TO-END reading rather
%% than a second copy of the extent above.
#(define (dump-suggestion grob)
   (let* ((x (ly:grob-extent grob grob X))
          (y (ly:grob-extent grob grob Y))
          (sys (ly:grob-system grob))
          (col (ly:grob-parent grob X))
          (head (if (ly:grob? col) col grob)))
     (format #t "\nPROBE ACCSUG glyph=~a x=(~a . ~a) y=(~a . ~a) origin=~a head=~a\n"
             (ly:grob-property grob 'glyph-name)
             (car x) (cdr x) (car y) (cdr y)
             (ly:grob-relative-coordinate grob sys X)
             (ly:grob-relative-coordinate head sys X))))

\book {
  \score {
    \new Staff \with { suggestAccidentals = ##t } {
      \override AccidentalSuggestion.after-line-breaking = #dump-suggestion
      %% The same three notes the Lily# twin carries (fis / bes / c-natural), so the pair
      %% differs in nothing but the engine. All three heads are black quarters, which is
      %% what makes `origin - head` a pure X reading.
      fis'4 bes' c'! c' \bar "|."
    }
  }
}
