\version "2.26.0"
%
% AN EMPTY BAR OPENING A LINE: WHERE DOES ITS CLOSING BAR LINE STAND?
%
% A percent repeat's bars draw no rest, so every column inside them is unused and the bar's
% two columns are the prefatory column and its closing bar line: one breakable pair,
% standard_breakable_column_spacing (lily/spacing-basic.cc:40-66) — Spring (min_dist + space,
% min_dist), stretched by `space` alone. Lily# priced it until session 833 as a line start
% wishing to a first NOTE and then the bar-to-bar pair, 2.18 too wide on a justified line.
%
% ESB: a bass staff, `c1` then thirty one-bar percent repeats, justified; system 1 (the first
%      continuation) opens on an empty bar.
%
% Body is what `lysc ly` emitted for the .lys book ESB (LpGeometryProbes.cs), indent 0 as
% the ledger's ProbePaper renders a nameless probe.
%
% Output: PROBEESB <grob> x=<refpoint x in the system> sys=<system rank>

\paper {
  property-defaults.fonts.serif = "LilyPond Serif"
  property-defaults.fonts.sans = "LilyPond Sans Serif"
}

#(define (dump name)
   (lambda (grob)
     (let ((sys (ly:grob-system grob)))
       (format #t "\nPROBEESB ~a x=~a sys=~a\n" name
               (ly:grob-relative-coordinate grob sys X)
               (ly:grob-property sys 'rank 0)))))

\score {
  \new Staff { \clef "bass" \fixed c' {
    \override Staff.Clef.after-line-breaking = #(dump "CLEF")
    \override Staff.BarLine.after-line-breaking = #(dump "BAR")
    \time 4/4
    \key c \major
    c1 |
    \repeat percent 30 { r1 }
  } }
  \layout { indent = 0 \context { \Score printInitialRepeatBar = ##t } }
}
