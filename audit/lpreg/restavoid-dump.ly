\version "2.26.0"
% restavoid.ly + a Rest dump. Regenerated 2026-08-15 (第179) when the two PITCHED
% rests came back into the book: the twin is now what `lysc ly` writes from
% audit/lp-regression/lys/rest-avoid-note.lys — not a hand-written one — and the dump
% prints the GLYPH as well as the position, because which cut of rests.1 LilyPond chose
% is half of what this book measures now (rests.1o carries a ledger line inside it).

v = \fixed c' {
  \time 4/4
  << { g'8 g' g' r8 r2 | } \\ { a,4\rest c r2 | } \\ { c'4 c' f'2\rest | } \\ { r2 g | } >>
}

\score {
    \new Staff { \v }
  \layout {
    indent = 0\mm
    \context { \Score
      \override Rest.after-line-breaking =
        #(lambda (g) (format #t "REST durlog=~a pos=~a Yrel=~a Xrel=~a expr=~a\n"
             (ly:grob-property g 'duration-log)
             (ly:grob-property g 'staff-position)
             (ly:grob-relative-coordinate g (ly:grob-system g) Y)
             (ly:grob-relative-coordinate g (ly:grob-system g) X)
             (ly:stencil-expr (ly:grob-property g 'stencil))))
    }
  }
}
