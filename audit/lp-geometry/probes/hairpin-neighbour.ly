\version "2.26.0"
%% SCRATCH (session 276) — PRE-FLIGHT for the hairpin pair, and it is a GATE.
%%
%% Session 276 act 6 measured that OutsideStaffStacker.HairpinHalfHeight moves two tracked
%% books and no ledger point, and that the arrangement those books draw is `@decresc <>@pp`
%% — a hairpin running into a second dynamic. The remark that blocked the point called that
%% arrangement missing from the corpus; it is not. So the next step is to cut the pair.
%%
%% ⚠️ BUT A PAIR HAS TO BE A PAIR (HANDOFF 5.0, the session-60/61 trap: a strong statement off
%% a broken pair cost two sessions). The unknown is whether LilyPond even HAS a vertical
%% relationship here. In LilyPond a Hairpin and a DynamicText both live inside the SAME
%% DynamicLineSpanner and are aligned on ONE line, side by side; if that is what happens, then
%% nothing in LilyPond is pushed vertically by this neighbour, Lily#'s collision pass is
%% answering a question LilyPond never asks, and the two engines cannot be paired on it.
%%
%% THIS BOOK ASKS THAT BEFORE ANY POINT IS CUT. It prints, for each score, the Y of the
%% staff's middle line, of the Hairpin, of the DynamicText, and of the DynamicLineSpanner they
%% share, so that "same line" and "stacked" are distinguishable by reading, not by argument.
%%
%%   HN1  hairpin running into a second dynamic  (the corpus arrangement)
%%   HN2  the same dynamic with NO hairpin        (control: one variable apart)
%%   HN3  hairpin with a tall script BELOW it     (the other arrangement the remark named)
%%   HN4  the plain hairpin                       (HN3's and HN5's control)
%%   HN5  hairpin under a FORCED-DOWN FERMATA     (the arrangement that DOES observe the box)
%%   HPC  HN4 rewritten in \absolute              (the ledger's control book)
%%   HPF  HN5 rewritten in \absolute              (the ledger's fermata book)
%%
%% ⚠️ HPC/HPF EXIST BECAUSE OF THE OCTAVE, NOT BECAUSE OF THE MUSIC. The books above are
%% written inside `\fixed c'`, where the written `c'` is C5 and not the C4 the same three
%% characters mean in Lily#'s `octave absolute`. A ledger point is a pair of readings from
%% two books that must be THE SAME MUSIC, and session 275 lost a session to exactly this
%% class of slip (HANDOFF §5.0: a relative octave mark turning `c' d' e'` into six different
%% octaves while the picture stayed plausible). The twin check that normally settles it —
%% `lysc ly` on the Lily# book — is unavailable while Smart App Control blocks lysc.dll, so
%% the octave is pinned HERE instead: HPC and HPF spell the pitch absolutely, and their
%% numbers must come out identical to HN4's and HN5's. They do (2026-08-28, LilyPond 2.26.0),
%% which is what makes `c'` in the Lily# book the same note as `c'` in HN4/HN5.

#(define (dump-hn tag)
   (lambda (layout pages)
     (format #t "\nPROBEHN BOOK ~a\n" tag)
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
                            (if (memq nm '(StaffSymbol Hairpin DynamicText
                                           DynamicLineSpanner TextScript Script))
                                (let ((ry (ly:grob-relative-coordinate g sg Y))
                                      (rx (ly:grob-relative-coordinate g sg X))
                                      (ye (ly:grob-extent g g Y))
                                      (xe (ly:grob-extent g g X)))
                                  (format #t "PROBEHN ~a G ~a ry=~a rx=~a Y=(~a . ~a) X=(~a . ~a)\n"
                                          tag nm ry rx
                                          (car ye) (cdr ye) (car xe) (cdr xe))))))
                        (ly:grob-array->list all)))))))
         (ly:prob-property page 'lines)))
      pages)))

probeHN =
#(define-scheme-function (tag) (string?)
   #{ \paper { ragged-bottom = ##t
               ragged-right = ##t
               indent = 0
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(dump-hn tag) } #})

%% ⚠️ EVERY DYNAMIC IS ANCHORED BY A FOLLOWING NOTE, and the first draft of this book was not.
%% Written as `c'1\> <>\pp' with nothing after it, LilyPond answers `programming error: bounds
%% of this piece aren't breakable' / `no broken bound', prints NO DynamicText at all, and still
%% says `Success' at the end. Read at face value that dump says "LilyPond makes no DynamicText
%% for <>\pp", which is a strong statement off a BROKEN BOOK — the session-60/61 trap exactly.
%% ⇒ read the probe's stderr before its stdout.
%%
%% HN1 — the corpus arrangement: a decrescendo ending into a second dynamic.
\book { \probeHN "HN1" \score { \new Staff { \fixed c' { \time 4/4 c'1\> <>\pp c'1 } } } }

%% HN2 — the control, one variable apart: the same dynamic, no hairpin.
\book { \probeHN "HN2" \score { \new Staff { \fixed c' { \time 4/4 c'1 <>\pp c'1 } } } }

%% HN3 — a hairpin with a tall script hanging BELOW it, the other arrangement the blocked
%% remark named. If anything pushes a hairpin vertically in LilyPond, it shows here.
\book { \probeHN "HN3" \score { \new Staff { \fixed c' { \time 4/4 c'1\>_\markup { \column { A B C } } c'1\! } } } }

%% HN4 — HN3's control, one variable apart: the same hairpin with NO script under it. HN1
%% cannot serve as that control (different music), and without HN4 the HN3 reading is a number
%% with nothing to be compared to.
\book { \probeHN "HN4" \score { \new Staff { \fixed c' { \time 4/4 c'1\> c'1\! } } } }

%% HN5 — THE ARRANGEMENT THAT ACTUALLY OBSERVES THE BOX, found by reading the priorities
%% instead of guessing at configurations. A hairpin can only be pushed by an outside-staff
%% grob whose priority is BELOW DynamicLineSpanner's 250, and below the staff there are
%% exactly two: TrillSpanner (50) and the fermata family (75). A fermata forced down is the
%% cheapest of them.
%%   LILYPOND-REF: scm/script.scm — the fermata family's outside-staff-priority 75.
%%   LILYPOND-REF: scm/define-grobs.scm:4078 TrillSpanner outside-staff-priority 50.
%% HN4 is its control, one variable apart (the same hairpin, no fermata).
%%
%% ANSWERED 2026-08-28 (session 276), and the pair EXISTS:
%%   HN4 (no fermata)   Hairpin ry = -7.1426
%%   HN5 (fermata below) Hairpin ry = -8.86597818620712
%%   ⇒ the fermata pushes the wedge 1.723378186 DEEPER.
%% So LilyPond does push a hairpin — just never with the two things the blocked remark named
%% (a TextScript at 450 and a second dynamic on the same line), and always with something
%% whose priority is under 250. THIS is the arrangement a ledger point should be cut on, and
%% the LilyPond side of it is measured above so the next session does not pay for it again.
\book { \probeHN "HN5" \score { \new Staff { \fixed c' { \time 4/4 c'1\>_\fermata c'1\! } } } }

%% ---------------------------------------------------------------------------------------
%% THE LEDGER'S OWN TWO BOOKS (session 277). Same music as HN4/HN5, octave spelled out.
%%
%% ledger hairpin.plain.staff-to-wedge        <- HPC
%% ledger hairpin.under-fermata.staff-to-wedge <- HPF
%%
%% The reading both entries take is THE WEDGE'S CENTRE MEASURED DOWN FROM THE STAFF'S MIDDLE
%% LINE. On this side that is `StaffSymbol ry - Hairpin ry`: the Hairpin grob's reference
%% point IS the centre of its opening (lily/hairpin.cc builds the stencil symmetrically about
%% its own Y=0, which is why the grob's Y-extent prints as ±0.7166 = the declared height
%% 0.6666 plus half the 0.1 line), and Lily# draws the same two arms symmetrically about
%% HairpinLayout.YUp. Centre against centre, so neither side's line thickness enters.
%%
%% ⚠️ THE TWO BOOKS DIFFER IN ONE VARIABLE ONLY (the fermata). Everything a hairpin's depth
%% could otherwise depend on — pitch, texture, clef, paper, where the wedge starts and stops
%% — is identical between them, so the DIFFERENCE of the two readings is the fermata's push
%% and nothing else. That difference, not either reading alone, is what the pair is for.
\book { \probeHN "HPC" \score { \new Staff { \absolute { \time 4/4 c''1\> c''1\! } } } }
\book { \probeHN "HPF" \score { \new Staff { \absolute { \time 4/4 c''1\>_\fermata c''1\! } } } }
