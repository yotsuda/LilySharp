\version "2.26.0"
%% LP FIDELITY PROBE — WHAT A DYNAMIC UNDER A ROW-LED SYSTEM CHARGES THE PAIR GAP.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe dynamics-row-page.ly -Prefix PROBEDY
%%
%% THE REGIME THIS OPENS (2026-08-28, session 272 act 2 — the dynamics half of the
%% row-leading FRAME family that books CTW/CTWN/CTWO opened for the custom text).
%% Lily#'s dynamics silhouette arm translates a dynamic's Y in ONE step
%% (dY = YUp − StaffMiddle), right exactly when the system opens on a staff; a chord
%% row LEADING the system moves the system origin above the staff and the box floats
%% up with it — the float the mark arm fixed on 2026-08-25 (two-step
%% ScoreGrobStaffTopUp). For a BELOW-staff dynamic the float is the MIRROR of CTW's:
%% the box rises OUT of the system's down silhouette, so the pair gap under it is
%% UNDER-charged — the collision direction, not the too-much-air direction.
%%
%% THE BOOKS (HANDOFF 5.0①), Lily# measured BEFORE this probe ran
%% (scratch/p272/predictions.txt act 2):
%%   DYW  — system 1 is the deep g-column line with \pp on bar 1's first note AND a
%%          chord row (chords on bars 1-2 only); system 2 the a''' line.
%%          Lily# reads gap-first 13.090000000 — EXACTLY its no-dynamic twin DYWO,
%%          i.e. THE DYNAMIC HAS VANISHED from the gap: its box floats up by the
%%          row's origin offset and the note-note term (5.045 + 7.045 + 1) binds
%%          instead.
%%   DYWN — the row taken out, nothing else changed. Lily# 15.442000000: the same
%%          dynamic properly deep charges the gap 2.352 over the note-note term.
%%   DYWO — the dynamic taken out, nothing else changed. Lily# 13.090000000 (the
%%          CTGN geometry re-read: g-bottom 5.045 + a''' top 7.045 + padding 1).
%%
%% PREDICTION, written before running: LilyPond reads DYW = DYWN IDENTICALLY — the
%% below-staff DynamicText and the above-staff chord row live on opposite sides of
%% system 1 and do not interact; the row's top (~5.94 above the refpoint with
%% padding) is under the top spring's basic-distance 6, so it does not even move
%% the page top, and the pair gap charges the dynamic's REAL ink bottom against the
%% a''' tops (lily/page-layout-problem.cc build_system_skyline — the system skyline
%% contains the DynamicText grob like any other outside-staff stencil). DYWO is
%% predicted at 13.090000 exact, the note-note control both engines read from real
%% ink (its custom-text sibling CTGN landed 0 exact).
%% FALSIFIER, and what CTW already taught once: DYW ≠ DYWN in LilyPond means the
%% row DOES reach the pair gap (loose-line redistribution, the subsystem Lily#
%% deliberately lacks — page-layout-problem.cc:860-880) and the residual story must
%% be re-derived before any ledgering.
%% THE FIX'S LANDING: when the dynamics arm's frame goes two-step, Lily#'s DYW must
%% land on its own DYWN reading (15.442000000) — the residual moves from the float
%% to DYWN's face terms, and DYWO must not move at all.
%%
%% Everything printed is in STAFF SPACES (see page-vertical.ly's header for why).
%% first STAFF refpoint below the paper edge = top-margin + Y-offset - staffUp.
%% ⚠️ Lily# g, / a'' (octave absolute) = LilyPond g / a''' (HANDOFF 5.5).

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
                          (if (memq nm '(DynamicText ChordName StaffSymbol))
                              (format #t "PROBEDY ~a GROB ~a rel=~a ext=(~a . ~a)\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))

#(define (dump-pages tag layout pages)
   (let ((tm (ly:output-def-lookup layout 'top-margin)))
     (format #t "PROBEDY ~a top-margin=~a\n" tag tm)
     (for-each
      (lambda (page)
        (for-each
         (lambda (sys)
           (let* ((y (ly:prob-property sys 'Y-offset 0.0))
                  (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0))))
             (format #t "PROBEDY ~a SYS y=~a staff=(~a . ~a) first-staff-refpoint-below-edge=~a\n"
                     tag y (car staff) (cdr staff)
                     (+ tm y (- (cdr staff))))))
         (ly:prob-property page 'lines)))
      pages)
     (dump-grobs tag layout pages)))

probeDY =
#(define-scheme-function (tag) (string?)
   #{ \paper { ragged-bottom = ##t
               indent = 0
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEDY BOOK ~a\n" tag)
                                      (dump-pages tag layout pages)) } #})

%% DYW — the row-led system with the below-staff dynamic. Lily#: section A carries
%% chords prog { C | C | } and g,4@pp; the row leads system 1.
\book {
  \probeDY "DYW"
  \score {
    <<
      \chords { c1 c1 s1 s1 }
      \new Staff { g4\pp g g g | g4 g g g | \break
                   a'''4 a''' a''' a''' |
                   a'''4 a''' a''' a''' }
    >>
  }
}

%% DYWN — DYW with the row taken out and nothing else changed (dynamic, no row).
\book {
  \probeDY "DYWN"
  \score {
    \new Staff { g4\pp g g g | g4 g g g | \break
                 a'''4 a''' a''' a''' |
                 a'''4 a''' a''' a''' }
  }
}

%% DYWO — DYW with the dynamic taken out and nothing else changed (row, no dynamic).
\book {
  \probeDY "DYWO"
  \score {
    <<
      \chords { c1 c1 s1 s1 }
      \new Staff { g4 g g g | g4 g g g | \break
                   a'''4 a''' a''' a''' |
                   a'''4 a''' a''' a''' }
    >>
  }
}
