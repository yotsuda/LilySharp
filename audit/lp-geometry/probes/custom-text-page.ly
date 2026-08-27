\version "2.26.0"
%% LP FIDELITY PROBE — WHAT A CUSTOM TEXT (TextScript) CHARGES THE PAGE.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe custom-text-page.ly -Prefix PROBECT
%%
%% THE REGIME THIS OPENS (2026-08-28, session 272 — map step (3) of scratch/p271/
%% sweep-map.txt). Lily#'s custom text (`_"text"`, drawn as an above-staff italic
%% TextScript at em 2.2) reaches the PAGE through two arms that both carry a scalar
%% pair [1.8 up, 0.6 down] about the placed baseline: the per-measure annotation
%% extents (LayoutEngine.PagingSkylines.cs:271, Add(ctY − 1.8, ctY + 0.6)) and the
%% X-aware inter-system silhouette (:945, AddMarkBox(.., ctY + 1.8, ctY − 0.6)).
%% 1.8 / 0.6 = 0.75 / 0.25 × em 2.4 — the letter-class trio (TextAscentEm /
%% TextDescentEm, "no single LP grob source") that the outside-staff stacker RETIRED
%% for real outlines (OutsideStaffStacker.cs:155-156), and whose em has since moved
%% to 2.2 (TextScriptFontSize): the paging arms are the last two readers of the dead
%% constants' values. A ±0.03 poison sweep (session 271) found ZERO of 572 tracked
%% books observing either arm — the regime had no observer at all. These books are
%% the observers. LilyPond has no envelope to port: what joins its system skyline
%% and its page springs is the TextScript grob's own stencil skyline
%% (lily/axis-group-interface.cc:359-474), the same story as the mark's 0.8 envelope
%% (page.section-label.first-staff-refpoint) and the chord estimate's 1.9 scalar
%% (page.grand-chords.*).
%%
%% THE PAIRS (HANDOFF 5.0-1), all with the LILY# SIDE MEASURED FIRST
%% (scratch/p272/predictions.txt, taken before this probe ran):
%%
%% CTP/CTC — the PAGE ANCHOR pair, one variable apart: italic "meno mosso" over a
%%   bar of a''' against "Meno mosso" (the capital M is the only change; neither
%%   string has a descender, so the stacked baseline is common). The text stacks
%%   over the high notes and its ink top is the page's first-system up extent.
%%   PREDICTION: LP's refpoint-below-edge = top-margin + (stacked baseline + the
%%   string's real ink top) + padding 1, so CTC − CTP = the M's ink-top growth over
%%   the x-height in LP's own faces. Lily# ALREADY MEASURED both halves at
%%   16.028551000 — difference EXACTLY 0.000000000, because the scalar prices every
%%   string at a flat 1.8 over the baseline (16.028551 = margin 5.690551 + baseline
%%   7.538000 + 1.8 + padding 1). The pair difference IS the scalar's flatness, and
%%   the per-half residual is expected ≈ +(1.8 − x-height) ≈ +0.7..0.8 on CTP —
%%   the mark island's magnitude — and smaller on CTC (its ink is taller).
%%   Divergence side: CTP diverges MORE.
%%
%% CTG/CTGC/CTGN — the same identity on an INTER-SYSTEM gap: system 1 is a deep
%%   g-column line, system 2 the a''' line with the text on bar 4's first note, so
%%   the X-aware distance binds sys1's notehead bottoms against the text's ink top.
%%   PREDICTION: CTGC − CTG = the same M growth; CTGN (no text) pins the note-note
%%   term, which both engines read from real note ink (face-term-small residual).
%%   Lily# ALREADY MEASURED: CTG = CTGC = 15.383000000 (Δ 0 exactly, the same
%%   flatness; 15.383000 = sys1 down 5.045000 + (baseline 7.538000 + 1.8) + 1.0),
%%   CTGN = 13.090000000 (= 5.045000 + a''' ink top 7.045000 + 1.0).
%%
%% CTW/CTWN/CTWO — the ROW-LEADING FRAME books (the float the mark arm fixed on
%%   2026-08-25): a chord row LEADS system 2 (chords on bars 3-4 only) and the text
%%   stands on system 2 under it. Lily#'s silhouette arm translates the text's Y in
%%   ONE step (ctY = YUp − StaffMiddle, PagingSkylines.cs:944), right exactly when
%%   the system opens on a staff — the row moves the system origin above the staff
%%   and the box floats up with it. Lily# ALREADY MEASURED: CTW = 14.342092602,
%%   CTWN (text, no row) = CTWO (row, no text) = 12.000000000 — NEITHER ingredient
%%   does it alone, together they cost 2.342092602 (the ROWM shape, session 253).
%%   PREDICTION: LilyPond reads 12.000000 EXACT for ALL THREE — the row's ink and
%%   the text's ink are each far under the system-system basic-distance 12 floor,
%%   and LilyPond has no frame to garble (the TextScript's stencil joins the system
%%   skyline where it is drawn). FALSIFIER, and what the pair exists to catch: an
%%   LP reading above 12 on CTW means the row and text DO combine inside LilyPond's
%%   gap, and the frame-float story must be re-derived before any port.
%%
%% Everything printed is in STAFF SPACES (see page-vertical.ly's header for why).
%% first STAFF refpoint below the paper edge = top-margin + Y-offset - staffUp,
%% the same arithmetic Measure-LilyPondPageGeometry.ps1 does for PROBEV lines.
%% ⚠️ Lily# a'' / g, / g / a (octave absolute) = LilyPond a''' / g / g' / a'
%% (HANDOFF 5.5).

#(define (dump-grobs tag layout pages)
   (for-each
    (lambda (page)
      (for-each
       (lambda (sys)
         (let ((sg (ly:prob-property sys 'system-grob)))
           (if (ly:grob? sg)
               (let ((all (ly:grob-object sg 'all-elements)))
                 (if (ly:grob-array? all)
                     (for-each
                      (lambda (g)
                        (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                          (if (memq nm '(TextScript ChordName StaffSymbol))
                              (format #t "PROBECT ~a GROB ~a rel=~a ext=(~a . ~a)\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))

#(define (dump-pages tag layout pages)
   (let ((tm (ly:output-def-lookup layout 'top-margin)))
     (format #t "PROBECT ~a top-margin=~a\n" tag tm)
     (for-each
      (lambda (page)
        (for-each
         (lambda (sys)
           (let* ((y (ly:prob-property sys 'Y-offset 0.0))
                  (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0))))
             (format #t "PROBECT ~a SYS y=~a staff=(~a . ~a) first-staff-refpoint-below-edge=~a\n"
                     tag y (car staff) (cdr staff)
                     (+ tm y (- (cdr staff))))))
         (ly:prob-property page 'lines)))
      pages)
     (dump-grobs tag layout pages)))

probeCT =
#(define-scheme-function (tag) (string?)
   #{ \paper { ragged-bottom = ##t
               indent = 0
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBECT BOOK ~a\n" tag)
                                      (dump-pages tag layout pages)) } #})

%% CTP — the page-anchor half with x-height ink: one bar of a''', the text on the
%% first note. Lily#: section A { melody { a''4 a'' a'' a'' | } }, form ~A _"meno mosso".
\book {
  \probeCT "CTP"
  \score {
    \new Staff { a'''4^\markup \italic "meno mosso" a''' a''' a''' }
  }
}

%% CTC — CTP with the M capitalized and NOTHING else changed.
\book {
  \probeCT "CTC"
  \score {
    \new Staff { a'''4^\markup \italic "Meno mosso" a''' a''' a''' }
  }
}

%% CTG — the gap half: system 1 deep g columns, system 2 a''' with the text on
%% bar 4's first note. Lily# breaks after bar 2 (`break`), so \break here.
\book {
  \probeCT "CTG"
  \score {
    \new Staff { g4 g g g | g4 g g g | \break
                 a'''4 a''' a''' a''' |
                 a'''4^\markup \italic "meno mosso" a''' a''' a''' }
  }
}

%% CTGC — CTG with the M capitalized and NOTHING else changed.
\book {
  \probeCT "CTGC"
  \score {
    \new Staff { g4 g g g | g4 g g g | \break
                 a'''4 a''' a''' a''' |
                 a'''4^\markup \italic "Meno mosso" a''' a''' a''' }
  }
}

%% CTGN — CTG with NO text: the note-note control that pins the gap's other terms.
\book {
  \probeCT "CTGN"
  \score {
    \new Staff { g4 g g g | g4 g g g | \break
                 a'''4 a''' a''' a''' |
                 a'''4 a''' a''' a''' }
  }
}

%% CTW — the row-leading frame book: a chord row on system 2 only (bars 3-4), the
%% text on system 2 under it. Lily#: section B carries chords prog { C | C | }.
\book {
  \probeCT "CTW"
  \score {
    <<
      \chords { s1 s1 c1 c1 }
      \new Staff { g4 g g g | g4 g g g | \break
                   g'4 a' g' a' |
                   g'4^\markup \italic "meno mosso" a' g' a' }
    >>
  }
}

%% CTWN — CTW with the row taken out and nothing else changed (text, no row).
\book {
  \probeCT "CTWN"
  \score {
    \new Staff { g4 g g g | g4 g g g | \break
                 g'4 a' g' a' |
                 g'4^\markup \italic "meno mosso" a' g' a' }
  }
}

%% CTWO — CTW with the text taken out and nothing else changed (row, no text).
\book {
  \probeCT "CTWO"
  \score {
    <<
      \chords { s1 s1 c1 c1 }
      \new Staff { g4 g g g | g4 g g g | \break
                   g'4 a' g' a' |
                   g'4 a' g' a' }
    >>
  }
}
