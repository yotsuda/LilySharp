\version "2.26.0"
%% LP FIDELITY PROBE - a lyrics row under a TAB staff, the user-report arrangement of
%% 2026-08-29 distilled.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe tab-lyric-row.ly -Prefix PROBETL
%%
%% WHY THIS SHAPE. No entry in this corpus measures a lyric line under a staff that is
%% not four staff spaces tall. LilyPond's TabStaff sets StaffSymbol.staff-space = 1.5
%% for every string count (ly/engraver-init.ly), so a six-string tab's lines span
%% (6-1) * 1.5 = 7.500000 and its outermost string sits 3.750000 below the
%% VerticalAxisGroup reference point every spacing distance is written against --
%% where an ordinary staff's sits 2.000000 below. A Lyrics line has staff-affinity UP
%% and takes nonstaff-relatedstaff-spacing from the staff above it, so the question is
%% whether the extra 1.750000 of STRING reaches the syllable.
%%
%% THE BOOKS (one system of one bar, ragged-right; every fret INSIDE the strings, so
%% no fret number hangs below the staff and the only thing under the reference point
%% is the staff symbol itself -- that is the case the report was about):
%%   TBL1  a lone six-string TabStaff with a lyrics row under it.
%%   TBL2  the control: the same music and the same words on an ordinary Staff. One
%%         variable against TBL1 -- how tall the staff above the row is.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2):
%%   (a) TBL1's staff-to-lyric is LARGER than TBL2's by about 1.750000 -- the string
%%       span's own half, because the floor is the profile and the profile ends at the
%%       bottom string. (falsifier: the two are equal, which would mean the distance is
%%       the spec's basic-distance and the staff's own lines never bind; Lily#'s
%%       nominal-height arithmetic would then be accidentally right and the reported
%%       overlap would have some other cause.)
%%   (b) Neither reading is the bare basic-distance 5.500000: TBL2's ordinary staff has
%%       its own ink under the refpoint too.
%%
%% ---- MEASURED 2026-08-29 (2.26.0, fonts pinned). (a) HELD IN DIRECTION AND FAILED IN
%% MAGNITUDE, and (b) IS FALSIFIED OUTRIGHT -- both worth keeping, because the reason is
%% the same one fact and it is the fact this pair exists to record.
%%   TBL1 6.120115  TBL2 5.500001  difference 0.620114, not 1.750000.
%% TBL1 decomposes EXACTLY: staff-symbol ink 3.800000 (the 7.5 span plus half a line's
%% thickness at each edge) + the syllable's own ascender 1.820098 + padding 0.500000 =
%% 6.120098, against 6.120115 read. The profile binds, and what it ends at is the bottom
%% STRING -- prediction (a)'s mechanism, exactly.
%% TBL2 is 5.500001 = nonstaff-relatedstaff-spacing's basic-distance to the digit, which
%% is (b) falsified: the ordinary staff's VerticalAxisGroup reaches 3.550000 below its
%% refpoint (its stems), and 3.550000 + 1.820098 + 0.500000 = 5.870098 WOULD have bound
%% had the distance been taken between extents. It is not. Skyline::distance is pointwise
%% and the stems are not under the syllables' ascenders at the same X, so the floor loses
%% to the basic-distance. ⇒ THE PAIR'S DIFFERENCE IS NOT THE HALF-STAFF DIFFERENCE: the
%% tab's lines run the WHOLE system width and so bind at every X, where an ordinary
%% staff's deepest ink is a few stems and binds nowhere. That is why the defect this pair
%% was built for was invisible on any book with a fret hanging below the staff.

%%
%% ⚠️ THE PITCHES CAME OUT OF `lysc ly`, NOT OUT OF A HEAD (probe trap 5, and RULES'
%% standing instruction): Lily# absolute is LilyPond minus one apostrophe, so the .lys
%% twin's `g'4 a' g' a'` is `\fixed c' { g'4 a' g' a' }` here -- g'' a'' g'' a''. Key of
%% C, no accidental on either side, and both frets (15 and 17 on the top string) inside.
%% ⚠️ THE TAB IS \tabFullNotation, which is what a LONE tab defaults to in Lily# since
%% 2026-08-29 (U4) -- the twin has to name the same style or the two are not one book.
%% ⚠️ AND NEITHER SIDE CARRIES A REHEARSAL MARK: the .lys spells `form main { ~A }`,
%% because a marked section prints an "A" above the staff and LilyPond's twin has none.


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
                              (format #t "PROBETL ~a ~a rel=~a ext=(~a . ~a) aff=~a\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))
                                      (ly:grob-property g 'staff-affinity 'none)))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))

words = \lyricmode { Twin -- kle twin -- kle }
tune = \fixed c' { \key c \major g'4 a' g' a' }

\book {
  \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
           property-defaults.fonts.serif = "LilyPond Serif"
           property-defaults.fonts.sans = "LilyPond Sans Serif"
           page-post-process = #(lambda (layout pages)
                                  (format #t "\nPROBETL BOOK TBL1\n")
                                  (dump "TBL1" layout pages)) }
  \score { <<
    \new TabStaff \with { stringTunings = #guitar-tuning }
      { \tabFullNotation \new TabVoice = "mel" { \tune } }
    \new Lyrics \lyricsto "mel" \words
  >> } }

\book {
  \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
           property-defaults.fonts.serif = "LilyPond Serif"
           property-defaults.fonts.sans = "LilyPond Sans Serif"
           page-post-process = #(lambda (layout pages)
                                  (format #t "\nPROBETL BOOK TBL2\n")
                                  (dump "TBL2" layout pages)) }
  \score { <<
    \new Staff { \new Voice = "mel" { \tune } }
    \new Lyrics \lyricsto "mel" \words
  >> } }
