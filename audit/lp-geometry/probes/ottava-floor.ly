\version "2.26.0"
%% LP FIDELITY PROBE — where the OTTAVA BRACKET's LINE sits above the staff, in the two
%% regimes of side-position-interface.cc aligned_side: the staff-padding FLOOR and the
%% note-column SUPPORT.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe ottava-floor.ly (two tiny books).
%%
%% WHAT IS BEING MEASURED, AND WHY THE CORPUS HAD NO POINT FOR IT
%%
%% OttavaBracket declares (staff-padding . 2.0) and (padding . 0.5)
%% (scm/define-grobs.scm), consumed by side-position-interface.cc:401-453 aligned_side:
%% the grob's REFPOINT is floored at staff_extent[UP] + staff-padding, over whatever the
%% support (the note columns) asked for. Lily#'s OutsideStaffStacker has no such floor for
%% any grob but TextScript (ported 2026-07-29, session 30, ledger
%% textscript.no-descender.staff-to-baseline); Ottava (2.0), TrillSpanner (1.0),
%% TextSpanner (0.8) and DynamicLineSpanner (0.1) were left NAMED but unmeasured. This
%% probe opens the largest of the four.
%%
%% THE ANCHOR: ottava-bracket.cc print puts the dashed LINE at the stencil's own Y=0 and
%% centres the text on it (text.align_to (Y_AXIS, CENTER), line built at Offset(len, 0)),
%% so the grob's relative coordinate IS the drawn line — the same physical anchor Lily#'s
%% DrawOttavaBrackets places (its YUp is the line's Y). ⚠️ Lily# draws the TEXT's
%% BASELINE on the line where LilyPond centres the text's INK on it — that second claim
%% is a DRAWING difference the rel dump cannot see; the ext dump rides along for it
%% (a centred text reads a roughly symmetric ext about 0, a baseline-anchored one would
%% not).
%%
%% THE PAIR: OTF engraves \ottava 1 over notes DRAWN third-space c'' (written c''' minus
%% the ottava octave) — column top ≈ 1.05 above the refpoint, so every support-side
%% constraint (+0.5 side padding, +0.46 outside-staff) is far below the floor and the
%% floor decides. OTC is the same music two octaves up (drawn c''', two ledger lines):
%% column top ≈ 4.5, the support decides, the floor loses. Bar 2 of each book is loco at
%% the drawn pitch, so both books carry one bracket over bar 1 only.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0, with signs):
%%   * OTF: rel − staffRefpoint = 4.050000 EXACT (staff ink 2.050000 + staff-padding 2.0)
%%     — six-digit round, like TextScript's 2.550000. If it reads HIGHER, something else
%%     (the text's own skyline, an outside-staff term) stands on the floor; if LOWER, the
%%     floor is not on the refpoint and the TextScript reading does not generalize.
%%   * OTC: strictly HIGHER than 4.05 (sign certain), around drawn-column-top + a padding
%%     term (≈ 5.0). The decomposition (0.5 side padding vs 0.46 outside-staff) is left
%%     to the measurement — both candidates are written here so the answer picks one.
%%   * FORK: OTF at 4.050000 → port is the TextScript floor with 2.0, same three lines.
%%     OTF ≈ OTC's shape → the floor never binds for ottava and the port target is the
%%     support arithmetic instead.
%%   * FALSIFIER: OTF == OTC means the pitch edit did not switch the regime and the pair
%%     measured nothing — treat as unmeasured, do not record.
%%
%% ⚠️ The ottava label is bold italic serif TEXT; its ink is in the grob's extent and in
%% skylines, so the serif pin is load-bearing (svg backend resolves fonts.serif via this
%% machine's fontconfig otherwise; page-vertical.ly's header has the history).

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
                   ;; The OttavaBracket rides along: rel is its refpoint (= its dashed
                   ;; line) about the SYSTEM refpoint, ext its own ink about that line.
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (if (eq? nm 'OttavaBracket)
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

%% OTF — the FLOOR regime: drawn third-space c'' under the bracket, every support
%%     constraint far below staff ink + 2.0.
\book {
  \probeTag "OTF"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \ottava 1 c'''4 c''' c''' c''' \ottava 0 | c''4 c'' c'' c'' \bar "|." }
  }
}

%% OTC — THE CONTROL, the SUPPORT regime: the same music two octaves up (drawn c''',
%%     two ledger lines), the column decides, the floor loses.
\book {
  \probeTag "OTC"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \ottava 1 c''''4 c'''' c'''' c'''' \ottava 0 | c'''4 c''' c''' c''' \bar "|." }
  }
}
