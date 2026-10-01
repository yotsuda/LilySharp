\version "2.26.0"
%
% A SLUR ON A GRACE COLUMN, ON A TAB STAFF. LilyPond draws it like any tab slur - the ordinary scorer
% over bare digits, then slur::move-closer-to-tab-note-heads (tab-slur.ly) - with the grace's own
% digit as its bound and DOWN when it starts in grace time (score-grace-settings, Slur direction DOWN).
% Lily# skipped every tab slur with a grace bound until session 730.
%   TGH  grace { d16( } e4)        a hand-written appoggiatura shape
%   TGA  \acciaccatura { d16 } e4  the automatic grace slur (ly/grace-init.ly)
%   TGI  grace { d16( e16) } f4    both ends in grace time
% Bodies are what `lysc ly` emitted for TGH / TGA / TGI (LpGeometryProbes.cs). RUN WITH -dbackend=null
% (tab-slur.ly says why).
% Output: PROBET TABSLUR dir= ss= span= y0= y1= y2= y3=  (tab staff spaces above its middle)

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

blTGH = \fixed c' {
  \time 4/4
  \key c \major
  c,4\3 r g,,\4 r |
  c,\3 r \grace { d,16\3 ( } e,4\3 ) g,,\4 |
}

\score {
  \new TabStaff \with { stringTunings = #bass-four-string-tuning } { \transpose c c, \blTGH }
  \layout {}
}

blTGA = \fixed c' {
  \time 4/4
  \key c \major
  c,4\3 r g,,\4 r |
  c,\3 r \acciaccatura { d,16\3 } e,4\3 g,,\4 |
}

\score {
  \new TabStaff \with { stringTunings = #bass-four-string-tuning } { \transpose c c, \blTGA }
  \layout {}
}

blTGI = \fixed c' {
  \time 4/4
  \key c \major
  c,4\3 r g,,\4 r |
  c,\3 r \grace { d,16\3 ( e,16\3 ) } f,4\3 g,,\4 |
}

\score {
  \new TabStaff \with { stringTunings = #bass-four-string-tuning } { \transpose c c, \blTGI }
  \layout {}
}

% NOSLUR (book TGN): TGH without its slur - the tab grace column's own spacing, read digit centre to digit
% centre: 1.466742 (session 731).
blTGN = \fixed c' {
  \time 4/4
  \key c \major
  c,4\3 r g,,\4 r |
  c,\3 r \grace { d,16\3 } e,4\3 g,,\4 |
}

\score {
  \new TabStaff \with { stringTunings = #bass-four-string-tuning } { \transpose c c, \blTGN }
  \layout {}
}
