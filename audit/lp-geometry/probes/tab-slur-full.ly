\version "2.26.0"
%
% THE SAME TAB SLURS AS tab-slur.ly, UNDER \tabFullNotation — which is what Lily#'s
% default `tab` is (a lone tab draws its own rhythm; LilyPondExporter writes
% \tabFullNotation for it) and what tab-slur.ly deliberately is not.
%
% \tabFullNotation reverts TabStaff.Slur.control-points (ly/property-init.ly:845), so the
% second of tab-slur.ly's two stages — slur::move-closer-to-tab-note-heads, 0.35 staff
% spaces back toward the digits — does not run. It also reverts the stem overrides, but in
% this book every stem points AWAY from its slur (the direction rule puts it there), so the
% stems reach nothing the scorer reads. Expected, and measured on 2.26.0: every y exactly
% 0.35 farther out than tab-slur.ly's, the rise and the span unchanged.
%   null  dir=1  span=5.574433  y0=1.570223  y1=2.627652
%
% The book, the output line and the -dbackend=null requirement are tab-slur.ly's; see there.

\paper { indent = 0 ragged-right = ##t }

\layout {
  \context {
    \Score
    \override Slur.after-line-breaking =
      #(lambda (grob)
         (let* ((ss (ly:staff-symbol-staff-space grob))
                (staff (ly:grob-object grob 'staff-symbol))
                (cps (ly:grob-property grob 'control-points))
                ;; control-points are relative to the slur's own Y refpoint, which on a
                ;; single-staff score IS the staff symbol's; subtract it anyway, so the
                ;; reading cannot silently depend on that.
                (base (if (ly:grob? staff)
                          (- (ly:grob-relative-coordinate grob (ly:grob-common-refpoint grob staff Y) Y)
                             (ly:grob-relative-coordinate staff (ly:grob-common-refpoint grob staff Y) Y))
                          0)))
           (format #t "\nPROBET TABSLUR dir=~a ss=~a span=~a y0=~a y1=~a y2=~a y3=~a\n"
                   (ly:grob-property grob 'direction)
                   ss
                   (/ (- (car (list-ref cps 3)) (car (list-ref cps 0))) ss)
                   (/ (+ base (cdr (list-ref cps 0))) ss)
                   (/ (+ base (cdr (list-ref cps 1))) ss)
                   (/ (+ base (cdr (list-ref cps 2))) ss)
                   (/ (+ base (cdr (list-ref cps 3))) ss))))
  }
}

bl = \fixed c' {
  \time 4/4
  \key c \major
  g,4\2( c\1 c\1 g,\2) |
  d,4\3( a,,\4 a,,\4 d,\3) |
}

\score {
  \new TabStaff \with { stringTunings = #bass-four-string-tuning }
    { \tabFullNotation \transpose c c, \bl }
  \layout {}
}
