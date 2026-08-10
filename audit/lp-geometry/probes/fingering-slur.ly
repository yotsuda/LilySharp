\version "2.26.0"
%% LP FIDELITY PROBE — a FINGERING gets out of a SLUR's way (2026-08-11, session 133, round 4).
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe fingering-slur.ly (four books, ~10 s).
%%
%% WHY THIS EXISTS
%%
%% HANDOFF §1: on corpus book chord-repetition the down digit sits under the slur in Lily#,
%% and the sighting has been on the shelf since session 132 with a warning attached — ⚠️
%% LILYPOND'S OWN SLUR MOVES THAT BOOK'S DIGITS BY NOTHING (measured, scratch cr-probe.ly), so
%% the sighting is an ISSUE and not a measurement, and the point has to be made in a texture
%% where LilyPond actually does something. This file is that texture.
%%
%% THE RULE, read from the source before measuring. Fingering declares avoid-slur #'around
%% (scm/define-grobs.scm Fingering) and Slur_engraver acknowledges it
%% (lily/slur-engraver.cc:74 ADD_ACKNOWLEDGER_FOR (acknowledge_extra_object, fingering)), so
%% lily/slur.cc:364-387 auxiliary_acknowledge_extra_object chains outside_slur_callback onto
%% the DIGIT. ⇒ THIS IS THE OPPOSITE DIRECTION FROM THE ONE SESSION 133 GAVE THE SCRIPTS: an
%% 'inside mark stays and the BOW is scored around it; an 'around mark rides OFF the finished
%% bow and the bow does not move. Lily# ports outside_slur_callback for SCRIPTS
%% (ArticulationEngraver.SlurAvoidanceShift) and a fingering never enters it — the gap is
%% named in FingeringEngraver's own class remark.
%%
%% THE GEOMETRY THE BOOKS ARE BUILT ON: the note is g' so the HEAD chain decides the digit's
%% height (ink bottom 3.545 = head ink top 3.045 + Fingering's padding 0.5) rather than the
%% staff clamp 2.55. HANDOFF §5.0: do not seat a reading on a floor it shares with the answer
%% — a clamped digit would be metres clear of the bow and every book would read zero, which is
%% exactly why the corpus book reads zero. One fingering goes UP whatever the pitch
%% (new-fingering-engraver.cc:248-253, 1 / 2 == 0 leaves the down bucket empty) and the slur
%% is above because the stems on g' point down.
%%
%% THE BOOKS (the quantity is the digit's ink bottom about the staff refpoint, up-positive):
%%   FSB — the digit on the slur's own BOUND note. There the attachment sits at head top +
%%         1.045 (slur-scoring.cc:555-557) while the digit's chain puts it at head top + 0.5,
%%         so the bow's own ENDPOINT is inside the digit.
%%   FSN — FSB's control: the same music, no slur.
%%   FSI — the digit on an INTERIOR note of a three-note slur.
%%   FSC — FSI's control: the same music, no slur.
%%
%% ⚠️ THESE FOUR LP NUMBERS WERE MEASURED IN SCRATCH FIRST (fingslur-probe.ly), as the search
%% for a texture that moves at all — so they are not predictions that stood a test. Rounds 2
%% and 3 of chord-fingering.ly carry the same disclosure. What IS written ahead of its run is
%% each book's Lily# mirror, in the ledger whys, and the decomposition below.
%%
%% ★★★ AND ONE PREDICTION WAS WRITTEN AHEAD, IN THE SCRATCH FILE, AND IT WAS FALSIFIED —
%% recorded here because it is the useful part. The scratch header said FSI "expected to read
%% its control exactly, i.e. zero effect", on the argument that over an INTERIOR note the bow
%% only clears the head by free-head-distance and therefore passes BELOW a digit that sits a
%% whole padding above it. That is wrong, and wrong in the informative direction: an up bow
%% ARCS, so over an interior note its peak is far higher than at its ends, and the interior
%% digit is pushed nearly TWICE as far as the bound one.
%%   MEASURED: FSB 4.004942353, FSN 3.545000000 ⇒ the bound digit rises 0.459942353.
%%             FSI 4.487006498, FSC 3.545000000 ⇒ the interior digit rises 0.942006498.
%%   ⇒ THE TWO CONTROLS ARE THE SAME NUMBER TO FIFTEEN DIGITS, which is what makes the two
%%   shifts directly comparable: same music height, same chain, only the bow differs.
%%
%% THE DECOMPOSITION, and it is the reason a constant will not do. outside_slur_callback
%% (lily/slur.cc:262-359) widens the digit's ink box by slur-padding (0.2, Fingering's own
%% declaration), takes the CURVE's extremum over the box's x-overlap — Slur::get_curve, the
%% centreline control polygon, not the drawn ink — and shifts by that extremum minus the box's
%% near edge. FSB's bow is ((0.6521 . 3.545) (1.3242 . 4.1269) (2.4842 . 4.1269) (3.1563 .
%% 3.545)) about x 8.585, and the digit stands at x 8.827: the overlap is the bow's FIRST
%% TENTH, where the curve is still climbing out of its attachment, which is why the bound
%% digit moves LESS than the interior one even though the bow starts inside it. A single
%% number fitted to either book misses the other by half a staff space.
%%
%% THE MUSIC IS GENERATED, NOT WRITTEN (HANDOFF: the octave trap) — `lysc ly` on the .lys
%% twins recorded in LpGeometryProbes.cs (FingeringSlurScore).
%%
%% MEASURED: see the ledger entries fingering.slur.* .

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
                   ;; The Slur rides with the Fingering, the heads and the staff symbol so the
                   ;; reading can be DECOMPOSED: a Slur prints its control-points, which is the
                   ;; curve the callback reads, not the ink its arc happens to reach.
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (memq nm '(Fingering NoteHead StaffSymbol Slur Stem))
                                        ;; The X EXTENT is printed too, and it is not
                                        ;; decoration: the callback takes the curve's
                                        ;; extremum over the digit's OWN x-extent
                                        ;; (slur.cc:350-356 outside_slur_callback's own
                                        ;; minmax call — only yext is widened by
                                        ;; slur-padding, xext is not), so on a book whose
                                        ;; extremum sits AT that edge the reading is the
                                        ;; edge. FSB is such a book and FSI is not.
                                        (format #t "PROBEC GROB ~a ~a name=~a text=~a rel=~a ext=(~a . ~a) x=~a xext=(~a . ~a)\n"
                                                n i nm
                                                (cond ((eq? nm 'Fingering) (ly:grob-property g 'text))
                                                      ((eq? nm 'Slur) (ly:grob-property g 'control-points))
                                                      (else "-"))
                                                (ly:grob-relative-coordinate g sg Y)
                                                (car (ly:grob-extent g g Y))
                                                (cdr (ly:grob-extent g g Y))
                                                (ly:grob-relative-coordinate g sg X)
                                                (car (ly:grob-extent g g X))
                                                (cdr (ly:grob-extent g g X))))))
                                (ly:grob-array->list all))))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEC BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

%% FSB — generated from fsb.lys by `lysc ly`; the digit on the slur's own bound note.
\book {
  \probeTag "FSB"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 4/4 \key c \major g'8-1 ( g'8 ) r4 r2 | } }
    \layout { indent = 0\mm }
  }
}

%% FSN — generated from fsn.lys; FSB's control, no slur.
\book {
  \probeTag "FSN"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 4/4 \key c \major g'8-1 g'8 r4 r2 | } }
    \layout { indent = 0\mm }
  }
}

%% FSI — generated from fsi.lys; the digit on an INTERIOR note, where the bow's peak is.
\book {
  \probeTag "FSI"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 4/4 \key c \major g'8 ( g'8-1 g'8 ) r8 r2 | } }
    \layout { indent = 0\mm }
  }
}

%% FSC — generated from fsc.lys; FSI's control, no slur.
\book {
  \probeTag "FSC"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 4/4 \key c \major g'8 g'8-1 g'8 r8 r2 | } }
    \layout { indent = 0\mm }
  }
}
