\version "2.26.0"
%% LP FIDELITY PROBE — how WIDE is a fingering digit, and which glyph is it? (2026-08-11,
%% session 134, round 1).
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe fingering-digit-width.ly (five books, ~20 s).
%%
%% WHY THIS EXISTS
%%
%% HANDOFF §1 "next hand ⒜" was seeded by fingering-slur.ly's remark, which said: Lily#'s
%% FingeringEngraver.DigitRun answers the ADVANCE where LilyPond answers the INK, LP reading
%% xext = (0.0 . 0.819439) for a "1" against Lily#'s ~0.90.
%%
%% ⚠️⚠️ THAT DIAGNOSIS IS FALSIFIED, and this probe is what falsified it. LilyPond's box for a
%% TEXT stencil is X = the LOGICAL rect and Y = the INK rect — lily/pango-font.cc:358-360,
%% `Box string_extent (Interval (PANGO_LBEARING (logical_rect), PANGO_RBEARING (logical_rect)),
%% Interval (-PANGO_DESCENT (ink_rect), PANGO_ASCENT (ink_rect)))`. So the LEFT edge is the pen
%% origin (which is why every reading below starts at 0.0, and no digit's left side bearing
%% ever shows) and the RIGHT edge is an ADVANCE. Lily#'s box has the RIGHT SHAPE. What is wrong
%% is WHICH advance, and it is wrong three separate ways.
%%
%% THE RULE, read from the source before measuring:
%%   Fingering  (scm/define-grobs.scm:1547-1548): font-encoding fetaText,
%%              font-features ("cv47" "ss01"),         font-size -5
%%   BassFigure (scm/define-grobs.scm:352-356):   font-encoding fetaText,
%%              font-features ("tnum" "cv47" "ss01"),  font-size -5
%% ⇒ THEY DIFFER BY tnum = TABULAR FIGURES = the .fixedwidth cut. A fingering is set in the
%% PROPORTIONAL digits and a figure in the fixed-width ones. Lily# feeds the fingering the
%% figured bass's run (FiguredBassGlyphRun), i.e. the fixed-width cut, so every digit is as
%% wide as the widest.
%%
%% ★★★ MEASURED, not deduced — `ly:stencil-expr` names the glyph AND the font file
%% (scratch/fingering-digit-width.ly, the ten-digit sighting this probe was cut down from):
%%   Fingering  "1" -> (glyph-string … emmentaler-11.otf … fattened.one)
%%   BassFigure "1" -> (glyph-string … emmentaler-11.otf … fattened.fixedwidth.one)
%%   Fingering  "4" -> fattened.four.alt      "7" -> fattened.seven.alt   (cv47's own pair)
%% So the cut is confirmed on the page, and so is the OPTICAL DESIGN: a fingering asks for
%% 20 · magstep(-5) = 11.2246 pt, which lands on emmentaler-11 and NOT on the 20 design whose
%% table Lily# reads (lily/font-select.cc:41-70 best_rounded_design_size —
%% EmmentalerDesignSize already spells this rule; DigitRun just never asked it).
%%
%% THE THIRD WAY, and the one that makes every reading below a whole number of pixels: Pango
%% hints an advance to a device pixel at PANGO_RESOLUTION 1200 (lily/include/pango-font.hh:75),
%% so the width in staff spaces is an integer multiple of 0.034143307086614. All five readings
%% here are exactly 21, 23, 24, 25 and 27 of them.
%%
%% ⇒ THE WHOLE MODEL, and it is not fitted:
%%      width = quantise( advance(fattened.<digit>, emmentaler-11) · magstep(-5) )
%%    Checked against LilyPond's own ten digits (0-9) before this probe was written: all TEN
%%    agree to DOUBLE PRECISION (diff 0.000E+000), including the two .alt glyphs. The Lily#
%%    mirror written ahead of the port is in the ledger whys.
%%
%% THE BOOKS. Five, one per DISTINCT pixel width, so no two points read the same number:
%%   FD7 — 21 px (0.717009…) the NARROWEST digit, and a cv47 .alt glyph
%%   FD3 — 23 px (0.785296…) the crowd (3, 5, 6 and 9 all land here)
%%   FD1 — 24 px (0.819439…) the digit fingering-slur.ly's four books use
%%   FD0 — 25 px (0.853583…) (0, 2 and 8)
%%   FD4 — 27 px (0.921869…) the WIDEST, the other .alt glyph — and the ONLY one where Lily#
%%         is too NARROW, so the sign of the residual flips. A "shrink the box" fix fitted to
%%         the other four would move this one the wrong way.
%% Ten books would carry no more information than five: the widths repeat.
%%
%% THE QUANTITY is the digit's box LEFT relative to its own notehead's anchor. A Fingering is
%% self-alignment-X = CENTER on its head and what that centres on is the head's own ink extent
%% (lily/self-alignment-interface.cc:147), so the reading is 1.3042/2 - width/2 = 0.6521 -
%% width/2: it observes the WIDTH directly, through the consumer that PLACES the run. The
%% existing point fingering.whole.column-to-ink-centre reads the same run's CENTRE and is
%% exact — and stays exact through this port, because half a wrong width cancels there. That
%% pair is the reason a centre point could not have caught this.
%%
%% THE MUSIC IS GENERATED, NOT WRITTEN (HANDOFF: the octave trap) — `lysc ly` on the .lys twins
%% recorded in LpGeometryProbes.cs (FingeringDigitScore).
%%
%% MEASURED: see the ledger entries fingering.digit.* .

#(define (probe-dump-pages layout pages)
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let* ((sys (car ls)))
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    ;; The NoteHead rides along because the quantity is a
                                    ;; DIFFERENCE: the digit's box left minus the head's
                                    ;; anchor. Printing only the digit would make the reading
                                    ;; depend on where the system happened to start.
                                    (if (memq nm '(Fingering NoteHead))
                                        (format #t "PROBEF GROB ~a name=~a text=~a x=~a xext=(~a . ~a) yext=(~a . ~a)\n"
                                                n nm
                                                (if (eq? nm 'Fingering)
                                                    (ly:grob-property g 'text)
                                                    "-")
                                                (ly:grob-relative-coordinate g sg X)
                                                (car (ly:grob-extent g g X))
                                                (cdr (ly:grob-extent g g X))
                                                (car (ly:grob-extent g g Y))
                                                (cdr (ly:grob-extent g g Y))))))
                                (ly:grob-array->list all))))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEF BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

%% FD7 — generated from fd7.lys by `lysc ly`; the NARROWEST digit (fattened.seven.alt, 21 px).
\book {
  \probeTag "FD7"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 4/4 \key c \major g'4-7 r4 r2 | } }
    \layout { indent = 0\mm }
  }
}

%% FD3 — generated from fd3.lys; the width 3, 5, 6 and 9 share (fattened.three, 23 px).
\book {
  \probeTag "FD3"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 4/4 \key c \major g'4-3 r4 r2 | } }
    \layout { indent = 0\mm }
  }
}

%% FD1 — generated from fd1.lys; the digit the fingering-slur books use (fattened.one, 24 px).
\book {
  \probeTag "FD1"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 4/4 \key c \major g'4-1 r4 r2 | } }
    \layout { indent = 0\mm }
  }
}

%% FD0 — generated from fd0.lys; the width 0, 2 and 8 share (fattened.zero, 25 px).
\book {
  \probeTag "FD0"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 4/4 \key c \major g'4-0 r4 r2 | } }
    \layout { indent = 0\mm }
  }
}

%% FD4 — generated from fd4.lys; the WIDEST (fattened.four.alt, 27 px), and the one whose
%% residual has the OTHER sign.
\book {
  \probeTag "FD4"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 4/4 \key c \major g'4-4 r4 r2 | } }
    \layout { indent = 0\mm }
  }
}
