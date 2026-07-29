\version "2.26.0"
%% LP FIDELITY PROBE — where the TRILL SPANNER's and the TEXT SPANNER's LINE sits above
%% the staff, in the two regimes of side-position-interface.cc aligned_side: the
%% staff-padding FLOOR (or whatever else stands on the quiet-support height) and the
%% note-column SUPPORT.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe spanner-floors.ly (four tiny books).
%%
%% WHAT IS BEING MEASURED, AND WHY THE CORPUS HAD NO POINT FOR IT
%%
%% TrillSpanner declares (staff-padding . 1.0) and (padding . 0.5); TextSpanner declares
%% (staff-padding . 0.8) and NO vertical padding (side-position's default is 0.0,
%% side-position-interface.cc:361-363). Both are consumed by aligned_side:401-453 — the
%% grob's REFPOINT is floored at staff_extent[UP] + staff-padding — and then by
%% avoid_outside_staff_collisions with outside-staff-padding 0.46 against the inside-staff
%% skylines. These are the last two of the four floors the TextScript port (session 30)
%% left NAMED but unmeasured (TextScript 0.5 and OttavaBracket 2.0 are closed and exact;
%% DynamicLineSpanner 0.1 is ported in DynamicEngraver.BaselineY). Lily#'s
%% OutsideStaffStacker applies NEITHER floor: the trill rests at an invented
%% StaffPadding + TrillGlyphHeight = 2.2 above the staff top, the text spanner at
%% staff edge + 0.46 + its box descent.
%%
%% THE ANCHOR: both grobs' stencil is ly:line-spanner::print, which builds the line at
%% the stencil's own Y=0 — the grob's rel coordinate IS the drawn line, the same claim
%% ottava-floor.ly pinned for OttavaBracket. ⚠️ TrillSpanner's left bound text (the
%% scripts.trill glyph) carries (stencil-offset . (0 . -1)) (define-grobs.scm:4068), so in
%% LilyPond the "tr" glyph hangs about ONE STAFF SPACE BELOW the line and its ink is part
%% of the grob's extent/skyline — where Lily# draws the glyph's baseline ON the line. The
%% ext dump rides along for that claim.
%%
%% THE PAIR (per grob): the F book engraves the spanner over DRAWN third-space c''
%% heads — every note-column support term far below the floor, so the QUIET height
%% (floor or the 0.46 pass, whichever stands higher) decides. The C book is the same
%% music two octaves up (drawn c'''' on ledger lines) — the column decides, the quiet
%% height loses.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0, with signs):
%%   * TRF: two candidates, and WHICH ONE WINS IS THE MEASUREMENT —
%%       floor:      rel − staffRefpoint = 2.05 + 1.0 = 3.050000 (six-digit round);
%%       0.46 pass:  2.05 + 0.46 + (the grob's downward reach under the glyph ≈ 1.0
%%                   from the stencil-offset) ≈ 3.51 (NOT round, decomposes).
%%     The floor candidate is round, the pass candidate is not — the reading itself
%%     names the mechanism. Sign vs Lily# is certain either way: Lily# rests at
%%     2.0 + 2.2 = 4.2 over the refpoint, ABOVE both candidates (residual ≈ +1.15 or
%%     +0.69), because the 2.2 is an invention (glyph height is not an aligned_side
%%     term).
%%   * TRC: strictly HIGHER than TRF (sign certain). Candidate: column top ≈ 4.485489
%%     (the OTC value for the same drawn head) + padding 0.5 + (refpoint − facing
%%     edge ≈ 1.0) ≈ 5.99; the 0.46 pass gives ≈ 5.95 and loses. Decomposition left to
%%     the measurement.
%%   * TSF: floor: 2.05 + 0.8 = 2.850000 (six-digit round). The 0.46 pass gives
%%     2.05 + 0.46 + (dash half-thickness ≈ 0.065) ≈ 2.58 and loses. If TSF reads NOT
%%     round, the floor is not binding and the port target is the 0.46 pass instead.
%%   * TSC: strictly HIGHER than TSF. Candidates: 0.46 pass = ledger-column outline top
%%     ≈ 4.485 + 0.46 + 0.065 ≈ 5.01 vs aligned_side = column top + 0.0 padding
%%     + 0.065 ≈ 4.55; the pass should win (no declared padding means aligned_side
%%     hugs the column). Whichever it is, the decomposition is recorded, not fitted.
%%   * FALSIFIER (per grob): F == C means the pitch edit did not switch the regime and
%%     the pair measured nothing — treat as unmeasured, do not record.
%%
%% MEASURED (2026-07-29, first run; the full record is in the ledger `why`s). Both
%% falsifiers held (F != C), and the TRILL resolved to a THIRD candidate the fork did
%% not offer: TRF = 3.550000 = staff ink 2.05 + padding 0.5 + reach 1.0 — staff-padding's
%% operative effect for a deep-reaching grob is include_staff (:219-222, :323-330
%% set_minimum_height puts the STAFF EXTENT INTO THE SUPPORT), over which the grob pays
%% its own padding; the :433-453 refpoint floor is subsumed whenever the facing reach
%% exceeds the staff-padding-minus-padding slack. TRC = 9.545000 = head BOX top
%% (7.5 + LILC 0.545) + 0.5 + 1.0 — same formula, column support. The TEXT SPANNER read
%% the naked floor: TSF = 2.850000 = 2.05 + 0.8 (round, ext bottom only -0.05); TSC =
%% 8.555000 = head box top 8.045 + outside-staff 0.46 + 0.05 (the 0.46 pass, since its
%% aligned_side padding is 0). ext rode along: TrillSpanner (-1.0 . 1.1) about the line
%% (the stencil-offset claim, to the digit), TextSpanner (-0.05 . 1.570859) — "rit."
%% baseline ON the line, t-ascender at em 2.2.
%%
%% ⚠️ The text spanner's "rit." is italic serif TEXT; the serif pin is load-bearing
%% (svg backend resolves fonts.serif via this machine's fontconfig otherwise;
%% page-vertical.ly's header has the history). The trill glyph is Emmentaler and does
%% not depend on it.

#(define (probe-dump-pages layout pages)
   (format #t "\nPROBEV PAPER top-margin=~a paper-height=~a line-width=~a\n"
           (ly:output-def-lookup layout 'top-margin)
           (ly:output-def-lookup layout 'paper-height)
           (ly:output-def-lookup layout 'line-width))
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (format #t "PROBEV PAGE ~a systems=~a\n" n (length lines))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let* ((sys (car ls))
                        (ext (ly:stencil-extent (ly:prob-property sys 'stencil) Y))
                        (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0))))
                   (format #t "PROBEV SYS ~a ~a y=~a ext=(~a . ~a) staff=(~a . ~a)\n"
                           n i
                           (ly:prob-property sys 'Y-offset 0.0)
                           (car ext) (cdr ext)
                           (car staff) (cdr staff))
                   ;; The spanner rides along: rel is its refpoint (= its drawn line)
                   ;; about the SYSTEM refpoint, ext its own ink about that line.
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (memq nm '(TrillSpanner TextSpanner))
                                        (format #t "PROBEV GROB ~a ~a name=~a rel=~a ext=(~a . ~a) x=(~a . ~a)\n"
                                                n i nm
                                                (ly:grob-relative-coordinate g sg Y)
                                                (car (ly:grob-extent g g Y))
                                                (cdr (ly:grob-extent g g Y))
                                                (+ (ly:grob-relative-coordinate g sg X)
                                                   (car (ly:grob-extent g g X)))
                                                (+ (ly:grob-relative-coordinate g sg X)
                                                   (cdr (ly:grob-extent g g X)))))))
                                (ly:grob-array->list all))))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEV BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

%% TRF — the trill's QUIET-SUPPORT regime: drawn third-space c'' under the spanner,
%%     every note-column term far below the floor and the 0.46 pass.
\book {
  \probeTag "TRF"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { c''4\startTrillSpan c'' c'' c''\stopTrillSpan | c'4 c' c' c' \bar "|." }
  }
}

%% TRC — THE CONTROL, the SUPPORT regime: the same music two octaves up (drawn c''',
%%     two ledger lines), the column decides.
\book {
  \probeTag "TRC"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { c''''4\startTrillSpan c'''' c'''' c''''\stopTrillSpan | c'''4 c''' c''' c''' \bar "|." }
  }
}

%% TSF — the text spanner's QUIET-SUPPORT regime. The left text is pinned to the same
%%     "rit." Lily#'s @rit engraves.
\book {
  \probeTag "TSF"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff {
      \override TextSpanner.bound-details.left.text = \markup \italic "rit."
      c''4\startTextSpan c'' c'' c'' | c''4 c'' c'' c''\stopTextSpan \bar "|."
    }
  }
}

%% TSC — THE CONTROL: two octaves up, the ledger column decides.
\book {
  \probeTag "TSC"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff {
      \override TextSpanner.bound-details.left.text = \markup \italic "rit."
      c''''4\startTextSpan c'''' c'''' c'''' | c''''4 c'''' c'''' c''''\stopTextSpan \bar "|."
    }
  }
}
