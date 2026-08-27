\version "2.26.0"
%% LP FIDELITY PROBE — WHERE THE FIRST STAFF SITS UNDER CHORD NAMES IN A GRAND STAFF.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe chords-grand-page.ly -Prefix PROBEG
%%
%% THE REGIME THIS OPENS (2026-08-28, session 271). page.chord-row.* (books CHR1/CHR2,
%% page-vertical.ly) pinned the top spring under a chord row over a PLAIN staff, and the
%% Lily#-side readings there are ink-true (residuals 0.001435575 / 2.83e-07): the row band
%% machinery reserves the symbols' real ink. The GRAND-STAFF spelling of the same
%% arrangement (\chords inside \new GrandStaff — lilypond-src/input/regression/
%% chord-names-in-grand-staff.ly, whose Lily# twin is audit/lp-regression/lys/
%% chord-names-in-grand-staff.lys) takes a DIFFERENT path in Lily#: the chords flow
%% through LayoutEngine's per-measure annotation extents, whose UP term is the scalar
%% pair [cnY - 1.9, cnY + 0.3] (EnrichExtentsWithAnnotationProtrusions' chord arm) —
%% HANDOFF 7.7's flat box standing beside real ink. MEASURED 2026-08-28 by poison sweep:
%% at +-0.03 that scalar moves exactly ONE of 572 tracked books (the grand-staff twin,
%% whose 'F' inks to 1.907250371, margin 0.007) and ZERO ledger entries — the scalar is a
%% DOMINATED shadow term today (every chord letter is a capital and inks above 1.9), and
%% nothing pins the grand-staff page anchor itself against LilyPond.
%%
%% THE PAIR (HANDOFF 5.0-1): GCF and GCS are ONE VARIABLE apart — f1 against fis1, so
%% every symbol's ink grows (the sharp adds 0.317582017716528 above the baseline and
%% 0.953516784923366 below it, 1.271098802639894 of ink height — the mark.over-chord
%% numbers) and nothing else moves. Same staves, same music, same paper, no titles.
%%
%% PREDICTION, written before running (HANDOFF 5.0-2), mechanism first: the ChordNames
%% line inside the GrandStaff is a non-spaceable line of the group's run, and the page's
%% top spring is max(basic-distance 6, ink above the first spaceable staff's refpoint +
%% padding 1) — scm/define-grobs.scm top-system-spacing via page-layout-problem. The
%% chord's baseline is placed clear of the rh staff by the line's own
%% nonstaff-relatedstaff-spacing, so a DESCENDER (the sharp's tail) lifts the baseline
%% and the raised sharp lifts the top: GCS - GCF should be the symbol's WHOLE ink-height
%% growth, 1.271099 — the same identity CHR1/CHR2 read over a plain staff, because the
%% quantity is the group's skyline and a GrandStaff bracket changes none of it.
%% FALSIFIER, and it is what the pair exists to catch: a difference of only the TOP
%% growth (0.317582) means the grand-staff run pins the baseline by spacing-spec rather
%% than by ink clearance, and the port target is a different quantity than the row's.
%% ⚠️ THE LILY#-SIDE PREDICTION IS ALREADY MEASURED (scratch/p271/act3-lilysharp.txt,
%% taken before this probe ran): GCF 12.097801371, GCS 13.368940283 — difference
%% 1.271138912, the ink growth in Lily#'s own faces (face term 0.000040110). So if
%% LilyPond confirms the identity, BOTH engines track the ink here and the entries carry
%% face/baseline terms, not a lift error; the scalar's remaining defect is that it is a
%% SECOND spelling (HANDOFF 5.2.1-2), retired by an output-identity refactor, not a port.
%%
%% Everything printed is in STAFF SPACES (see page-vertical.ly's header for why).
%% first STAFF refpoint below the paper edge = top-margin + Y-offset - staffUp,
%% the same arithmetic Measure-LilyPondPageGeometry.ps1 does for PROBEV lines.

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
                          (if (memq nm '(ChordName StaffSymbol))
                              (format #t "PROBEG ~a GROB ~a rel=~a ext=(~a . ~a)\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))

#(define (dump-pages tag layout pages)
   (let ((tm (ly:output-def-lookup layout 'top-margin)))
     (format #t "PROBEG ~a top-margin=~a\n" tag tm)
     (for-each
      (lambda (page)
        (for-each
         (lambda (sys)
           (let* ((y (ly:prob-property sys 'Y-offset 0.0))
                  (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0))))
             (format #t "PROBEG ~a SYS y=~a staff=(~a . ~a) first-staff-refpoint-below-edge=~a\n"
                     tag y (car staff) (cdr staff)
                     (+ tm y (- (cdr staff))))))
         (ly:prob-property page 'lines)))
      pages)
     (dump-grobs tag layout pages)))

probeG =
#(define-scheme-function (tag) (string?)
   #{ \paper { ragged-bottom = ##t
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEG BOOK ~a\n" tag)
                                      (dump-pages tag layout pages)) } #})

%% GCF — the plain pair half: LilyPond's own regression arrangement, one bar.
%% Lily# a / a,, (octave absolute, treble / bass) = LilyPond a' / a, (HANDOFF 5.5).
\book {
  \probeG "GCF"
  \score {
    \new GrandStaff
    <<
      \chords { f1 }
      \new Staff { a'4 a' a' a' }
      \new Staff { \clef "bass" a,4 a, a, a, }
    >>
  }
}

%% GCS — GCF with the chord sharped and NOTHING else changed.
\book {
  \probeG "GCS"
  \score {
    \new GrandStaff
    <<
      \chords { fis1 }
      \new Staff { a'4 a' a' a' }
      \new Staff { \clef "bass" a,4 a, a, a, }
    >>
  }
}
