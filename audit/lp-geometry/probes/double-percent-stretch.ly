\version "2.26.0"
%
% A DOUBLE PERCENT PAIR ON A JUSTIFIED LINE, OVER A TAB
%
% The pair's middle bar line forbids a break (double-percent-repeat-engraver.cc:68-70), so both
% bars take standard_breakable_column_spacing's dt != 0 branch, whose stretch strength is the
% spring's IDEAL (lily/spacing-basic.cc:68-83, Spring (ideal, min_dist)'s defaults) — column
% origin to column origin. The DoublePercentRepeat is break-aligned with the bar line
% (staff-bar), per staff, so the sign column's origin stands at the widest sign's left edge,
% the TAB's (1.5 times the staff's): 2.818 before the bar line. The bar after the sign
% therefore stretches by the whole sign more than the bar before it. Lily# took the stretch
% from the bar-line-to-bar-line ideal until session 836 and drew the pair 11.14 / 10.90 where
% LilyPond has 10.26 / 11.76; and it set the staff's key signature against the TAB's sign at
% a line start, the first bar line 1.07 too far right.
%
% DPK: E-flat major, a bass staff over a 5-string bass tab, eight double percent repeats of
%      two empty bars, then a break. System 0 is justified; system 1 opens on a pair's first bar.
%
% Body is what `lysc ly` emitted for the .lys book DPK (LpGeometryProbes.cs), indent 0.
%
% Output: PROBEDPK <grob> x=<refpoint x in the system> sys=<system's first column rank>

\paper {
  property-defaults.fonts.serif = "LilyPond Serif"
  property-defaults.fonts.sans = "LilyPond Sans Serif"
}

#(define (dump name)
   (lambda (grob)
     (let ((sys (ly:grob-system grob)))
       (format #t "\nPROBEDPK ~a x=~a sys=~a\n" name
               (ly:grob-relative-coordinate grob sys X)
               (car (ly:grob-spanned-column-rank-interval sys))))))

m = \fixed c' {
  \time 4/4
  \key ees \major
  \mark \markup \box "A" \repeat percent 8 { r1 | r1 } \break
  c,1 |
}

\score {
  <<
    \new Staff \with {
      \override Clef.after-line-breaking = #(dump "CLEF")
      \override BarLine.after-line-breaking = #(dump "BAR")
    } { \clef "bass" \m }
    \new TabStaff \with { stringTunings = #bass-five-string-tuning } { \transpose c c, \m }
  >>
  \layout { indent = 0 \context { \Score printInitialRepeatBar = ##t } }
}
