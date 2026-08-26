\version "2.26.0"
%% LP FIDELITY PROBE - a chords row and a multi-verse lyrics row between two staves,
%% the user-report arrangement of 2026-08-25 distilled (session 257 (13)'s missing
%% pair, built session 262).
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe chord-verse-row.ly -Prefix PROBERV
%%
%% WHY THIS SHAPE. The reported book (scratch/ベースタブLy/Untitled-6.lys) spells
%% staff / chords as names / lyrics sings melody / staff, with chords on some systems
%% only and a second verse on others -- and it is the one picture the seam-(1) fold
%% moved (+2.36 on the pair whose row carries two verses: the band had UNDER-reserved
%% and the lower staff was drawn through verse 2's text). Two quantities have no LP
%% referee yet: (i) the chords+verses run's own steps, and (ii) what a verse that
%% exists in the SCORE but not on THIS system costs -- Lily#'s
%% MultiStaffLayouter.TextRowVerseSpacing charges the row's band height 3.2 per extra
%% score-wide verse (session 257 (13): "+3.627 のうち 3.200 は描かない詩 1 本"), and the
%% port order of that constant was left waiting on exactly this pair.
%%
%% THE BOOKS (4/4, two systems of four bars, ragged-right; melody and lower staff both
%% g'/a' quarters so no note ink binds -- book ROWA's discipline):
%%   RVC1  system 1: chords C G C G above verse 1 ("no" x16); verse 2 silent.
%%         system 2: no chords; verses 1 and 2 (both "no" x16).
%%   RVC2  the control: verse 2 deleted entirely. One variable against RVC1 -- a
%%         second verse that never sings on system 1.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2):
%%   (a) RVC1 system-2 verse step = 2.800000 exactly -- identical "no" verses, the
%%       rigid nonstaff-nonstaff spec (engraver-init.ly:653-657).
%%   (b) RVC1 system 1 == RVC2 system 1, every reading -- LilyPond has no score-wide
%%       verse count, so a verse that sings only on system 2 cannot move system 1.
%%       (falsifier: any difference, which would mean LP DOES read cross-system state
%%        and the TextRowVerseSpacing question is not what session 257 named.)
%%   (c) The ChordNames context vanishes from system 2 (remove-empty) -- the ROWA
%%       family's measured behaviour; system 2's run is the two verses alone.
%%
%% ⚠️ THE PITCHES: Lily# absolute is LilyPond minus one apostrophe (probe trap 5).
%% The .lys twin spells g a for LilyPond's g' a'.
%% ⚠️ THE .lys TWIN CARRIES STANZA NUMBERS ([~1.]/[~2.]) that LilyPond does not --
%% Lily#-side furniture, measured non-binding on the VRS1/VRS2 pair (residual
%% difference 3e-6); kept because the user book spells its verses that way.

#(define (dump tag layout pages)
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
                          (if (memq nm '(VerticalAxisGroup StaffSymbol))
                              (format #t "PROBERV ~a ~a rel=~a ext=(~a . ~a) aff=~a\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))
                                      (ly:grob-property g 'staff-affinity 'none)))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))

\book {
  \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
           property-defaults.fonts.serif = "LilyPond Serif"
           property-defaults.fonts.sans = "LilyPond Sans Serif"
           page-post-process = #(lambda (layout pages)
                                  (format #t "\nPROBERV BOOK RVC1\n")
                                  (dump "RVC1" layout pages)) }
  \score { <<
    \new Staff {
      \repeat unfold 4 { g'4 a' g' a' } \break
      \repeat unfold 4 { g'4 a' g' a' }
    }
    \new ChordNames \chordmode { c1 g c g }
    \new Lyrics \lyricmode { \repeat unfold 32 no4 }
    \new Lyrics \lyricmode {
      \repeat unfold 16 \skip 4
      \repeat unfold 16 no4
    }
    \new Staff { \repeat unfold 8 { g'4 a' g' a' } }
  >> } }

\book {
  \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
           property-defaults.fonts.serif = "LilyPond Serif"
           property-defaults.fonts.sans = "LilyPond Sans Serif"
           page-post-process = #(lambda (layout pages)
                                  (format #t "\nPROBERV BOOK RVC2\n")
                                  (dump "RVC2" layout pages)) }
  \score { <<
    \new Staff {
      \repeat unfold 4 { g'4 a' g' a' } \break
      \repeat unfold 4 { g'4 a' g' a' }
    }
    \new ChordNames \chordmode { c1 g c g }
    \new Lyrics \lyricmode { \repeat unfold 32 no4 }
    \new Staff { \repeat unfold 8 { g'4 a' g' a' } }
  >> } }
