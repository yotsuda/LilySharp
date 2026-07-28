\version "2.26.0"
%% LP FIDELITY PROBE — WHERE Skyline::distance BINDS in the vertical alignment walk.
%%
%% WHY THIS EXISTS.  lyrics.between-staves.staff-to-lyric (book LYRB in page-vertical.ly)
%% is a NET of two errors of opposite sign, and one of them has been carried as BLOCKED for
%% several sessions: the closing step of the alignment walk.  Lily#'s mechanism reproduces
%% the shape exactly -- the staff above is raised, its clef hangs below its refpoint, and
%% the next staff's clef reaches up to meet it -- but the arithmetic done with LilyPond's
%% own numbers overshoots what LilyPond actually lays out:
%%
%%     Lily# :  3.800000 - 0.459200 + 1.500000 = 4.840800     (its own raise 4.009200)
%%     LP    :  3.776000 - 0.197890 + 1.500000 = 5.078110     (LilyPond's raise 3.737890)
%%     LP actually reads                          4.972149
%%     ------------------------------------------------------------------ 0.105961 lower
%%
%% The ledger's `why` names the cause but could not measure it: "LilyPond's Skyline::distance
%% is finding a different x than the naive pairing does" -- the naive pairing being
%% max_height(upper's DOWN profile) + max_height(lower's UP profile), which is what a pair of
%% BOXES would give.  A skyline is not a box: lily/skyline.cc:618-645 internal_distance walks
%% the two building lists together and maximises  i->height(x) + j->height(x)  POINTWISE, so
%% two profiles whose maxima sit at DIFFERENT x bind lower than the sum of their maxima.
%%
%% ⇒ AND THERE IS A MECHANISM ON THE TABLE, because of what the glyph-skyline port found:
%% lily/stencil-integral.cc:534-563 add_named_glyph_segments ends in
%% `open_fm->add_outline_to_skyline (skyline, local, gidx)` -- LilyPond puts the glyph's
%% ACTUAL OUTLINE PATH into the skyline, not the outline's bounding box.  Lily# ported the
%% BOX (audit/lp-geometry/probes/glyph-skyline.ly, commit 6c6be1af), which reproduces
%% max_height and therefore every reading that binds against a single profile, and cannot
%% reproduce a reading where two profiles are compared pointwise.  A G clef's deepest x and
%% its highest x are not the same x.
%%
%% ⚠️ THIS PROBE MEASURES ONLY.  Nothing here may be turned into a constant: 0.105961 is a
%% target to be REPRODUCED by porting a mechanism, never a number to write down (HANDOFF 5.2).
%%
%% ------------------------------------------------------------------------------------
%% PREDICTIONS, WRITTEN BEFORE THE FIRST RUN (HANDOFF 5.0 step 2).  Both are forks: each
%% outcome selects a DIFFERENT next piece of work, not a different number (HANDOFF 5.0).
%% ------------------------------------------------------------------------------------
%%
%% P1  In LYRB's first system, the pair (upper Staff's DOWN skyline, lower Staff's UP
%%     skyline) reports
%%          dist  = 7.210039        and       naive = 3.540000 + 3.776000 = 7.316000
%%     i.e. a deficit of exactly -0.105961, the whole unexplained closing term.
%%     Derivation: the walk's closing dy is dist(accumulated_down, lower_up) + padding, the
%%     accumulated down-skyline is the upper staff's own raised by the 3.737890 already
%%     fixed (align-interface.cc:272-273), so dist_pairwise = dy + raise - padding
%%     = 4.972149 + 3.737890 - 1.500000.
%%     ⚠️ FALSIFIER, and it is the useful branch: if dist comes out AT 7.316000, the deficit
%%     is NOT in this pair, and the next place to look is the LYRIC's own contribution to
%%     the accumulated skyline or the padding term -- both of which this dump also prints,
%%     so the probe does not need rewriting to answer that.
%%     ⚠️ SECOND FALSIFIER: the 1.500000 is quoted from the ledger's arithmetic and is NOT
%%     derived from a LilyPond declaration here (VerticalAxisGroup declares
%%     nonstaff-unrelatedstaff-spacing padding 0.5, define-grobs.scm:4240).  The dump prints
%%     each element's PLACED position, so padding = dy_actual - dist is read off, not
%%     assumed.  If it reads 0.5, P1's target moves to 6.210039 and the DEFICIT -- which is
%%     what this probe is for -- is unaffected, because both terms shift together.
%%
%% P2  The deficit is a property of TWO G CLEF OUTLINES facing each other and nothing else,
%%     so book PLAIN -- the same two staves with the lyric line REMOVED -- shows the SAME
%%     deficit  dist - (dn_max + up_max) = -0.105961  between its two staves.
%%     ⇒ IF P2 HOLDS the work is named: seed the clef's real SILHOUETTE into Lily#'s
%%       skyline instead of the outline's box, and the term closes for every clef pair at
%%       once (this is the "which grob declares vertical-skylines from its stencil" list
%%       6c6be1af already ported -- what changes is the SHAPE, not the members).
%%     ⇒ IF P2 FAILS (PLAIN's deficit is 0, or is a different number) the term is not the
%%       clef pair; it involves the lyric line or the staff's other ink, and the pairwise
%%       table below names which element carries it, because every ordered pair is printed.
%%
%% P3  DIRECTION, since HANDOFF 5.0 requires the sign: the deficit is NEGATIVE (a skyline
%%     distance can never exceed the sum of the two maxima), so P1/P2 are claims about
%%     MAGNITUDE and OWNER only.  A deficit of 0 is the meaningful negative result: it says
%%     the two maxima DO sit at the same x, and then the missing 0.105961 is somewhere else
%%     entirely.
%%
%% ------------------------------------------------------------------------------------
%% ANSWERED 2026-07-28, FIRST RUN.  P1 AND P2 BOTH HELD.
%% ------------------------------------------------------------------------------------
%%
%%   PROBESK PAIR 1 0 0 2  dist=7.210038725633767  touch=2.1880000000000006
%%                         hdn=-3.446038725633767  hup=3.7640000000000002
%%
%% P1: dist is 7.210038725633767 against the predicted 7.210039 -- fifteen digits -- while
%%     the two maxima sum to 3.540000 + 3.776000 = 7.316000.  THE DEFICIT IS -0.105961,
%%     which is the whole unexplained closing term of lyrics.between-staves.staff-to-lyric.
%%     ⚠️ The `naive` and `deficit` COLUMNS PRINTED BY THIS PROBE ARE WRONG BY A SIGN and
%%     are left as they are rather than quietly corrected: ly:skyline-max-height returns the
%%     SIGNED height (a DOWN skyline reports -3.540000, not +3.540000), contradicting the
%%     header glyph-skyline.ly carries.  Read `naive` as -dn + up.  Every other column is
%%     LilyPond's own number.
%%     THE PADDING FALSIFIER RESOLVED AT 1.500000, and it is a LilyPond declaration after
%%     all: define-grobs.scm:4240 gives the GROB default 0.5, but the Lyrics CONTEXT
%%     overrides it -- ly/engraver-init.ly:695
%%     `\override VerticalAxisGroup.nonstaff-unrelatedstaff-spacing.padding = 1.5`.
%%     The whole closing step then closes with nothing left over:
%%       dist(upper DOWN, lower UP)                          7.210039
%%       less the raise already fixed above it (min offset)  -3.737890
%%       plus that padding                                   +1.500000
%%       = 4.972149, the ledger's closing minimum, six digits.
%%     ⚠️ The RAISE in that line is the walk's MINIMUM (3.737890), not the placed distance:
%%     the yrel columns show this book laid out at 4.027851 / 4.972149 about a staff-to-staff
%%     9.000000, i.e. the first spring is off its minimum and the closing one is on it, which
%%     is the regime lyrics.between-staves.staff-to-lyric was opened for.
%%
%% P2: book PLAIN -- the same two staves with the lyric line REMOVED -- prints
%%     dist=7.210038725633767 touch=2.1880000000000006 hdn=-3.446038725633767
%%     hup=3.7640000000000002, IDENTICAL to fifteen digits.  ⇒ the deficit is two facing G
%%     clef OUTLINES and nothing else; no lyric, no staff line, no stem is in it.
%%
%% ⇒ WHY THE MAXIMA MISS EACH OTHER, straight off the PT lines (they are the point of the
%%    probe -- a box cannot produce these):
%%
%%      upper staff, DOWN profile          lower staff, UP profile
%%        x=1.0996  -3.001376               x=1.8792   3.465061
%%        x=1.2101  -3.217728               x=2.0177   3.621633
%%        x=1.3788  -3.388192               x=2.1880   3.764000   <- touching point
%%        x=1.5930  -3.499904               x=2.2080   3.773000
%%        x=1.8400  -3.540000  <- deepest   x=2.2280   3.776000   <- highest
%%        x=2.0802  -3.501333               x=2.2495   3.774500
%%        x=2.2699  -3.404000               x=2.2680   3.764000
%%
%%    The G clef's lowest ink is at x=1.84-1.86 and its highest at x=2.228; the sum of the
%%    two profiles peaks between them, at x=2.188, where NEITHER is at its own extreme.
%%    3.446039 + 3.764000 = 7.210039.
%%
%% ⇒ THE PORT THIS NAMES (it is a mechanism, never the number -- HANDOFF 5.2): Lily# seeds
%%    the clef as ONE FLAT BOX (SkylineBuilder.SeedClef -> VerticalSkyline.FromBox with
%%    GlyphMetrics.ClefGOutline), which is the outline's BOUNDING BOX.  6c6be1af ported that
%%    box and it reproduces max_height, hence every reading that binds against a single
%%    profile; it cannot reproduce a pointwise comparison of two.  What LilyPond seeds is the
%%    outline POLYGON: lily/stencil-integral.cc:562 add_named_glyph_segments ends in
%%    add_outline_to_skyline, and lily/freetype.cc:96-202 ly_FT_add_outline_to_skyline
%%    decomposes the outline with each cubic flattened into max(2, |end-start|/0.2) contour
%%    segments, classified by contour orientation (CCW for a CFF font like Emmentaler).
%%    ★ THAT EXACT FLATTENING IS ALREADY IN THE TREE: audit/scripts/Extract-EmmentalerSkylines.py
%%    emits the accidentals' HORIZONTAL skylines from it (GlyphSkylinesGenerated.cs).  The
%%    clef needs the same generator run on the other axis, not a new mechanism.
%%    ⚠️ ONE KNOWN DEVIATION TO DECLARE WHEN IT IS DONE: the quantisation count is taken on
%%    the TRANSFORMED length, so a magnified staff (ossia) flattens to a different number of
%%    segments than a full-size one.  A precomputed profile is therefore the full-size one.
%%
%% ⇒ AND THE PREDICTION THE PORT WILL BE JUDGED BY, written here before it is attempted:
%%    lyrics.between-staves.staff-to-lyric RISES from +0.165349 to exactly +0.271310, the
%%    lyric-face term alone, because the missing silhouette is today CANCELLING a third of it
%%    (0.271310 - 0.105961 = 0.165349, six digits).  A port that leaves the entry at
%%    +0.165349 has not bound anything.
%%
%% ------------------------------------------------------------------------------------
%% HOW TO READ THE DUMP
%% ------------------------------------------------------------------------------------
%% Everything is in staff spaces.  Only PAGE 1, SYSTEM 0 is dumped: the question is local to
%% one alignment walk and a full book would bury it (HANDOFF 5.3 "one record, one line").
%%
%%   PROBESK VAG  <pg> <sys> <k> aff=.. yrel=.. xrel=.. ext=(..) dn=.. dnx=.. up=.. upx=..
%%       one vertical axis group.  `yrel` is its PLACED offset about the System, so the
%%       distance the walk laid out between element k and k+1 is  yrel_k - yrel_{k+1}
%%       (stacking_dir is DOWN, so yrel decreases).  `dn`/`up` are max_height of the two
%%       skylines and `dnx`/`upx` the x at which each reaches it -- if dnx of one element
%%       and upx of the next differ, the naive pairing is already known to be wrong.
%%       ⚠️ `xrel` is printed because align-interface.cc:83-84 SHIFTS each skyline by the
%%       group's own x before comparing.  Scheme cannot shift a skyline, so this probe's
%%       pair numbers are only LilyPond's if every xrel is equal; the parser must check it
%%       rather than trust it.
%%
%%   PROBESK PAIR <pg> <sys> <a> <b> dist=.. touch=.. hdn=.. hup=.. naive=.. deficit=..
%%       ly:skyline-distance / ly:skyline-touching-point between element a's DOWN skyline
%%       and element b's UP skyline -- exactly the call align-interface.cc:228 makes.
%%       `hdn`/`hup` are the two profiles' heights AT the touching point (they sum to dist),
%%       `naive` is dn_max + up_max, `deficit` is dist - naive.
%%
%%   PROBESK PT   <pg> <sys> <k> <which> x=.. y=..
%%       the profile itself (ly:skyline->points), clipped to the left end of the system
%%       where the clef lives.  This is what says WHETHER a box can reproduce the number.
%%
%% ⚠️ `vertical-skylines` reaches Scheme as a plain CONS: car is the DOWN skyline, cdr the
%% UP one (lily/lily-guile.cc:503-506, and glyph-skyline.ly's header says the same).
%% ly:skyline-max-height returns the INTERNAL height, i.e. a DOWN skyline reports a POSITIVE
%% number for ink hanging below its reference point -- which is why `naive` is a SUM.
%%
%% NOTE: inside #(...) the comment character is `;`, not `%%`.

#(define (skb-finite? x) (and (number? x) (not (inf? x)) (not (nan? x))))

#(define (skb-dump-points sky which pg sys k)
   ;; The full point list is thousands of entries on a staff that spans the line; only the
   ;; left end matters here, and the count is printed so the clipping is visible rather
   ;; than silent (HANDOFF 5.0 "no silent caps").
   (let ((pts (ly:skyline->points sky X)))
     (format #t "PROBESK NPTS ~a ~a ~a ~a n=~a\n" pg sys k which (length pts))
     (for-each
      (lambda (p)
        (if (and (skb-finite? (car p)) (< (car p) 14.0) (> (car p) -6.0))
            (format #t "PROBESK PT ~a ~a ~a ~a x=~a y=~a\n"
                    pg sys k which (car p) (cdr p))))
      pts)))

#(define (skb-dump-align sg pg sys)
   (let ((align (ly:grob-object sg 'vertical-alignment)))
     (if (ly:grob? align)
         (let* ((els (ly:grob-array->list (ly:grob-object align 'elements)))
                (v (list->vector els))
                (cnt (vector-length v)))
           (format #t "PROBESK ALIGN ~a ~a elements=~a padding=~a stacking-dir=~a\n"
                   pg sys cnt
                   (ly:grob-property align 'padding)
                   (ly:grob-property align 'stacking-dir))
           (let loop ((k 0))
             (if (< k cnt)
                 (let* ((g (vector-ref v k))
                        (sky (ly:grob-property g 'vertical-skylines))
                        (dn (and (pair? sky) (car sky)))
                        (up (and (pair? sky) (cdr sky)))
                        (ext (ly:grob-extent g g Y)))
                   (format #t "PROBESK VAG ~a ~a ~a aff=~a yrel=~a xrel=~a ext=(~a . ~a) dn=~a dnx=~a up=~a upx=~a\n"
                           pg sys k
                           (ly:grob-property g 'staff-affinity)
                           (ly:grob-relative-coordinate g sg Y)
                           (ly:grob-relative-coordinate g sg X)
                           (car ext) (cdr ext)
                           (if dn (ly:skyline-max-height dn) 'NONE)
                           (if dn (ly:skyline-max-height-position dn) 'NONE)
                           (if up (ly:skyline-max-height up) 'NONE)
                           (if up (ly:skyline-max-height-position up) 'NONE))
                   (if dn (skb-dump-points dn "DOWN" pg sys k))
                   (if up (skb-dump-points up "UP" pg sys k))
                   (loop (1+ k)))))
           (let loopa ((a 0))
             (if (< a cnt)
                 (begin
                   (let loopb ((b (1+ a)))
                     (if (< b cnt)
                         (let ((ska (ly:grob-property (vector-ref v a) 'vertical-skylines))
                               (skb (ly:grob-property (vector-ref v b) 'vertical-skylines)))
                           (if (and (pair? ska) (pair? skb))
                               (let* ((dn (car ska))
                                      (up (cdr skb))
                                      (d (ly:skyline-distance dn up))
                                      (t (ly:skyline-touching-point dn up))
                                      (nv (+ (ly:skyline-max-height dn)
                                             (ly:skyline-max-height up))))
                                 (format #t "PROBESK PAIR ~a ~a ~a ~a dist=~a touch=~a hdn=~a hup=~a naive=~a deficit=~a\n"
                                         pg sys a b d t
                                         (if (skb-finite? t) (ly:skyline-height dn t) 'INF)
                                         (if (skb-finite? t) (ly:skyline-height up t) 'INF)
                                         nv (- d nv))))
                           (loopb (1+ b)))))
                   (loopa (1+ a)))))))))

#(define (skb-dump-pages layout pages)
   ;; Page 1 / system 0 only.  The count of what was skipped is printed for the same reason
   ;; the point list prints its length.
   (let* ((page (car pages))
          (lines (ly:prob-property page 'lines)))
     (format #t "PROBESK PAGE 1 systems=~a pages=~a (only page 1 system 0 dumped)\n"
             (length lines) (length pages))
     (let ((sys (car lines)))
       (let ((sg (ly:prob-property sys 'system-grob)))
         (if (ly:grob? sg)
             (skb-dump-align sg 1 0))))))

%% THE CLEF GLYPHS' OWN VERTICAL SKYLINES, added 2026-07-28 to close a verification gap the
%% port had: the G clef's profile was checked against this file's PAIR dump vertex by vertex,
%% but clefs.F and clefs.C were only baked by the SAME CODE and never compared with LilyPond.
%% "It came out of the same function" is not a measurement (HANDOFF 5.3), so they are asked
%% for directly here.  Printed in the GLYPH's own frame -- the grob's skyline is about its own
%% reference point, which is the line the clef names -- so the numbers can be read straight
%% against GlyphSkylinesGenerated.cs's ClefSky{G,F,C}{D,U}.
#(define (skb-dump-clef name)
   (lambda (grob)
     (let ((sky (ly:grob-property grob 'vertical-skylines)))
       (if (pair? sky)
           (begin
             (format #t "\nPROBESK CLEFGLYPH ~a down-max=~a down-x=~a up-max=~a up-x=~a\n"
                     name
                     (ly:skyline-max-height (car sky))
                     (ly:skyline-max-height-position (car sky))
                     (ly:skyline-max-height (cdr sky))
                     (ly:skyline-max-height-position (cdr sky)))
             (for-each
              (lambda (p)
                (if (skb-finite? (car p))
                    (format #t "PROBESK CLEFPT ~a DOWN x=~a y=~a\n" name (car p) (cdr p))))
              (ly:skyline->points (car sky) X))
             (for-each
              (lambda (p)
                (if (skb-finite? (car p))
                    (format #t "PROBESK CLEFPT ~a UP x=~a y=~a\n" name (car p) (cdr p))))
              (ly:skyline->points (cdr sky) X)))))))

\book {
  \paper { property-defaults.fonts.serif = "LilyPond Serif" }
  \score {
    \new Staff \with { \override Clef.after-line-breaking = #(skb-dump-clef "G") }
    { c'1 }
  }
}
\book {
  \paper { property-defaults.fonts.serif = "LilyPond Serif" }
  \score {
    \new Staff \with { \override Clef.after-line-breaking = #(skb-dump-clef "F") }
    { \clef bass c1 }
  }
}
\book {
  \paper { property-defaults.fonts.serif = "LilyPond Serif" }
  \score {
    \new Staff \with { \override Clef.after-line-breaking = #(skb-dump-clef "C") }
    { \clef alto c'1 }
  }
}

skbTag =
#(define-scheme-function (tag) (string?)
   ;; The serif pin is copied from page-vertical.ly for the reason its header gives: the svg
   ;; backend otherwise resolves `serif` through fontconfig, i.e. per machine.
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBESK BOOK ~a\n" tag)
                                      (skb-dump-pages layout pages)) } #})

%% LYRB — byte-for-byte the book of the same name in page-vertical.ly, including its paper.
%% It has to be that book and not a shorter one: the entry being explained rides on it, and
%% HANDOFF 5.0's trap list is mostly cases where "the same music" was not.
\book {
  \skbTag "LYRB"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new Staff { \new Voice = "mel" { \repeat unfold 120 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 480 { no } }
      \new Staff { \repeat unfold 120 { g'4 a' g' a' } }
    >>
  }
}

%% LYRBV — the two-verse twin, likewise copied.  It is here because the ledger reads the two
%% together: with one verse the accumulated profile binds over the next staff's CLEF, with
%% two the lyric's own outline binds instead, so the pair says WHICH element owns the
%% deficit rather than only how big it is.
\book {
  \skbTag "LYRBV"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new Staff { \new Voice = "mel" { \repeat unfold 120 { g'4 a' g' a' } } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 480 { no } }
      \new Lyrics \lyricsto "mel" { \repeat unfold 480 { no } }
      \new Staff { \repeat unfold 120 { g'4 a' g' a' } }
    >>
  }
}

%% PLAINFB — PLAIN with the LOWER staff in bass clef, added 2026-07-28 after the port: the
%% first two fixtures whose snapshots moved were a treble-over-BASS grand staff and an ossia,
%% and their gaps closed by 0.890000 and 0.530000, not by the 0.105961 two G clefs give.  A
%% number that large has to be LilyPond's or it is a bug in the port, so it is asked for here
%% rather than argued about.
%%
%% ★ PREDICTION, before the run: the deficit is LARGER than the G-over-G one, because an F
%% clef's highest ink is the top of its curl at the LEFT while a G clef's deepest is its tail
%% at x=1.84 -- the two extremes are further apart in x than two G clefs' are, and the
%% deficit IS that separation.  ⚠️ FALSIFIER: if LilyPond's deficit here is ~0.105961 or
%% smaller, Lily#'s 0.890000 is not LilyPond's and the port is wrong, not the fixture.
\book {
  \skbTag "PLAINFB"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new Staff { \repeat unfold 120 { g'4 a' g' a' } }
      \new Staff { \clef bass \repeat unfold 120 { g4 a g a } }
    >>
  }
}

%% PLAIN — LYRB with the lyric line REMOVED and nothing else changed.  This is the control
%% P2 is stated against, and it is the "existing book with one line taken away" shape
%% HANDOFF 5.0 recommends: whatever LilyPond does to two facing clefs, it does here too, with
%% no lyric anywhere in the accumulation.  If the deficit survives the removal it is the clef
%% pair; if it vanishes it never was.
\book {
  \skbTag "PLAIN"
  \paper { max-systems-per-page = #4 ragged-bottom = ##t }
  \score {
    <<
      \new Staff { \repeat unfold 120 { g'4 a' g' a' } }
      \new Staff { \repeat unfold 120 { g'4 a' g' a' } }
    >>
  }
}
