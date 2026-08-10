\version "2.26.0"
%% LP FIDELITY PROBE — a SLUR gets out of a SCRIPT's way (2026-08-10, session 132, round 3).
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe slur-script.ly (two books, ~8 s).
%%
%% WHY THIS EXISTS
%%
%% The user's third sighting on the side-by-side report, after the fingering island was
%% ported: on corpus book chord-repetition the SECOND slur runs straight through the last
%% staccatissimo in Lily#, and in LilyPond it does not.
%%
%% THE RULE, read from the source before measuring. A staccatissimo declares
%% (avoid-slur . inside) — scm/script.scm:386-389, one of the eleven scripts that do — so it
%% is NOT moved out of the bow's way (that is what 'outside and 'around mean, and it is the
%% direction Lily# already ports through outside_slur_callback). It stays where side-position
%% put it and the SLUR is scored around it: slur-scoring.cc:695-704 puts an 'inside
%% encompass-object's extent edge into the avoid list, and slur-configuration.cc scores every
%% candidate attachment against it. ⇒ THE TWO DIRECTIONS ARE DIFFERENT MECHANISMS, and Lily#
%% has exactly one of them.
%%
%% THE BOOKS (the quantity is control-points[0].y, the bow's attachment, about the staff
%% refpoint — up-positive, so "more negative" is "further below the staff"):
%%   SSC — one slur over two chords, the SECOND carrying the staccatissimo.
%%   SSN — the CONTROL: the same two chords, the same slur, no script.
%% Minimal on purpose: the sighting is on a book with two slurs, two scripts, three
%% fingerings, a dynamic and a beam, and measuring there would price all of them at once.
%% ⚠️ It was checked against that book rather than assumed to stand for it: in the corpus
%% texture the same difference is 1.000000 as well, and removing the fingerings and the
%% dynamic changes neither reading (scratch slur-probe.ly, books SLS/SLN/SLB).
%%
%% ⚠️ THESE LP NUMBERS WERE MEASURED IN SCRATCH FIRST, as the diagnosis of the sighting, so
%% they are not a prediction that stood a test — the same disclosure round 2 of
%% chord-fingering.ly carries. What is written before its run is the Lily# mirror, in the
%% ledger whys.
%%
%% MEASURED (LilyPond 2.26.0): SSC -5.045000, SSN -4.045000.
%%   The control's -4.045000 is the plain base attachment: the lowest head's ink bottom
%%   -3.545000 less half a staff space (slur-scoring.cc:555-557, `y = head extent[dir]` then
%%   `y += dir * 0.5 * staff_space`). With the script there the bow drops by EXACTLY
%%   1.000000 — one staff space, the scorer's own variation step, not "the script's ink plus
%%   a padding": the script's ink bottom is at -4.797000 and the chosen attachment clears it
%%   by 0.248000, which no padding in this island spells. ⇒ what is ported is a SCORED
%%   avoidance, and a constant would reproduce this book while missing every other one.

#(define (probe-dump-pages layout pages)
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let* ((sys (car ls))
                        (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0))))
                   (format #t "PROBEC SYS ~a ~a staff=(~a . ~a)\n"
                           n i (car staff) (cdr staff))
                   ;; The Slur rides with the Script, the heads and the staff symbol so the
                   ;; reading can be DECOMPOSED: rel is the grob about the SYSTEM refpoint,
                   ;; ext its own ink, and a Slur prints its control-points, which is the
                   ;; quantity — the bow's ATTACHMENT, not the extent its arc happens to
                   ;; reach.
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (memq nm '(Slur Script NoteHead StaffSymbol))
                                        (format #t "PROBEC GROB ~a ~a name=~a text=~a rel=~a ext=(~a . ~a) x=~a\n"
                                                n i nm
                                                (if (eq? nm 'Slur) (ly:grob-property g 'control-points) "-")
                                                (ly:grob-relative-coordinate g sg Y)
                                                (car (ly:grob-extent g g Y))
                                                (cdr (ly:grob-extent g g Y))
                                                (ly:grob-relative-coordinate g sg X)))))
                                (ly:grob-array->list all))))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEC BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})



%% SSC — generated from sls.lys by `lysc ly`; the second chord carries the staccatissimo.
\book {
  \probeTag "SSC"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 4/4 \key c \major
      <c e g>4 ( <c e g>-\staccatissimo ) r2 | } }
    \layout { indent = 0\mm }
  }
}

%% SSN — generated from sln.lys; the control, no script anywhere.
\book {
  \probeTag "SSN"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 4/4 \key c \major
      <c e g>4 ( <c e g> ) r2 | } }
    \layout { indent = 0\mm }
  }
}
