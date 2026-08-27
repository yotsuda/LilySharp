\version "2.26.0"
%% LP FIDELITY PROBE — WHAT AN INLINE CHORD SYMBOL CHARGES AN INTER-SYSTEM GAP.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe inline-chord-page.ly -Prefix PROBECI
%%
%% THE REGIME THIS OPENS (2026-08-28, session 273 — step (1) of scratch/p272/
%% sweep-map.txt's act-6 addendum, with the constant's NAME corrected). The map
%% calls it "chord row 3.0". It is not the chord row: EstimateAboveStaffExtents'
%% chord branch is handed `inlineChordNames` (LayoutEngine.cs:257 — score.ChordNames
%% minus every text-row staff), so it fires ONLY for INLINE chord symbols (`@chord`,
%% a bare symbol drawn above the staff at a note's X). A chord ROW is a text row and
%% is filtered OUT before it arrives, which is why CHR1/CHR2 and GCF/GCS — every
%% chord-row book this corpus has — are blind to the constant, and why the only one
%% of 572 tracked books that binds it (chords-funky-ignatzek.lys) is written with
%% `@chord`. The constant reaches the page through TWO arms and both were measured
%% by poison before these books were cut (scratch/p273/predictions.txt):
%%   upExtent → the page anchor. BLIND BY CONSTRUCTION: on a single staff the
%%     anchor is margin + max(6, header + upExtent + 2.0 + 1), so 3.0 makes the
%%     floor candidate EXACTLY 6.000000 — a tie with basic-distance 6. Raising the
%%     constant moves the page one-for-one; LOWERING it moves nothing at all.
%%   bandUp  → the inter-system floor (LayoutUtilities.InterSystemPairMinimum:
%%     dist = max(skyline…, inkBelowLastRefpoint + nextHalfFirst + bandUpNext)).
%%     TWO-SIDED, and that is where these entries go.
%%
%% THE PAIR (HANDOFF 5.0-1), LILY# SIDE MEASURED FIRST (scratch/p273/predictions.txt):
%%
%% CIB/CIBN — an IDENTITY pair on LilyPond's side, one variable apart: the same two
%%   systems with and without the chord symbols. System 1 is a DEEP c,-column line
%%   (its ink reaches 8.05 below its refpoint; the ct family's `g,` at 5.045 leaves
%%   the band under the 12 floor and would not bind at all), system 2 carries
%%   <c e g> and <f a c'>. Lily# ALREADY MEASURED: CIB 13.05 against CIBN 12.00, and
%%   a poison sweep splits that 1.05 in two — 0.60 arrives through the OTHER chord
%%   arm (the per-measure annotation extents' [cnY − 1.9, cnY + 0.3] scalar that
%%   GCF/GCS watch) and 0.45 is the band clearing it. The band's own arithmetic is
%%   8.05 + 2.0 + 3.0 = 13.05, reproduced nine digits by the sweep (3.5 → 13.55,
%%   4.0 → 14.05).
%%   PREDICTION: CIBN 12.000000 EXACT (sys1's 8.05 + sys2's 2.0 + padding 1 = 11.05
%%   loses to system-system-spacing's basic-distance 12 on both engines). CIB —
%%   LilyPond WIDENS, to about 13.6: the room above system 2's staff top inside a 12
%%   gap is 12 − 8.05 − 2.0 = 1.95 and the row needs about 2.6, so the loose line
%%   does not fit and distribute_loose_lines must push the systems apart. Against
%%   Lily#'s 13.05 that is a residual near −0.6, i.e. Lily# UNDER-reserving.
%%   ⚠️ FALSIFIER, AND IT INVERTS THE PORT: 12.000000 EXACT on CIB as well. That is
%%   what lyrics.chord-row.between-systems.system-gap found for a chord ROW (LYRMC
%%   = LYRM = 12.000000 — "the chord row does NOT widen the gap"), and if it holds
%%   here too then Lily#'s +1.05 is pure OVER-reservation with the opposite sign and
%%   the right repair is to RETIRE the band, not to make it ink-true. This probe
%%   predicts against that, on the ground that LYRMC's row FIT its room and this one
%%   cannot.
%%
%% CIBM — NOT A LEDGER POINT: the COUNTERPART CHECK (HANDOFF 5.0, the session-179
%%   trap — a translation substitution hides underneath an agreement). Lily#'s
%%   `@chord` is drawn as a symbol above the staff AT A NOTE'S X; LilyPond has no
%%   such construct, its chord symbols being a ChordNames CONTEXT, i.e. a LOOSE
%%   LINE. The corpus already makes that mapping (chords-funky-ignatzek.lys's header
%%   says so) and the twin exporter cannot even express it — `lysc ly` on these
%%   books answers "warning: @chord dropped (out of scope)". So the same music is
%%   measured a THIRD way, with the symbols spelled as TextScripts on the notes, and
%%   the entries' `why` records WHICH of the two LilyPond constructs Lily#'s drawing
%%   actually is rather than assuming the corpus's mapping. ⚠️ The face is NOT
%%   controlled here (a bare \markup is the text font, not ChordName's sans): what
%%   CIBM answers is the GEOMETRY question — loose line or script on the staff —
%%   and its ink figures are read only against CIB's, never as a fidelity number.
%%
%% ⚠️ THE SANS FACE IS PINNED, for the reason chord-symbol-width.ly's header gives:
%% the chord symbol's ink is sans text, and this machine's fontconfig picks Verdana
%% for generic sans, which is what made lyrics.chord-row.between-systems' first
%% measurement wrong by 0.023 (see that entry's `why`).
%%
%% Everything printed is in STAFF SPACES (see page-vertical.ly's header for why).
%% first STAFF refpoint below the paper edge = top-margin + Y-offset - staffUp; the
%% GAP is the difference of two consecutive systems' readings, which is what
%% RenderedGeometry.StaffGapAt(0) reads on the Lily# side.
%% ⚠️ THE OCTAVES ARE THE EXPORTER'S OWN, not hand-converted: `lysc ly` emits these
%% books as `\fixed c' { … }` and that wrapper is kept here verbatim, so the
%% HANDOFF 5.5 conversion cannot be got wrong by hand.

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
                          (if (memq nm '(ChordName TextScript StaffSymbol))
                              (format #t "PROBECI ~a GROB ~a rel=~a ext=(~a . ~a)\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))

#(define (dump-pages tag layout pages)
   (let ((tm (ly:output-def-lookup layout 'top-margin)))
     (format #t "PROBECI ~a top-margin=~a\n" tag tm)
     (for-each
      (lambda (page)
        (for-each
         (lambda (sys)
           (let* ((y (ly:prob-property sys 'Y-offset 0.0))
                  (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0))))
             (format #t "PROBECI ~a SYS y=~a staff=(~a . ~a) first-staff-refpoint-below-edge=~a\n"
                     tag y (car staff) (cdr staff)
                     (+ tm y (- (cdr staff))))))
         (ly:prob-property page 'lines)))
      pages)
     (dump-grobs tag layout pages)))

probeCI =
#(define-scheme-function (tag) (string?)
   #{ \paper { ragged-bottom = ##t
               indent = 0
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBECI BOOK ~a\n" tag)
                                      (dump-pages tag layout pages)) } #})

%% CIB — the chord half. Lily#: section A { melody { c,4 c, c, c, | c,4 c, c, c, |
%% break } } section B { melody { <c e g>1@chord | <f a c'>1@chord | } }.
\book {
  \probeCI "CIB"
  \score {
    <<
      \chords { s1 s1 c1 f1 }
      \new Staff { \fixed c' { c,4 c, c, c, | c,4 c, c, c, | \break
                               <c e g>1 | <f a c'>1 } }
    >>
  }
}

%% CIBN — CIB with the chord symbols taken out and NOTHING else changed.
\book {
  \probeCI "CIBN"
  \score {
    \new Staff { \fixed c' { c,4 c, c, c, | c,4 c, c, c, | \break
                             <c e g>1 | <f a c'>1 } }
  }
}

%% CIBM — the counterpart check: the same symbols as TextScripts on the notes,
%% i.e. the OTHER construct Lily#'s inline `@chord` could correspond to. Not a
%% ledger point; read only against CIB (see the header).
\book {
  \probeCI "CIBM"
  \score {
    \new Staff { \fixed c' { c,4 c, c, c, | c,4 c, c, c, | \break
                             <c e g>1^\markup "C" | <f a c'>1^\markup "F" } }
  }
}
