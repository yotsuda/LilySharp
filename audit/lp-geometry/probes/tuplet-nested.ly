\version "2.26.0"
%% LP FIDELITY PROBE: a tuplet bracket clears the tuplets it encloses.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe tuplet-nested.ly (four books).
%%
%% calc_position_and_height pushes each enclosed tuplet's box at its two ends and its number
%% as encompass points (lily/tuplet-bracket.cc:646-680). Lily# ported that for tab staves only
%% and stood a notation staff's outer bracket a fixed 2.0 per depth beyond where it would be
%% alone. These are the points for the notation staff (session 1100).
%%
%% THE FLOOR IS MADE TO BIND as in tuplet-bracket-rest-point.ly: the music is c'', stems DOWN,
%% over a bass staff of middle-line wholes, so the bracket hangs into the gap, and the gap IS
%% (outer bracket/number down-reach) + 2.05 + 1.
%%
%% THE BOOKS:
%%   TNESTC, THE CONTROL: the same notes with no inner tuplet. The bracket clears the stem tips.
%%   TNESTN: the middle three in a tuplet of their own, not beamed to the outer ones. FORK: a gap
%%        equal to TNESTC's means the box is not a point; deeper means it is.
%%   TNESTB: the inner three beamed, so the inner bracket is hidden and only its number is a point.
%%   TNESTI: an inner tuplet with the outer's ratio and bounds: one tuplet (tuplet-engraver.cc:74-77).
%%        LP IDENTITY with the same notes under one tuplet, in depth.
%%
%% PREDICTIONS, written before running:
%%   * TNESTN's and TNESTB's gaps exceed TNESTC's; TNESTN's by more than the inner bracket's own padding 1.1
%%     plus its number's half height, since the outer clears the number at the inner's bracket.
%%   * TNESTI has ONE number and ONE bracket per tuplet.
%%   * Lily# mirrors: all four residuals under 1e-3 after the port. Before it, TNESTN stood 2.0 deep.

#(define (probe-dump-pages layout pages)
   (format #t "\nPROBEV PAPER top-margin=~a paper-height=~a line-width=~a\n"
           (ly:output-def-lookup layout 'top-margin)
           (ly:output-def-lookup layout 'paper-height)
           (ly:output-def-lookup layout 'line-width))
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (format #t "PROBEV PAGE ~a systems=~a\n" n (length lines))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let* ((sys (car ls))
                        (ext (ly:stencil-extent (ly:prob-property sys 'stencil) Y))
                        (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0))))
                   (format #t "PROBEV SYS ~a ~a y=~a ext=(~a . ~a) staff=(~a . ~a)\n"
                           n i
                           (ly:prob-property sys 'Y-offset 0.0)
                           (car ext) (cdr ext)
                           (car staff) (cdr staff))
                   ;; TupletBracket / TupletNumber / Rest / Beam ride along so the reading
                   ;; can be decomposed: rel is the grob about the SYSTEM refpoint, ext its
                   ;; own ink — the Rest rows say where the rest's ink actually is, the
                   ;; bracket rows carry the raw positions and whether par_beam is set.
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (or (eq? nm 'TupletNumber) (eq? nm 'TupletBracket)
                                            (eq? nm 'Rest) (eq? nm 'Beam))
                                        (format #t "PROBEV GROB ~a ~a name=~a rel=~a ext=(~a . ~a) x=(~a . ~a) pos=~a beam=~a\n"
                                                n i nm
                                                (ly:grob-relative-coordinate g sg Y)
                                                (car (ly:grob-extent g g Y))
                                                (cdr (ly:grob-extent g g Y))
                                                (+ (ly:grob-relative-coordinate g sg X)
                                                   (car (ly:grob-extent g g X)))
                                                (+ (ly:grob-relative-coordinate g sg X)
                                                   (cdr (ly:grob-extent g g X)))
                                                (if (eq? nm 'TupletBracket)
                                                    (ly:grob-property g 'positions)
                                                    '())
                                                (if (eq? nm 'TupletBracket)
                                                    (if (ly:grob? (ly:grob-object g 'beam #f)) "SET" "#f")
                                                    '())))))
                                (ly:grob-array->list all))))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEV BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

zeroStaffStaff = \layout {
  \context {
    \Staff
    \override VerticalAxisGroup.default-staff-staff-spacing =
      #'((basic-distance . 0) (minimum-distance . 0) (padding . 1))
  }
}

%% TNESTC THE CONTROL: no inner tuplet.
\book {
  \probeTag "TNESTC"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff {
        \repeat unfold 2 { \tuplet 3/2 { c''4 c''4 c''4 } c''2 }
        c''1 \bar "|."
      }
      \new Staff { \clef bass d1 | d1 | d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

%% TNESTN the inner tuplet with its bracket drawn.
\book {
  \probeTag "TNESTN"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff {
        \repeat unfold 2 { \tuplet 3/2 { c''4 \tuplet 3/2 { c''8 r8 c''8 } c''4 } c''2 }
        c''1 \bar "|."
      }
      \new Staff { \clef bass d1 | d1 | d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

%% TNESTB the inner tuplet beamed: its number is the point.
\book {
  \probeTag "TNESTB"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff {
        \repeat unfold 2 { \tuplet 3/2 { c''4 \tuplet 3/2 { c''8 c''8 c''8 } c''4 } c''2 }
        c''1 \bar "|."
      }
      \new Staff { \clef bass d1 | d1 | d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}

%% TNESTI the same ratio over the same notes: one tuplet.
\book {
  \probeTag "TNESTI"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    <<
      \new Staff {
        \repeat unfold 2 { \tuplet 3/2 { \tuplet 3/2 { c''4 r4 c''4 } } c''2 \tuplet 3/2 { c''4 } }
        c''1 \bar "|."
      }
      \new Staff { \clef bass d1 | d1 | d1 \bar "|." }
    >>
    \layout { \zeroStaffStaff }
  }
}
