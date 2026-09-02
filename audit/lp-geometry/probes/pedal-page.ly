\version "2.26.0"
%% LP FIDELITY PROBE — WHAT A PEDAL BRACKET UNDER A SYSTEM CHARGES THE PAIR GAP.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe pedal-page.ly -Prefix PROBEPD
%%
%% THE REGIME THIS OPENS (2026-09-02, session 320 — user report, petite-valse.lys):
%% a sustain bracket under the LAST staff of system 1 printed through the trill and
%% the fermata standing above the FIRST staff of system 2. Lily# solves the bracket
%% per staff and per system (PedalEngraver.SolveAndSeed — ledger
%% lyrics.pedal-bracket.staff-to-lyric) and seeds it into the STAFF's down profile,
%% the one the lyric floor and the staff-to-staff springs read — but not into the
%% PAGING silhouette (LayoutEngine.AugmentSkylinesForPaging), the one the spring
%% BETWEEN two systems reads through the X-aware Distance(). The dynamics arm and the
%% text-spanner arm were each found missing from that silhouette the same way
%% (page.dynamics.leading-row.*, 2026-08-28; TextSpannerSystemSpacingTests,
%% 2026-08-30); this is the pedal's turn.
%%
%% THE BOOKS (HANDOFF 5.0①), Lily# to be measured BEFORE this probe runs:
%%   PDB  — system 1 is a c'' line (stems down, ink bottom 2.5 below the refpoint)
%%          with a sustain bracket from bar 1's first note to bar 2's last; system 2
%%          the a''' line (ink top 7.045 above its refpoint, the same line the DYW
%%          family measures against).
%%   PDBN — the bracket taken out, nothing else changed: the note-note control, which
%%          both engines should read from the basic-distance floor (2.5 + 7.045 + 1 =
%%          10.545 < 12).
%%
%% PREDICTION, written before running: LilyPond's PDB charges the bracket's LINE —
%% the bracket sits at support ink 2.5 + padding 1.2 + edge-height 1.0 below the
%% refpoint (SustainPedalLineSpanner padding, PianoPedalBracket edge-height, the
%% decomposition lyrics.pedal-bracket.staff-to-lyric measured to the digit), so the
%% pair gap reads ~4.75 + 7.045 + 1 ≈ 12.8, above the floor; PDBN reads 12.000000
%% exactly. Lily# BEFORE the fix reads 12.000000 for BOTH (the bracket is nowhere in
%% the paging silhouette, so the pair sits on the floor and the bracket is drawn into
%% the next system's ink). THE FIX'S LANDING: PDB moves onto LilyPond's number and
%% PDBN must not move.
%% FALSIFIER: PDB = PDBN in LilyPond means the bracket does NOT reach the page
%% (e.g. it hangs in a context whose skyline the page ignores) and the fix must be
%% re-derived before any ledgering.
%%
%% Everything printed is in STAFF SPACES (see page-vertical.ly's header for why).
%% first STAFF refpoint below the paper edge = top-margin + Y-offset - staffUp.
%% ⚠️ Lily# c' / a'' (octave absolute) = LilyPond c'' / a''' (HANDOFF 5.5).
%% ⚠️ pedalSustainStyle is 'text by default in LilyPond and bracket in Lily#
%% (RenderSpec.StaffSpec.PedalStyle), so the probe sets the bracket explicitly.

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
                          (if (memq nm '(PianoPedalBracket SustainPedalLineSpanner StaffSymbol))
                              (format #t "PROBEPD ~a GROB ~a rel=~a ext=(~a . ~a)\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))

#(define (dump-pages tag layout pages)
   (let ((tm (ly:output-def-lookup layout 'top-margin)))
     (format #t "PROBEPD ~a top-margin=~a\n" tag tm)
     (for-each
      (lambda (page)
        (for-each
         (lambda (sys)
           (let* ((y (ly:prob-property sys 'Y-offset 0.0))
                  (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0))))
             (format #t "PROBEPD ~a SYS y=~a staff=(~a . ~a) first-staff-refpoint-below-edge=~a\n"
                     tag y (car staff) (cdr staff)
                     (+ tm y (- (cdr staff))))))
         (ly:prob-property page 'lines)))
      pages)
     (dump-grobs tag layout pages)))

probePD =
#(define-scheme-function (tag) (string?)
   #{ \paper { ragged-bottom = ##t
               indent = 0
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEPD BOOK ~a\n" tag)
                                      (dump-pages tag layout pages)) } #})

%% PDB — the sustain bracket under system 1. Lily#: c'4@sustain … c'@!sustain | break.
\book {
  \probePD "PDB"
  \score {
    \new Staff { \set Staff.pedalSustainStyle = #'bracket
                 c''4\sustainOn c'' c'' c'' | c''4 c'' c'' c''\sustainOff | \break
                 a'''4 a''' a''' a''' |
                 a'''4 a''' a''' a''' }
  }
}

%% PDBN — PDB with the bracket taken out and nothing else changed (the control).
\book {
  \probePD "PDBN"
  \score {
    \new Staff { c''4 c'' c'' c'' | c''4 c'' c'' c'' | \break
                 a'''4 a''' a''' a''' |
                 a'''4 a''' a''' a''' }
  }
}
