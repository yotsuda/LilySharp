\version "2.26.0"
%% LP FIDELITY PROBE — the observers for the notehead INK FRAME rule, raised AFTER the
%% port that needed them (2026-08-05, session 96). Run with
%% ../Measure-LilyPondProbe.ps1 -Probe notehead-ink-frame.ly
%%
%% WHY THIS EXISTS, AND WHY IT IS LATE
%%
%% Session 95 established, from LilyPond's own dump, that a NoteHead's grob EXTENT is its
%% INK (1.9620 whole / 1.3774 half / 1.3042 black) and not its advance (1.960 / 1.376 /
%% 1.304), and corrected SEVEN sites that framed a head by the advance. Two of those sites
%% had observers (the dynamics island and, through the shared column centre,
%% trill.x.wave-zone, which went -0.000060062 -> -0.000000249). FOUR DID NOT: the ledger
%% line's X, the augmentation dot's X and the fingering's X have no ledger point at all,
%% and were corrected on the LilyPond source plus the font table rather than on a residual.
%% That is a debt the commit named in its own message. These three books are the payment.
%%
%% ⚠️ THE POINT OF A LATE OBSERVER IS THAT IT CAN STILL SAY NO. If a book here does NOT
%% land, session 95's commits are where to look — that is the whole reason to raise the
%% points instead of trusting the rule.
%%
%% THE MUSIC IS GENERATED, NOT WRITTEN (HANDOFF: the octave trap). Each book's notes come
%% from `lysc ly` on the .lys twin recorded beside it in LpGeometryProbes.cs, so the
%% `\fixed c'` bridge is the exporter's and not a hand count.
%% ⚠️ ONE MARK IS HAND-INSERTED AND MUST STAY THAT WAY UNTIL THE EXPORTER CARRIES IT:
%% `lysc ly` DROPS `@finger` ("warning: @finger.2 dropped (out of scope)"), so the
%% generated FNG twin was `c'1` with no Fingering grob at all — a twin that compiles and
%% is DIFFERENT MUSIC, which is the failure mode the handoff records as "a dropped mark
%% makes a FALSE match". The `-2` below was added by hand to the generated line; the pitch
%% and octave are still the exporter's. Fixing the exporter closes this note.
%%
%% THE BOOKS (one claim each):
%%   LDG — ONE whole note two ledger lines above the staff, and NOTHING else that draws a
%%         ledger, so the LedgerLineSpanner's whole stencil IS this note's ledger pair and
%%         its X-extent can be read directly. LilyPond builds it as the head's grob extent
%%         widened by length-fraction (0.25) of that extent's own length
%%         (lily/ledger-line-spanner.cc:228-230), so BOTH terms are the ink and the
%%         ink/advance difference is amplified 1.5x.
%%   DOT — a dotted WHOLE note (6/4 so it fits), on a space so no dot shift fires. The dot
%%         column's base is the head's extent (lily/dot-column.cc:82-84).
%%   FNG — a whole note with a fingering: self-alignment-X = CENTER on the head, so the
%%         Fingering's ink centre is the head's ink centre
%%         (lily/self-alignment-interface.cc:147).
%% Whole heads everywhere on purpose: 1.962 against 1.960 is the LARGEST separation the
%% three note values offer (the black head's 0.0001 is what let the old wrong rule survive
%% a measurement — see AnchorCentreOffset's retracted note).
%%
%% PREDICTIONS, WRITTEN BEFORE RUNNING (HANDOFF 5.0-2, with the falsifier named):
%%   * LDG: NoteHead X width = 1.962000 six-digit. LedgerLineSpanner X width =
%%     1.962 + 2*(0.25*1.962) = 2.943000.
%%     ★ FALSIFIER, and it is the sharp one: 2.940000 means LilyPond widens by the ADVANCE
%%     (1.960*1.5), session 95's ledger change was wrong in BOTH the seed and the draw, and
%%     30 snapshots were rebased for nothing. Record which.
%%     ⚠️ A SECOND FORK: if the spanner's extent is WIDER than 2.943 the stencil is carrying
%%     something besides these two lines (a second column's request, the staff's own trim)
%%     and the book is not measuring what it claims — say so rather than fitting.
%%   * DOT: Dots ink LEFT - NoteHead ink RIGHT = one dot width. Emmentaler's dot is
%%     (0 . 0.45) with advance 0.448, and scm/define-grobs.scm DotColumn takes
%%     dot-column-interface::pad-by-one-dot-width, so 0.45 (the ink) is expected and 0.448
%%     is the fork worth recording. NOT asserted; measured.
%%   * FNG: Fingering ink centre - NoteHead ink LEFT = 0.981000 (= 1.962/2), six-digit.
%%     FALSIFIER: 0.980000 means CENTER is on the advance and FingeringEngraver was right
%%     before session 95 touched it.
%%   * Every book: exactly ONE NoteHead row. Two rows means the texture grew a voice and
%%     the book is unmeasured — do not record it.
%%
%% Lily# MIRRORS, written before its run (the point of writing them here is that session
%% 95 already changed the code, so a mirror that MISSES is the interesting outcome):
%%   * All three land on LilyPond's number, because the ink frame is exactly what those
%%     four sites were changed to read. Expected residual: the e-5 face-sliver family or
%%     exact.
%%   * ⚠️ AND THE PRE-PORT VALUES ARE NAMED SO THE POINTS HAVE A POSITIVE CONTROL:
%%     before session 95 Lily# would have read ledger width 2.940000 (advance*1.5),
%%     dot left - head right = 0.45 measured off 1.960 instead of 1.962 (i.e. the whole
%%     reading 0.002 left), and fingering centre 0.980000. Any of those appearing now
%%     means a site was missed, not that the rule is wrong.

#(define (probe-dump-pages layout pages)
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let ((sys (car ls)))
                   (let ((sg (ly:prob-property sys 'system-grob)))
                     (if (ly:grob? sg)
                         (let ((all (ly:grob-object sg 'all-elements)))
                           (if (ly:grob-array? all)
                               (for-each
                                (lambda (g)
                                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                    (cond
                                     ;; ⚠️ A SPANNER DOES NOT SET AN X-EXTENT. The first
                                     ;; version of this dump asked LedgerLineSpanner for
                                     ;; `ly:grob-extent` and got (+inf.0 . -inf.0) — the
                                     ;; EMPTY interval, in all three books. That is not
                                     ;; "no ledgers"; LDG draws two. The drawn span lives
                                     ;; in the STENCIL, which Ledger_line_spanner::print
                                     ;; builds, so it is read from there.
                                     ((eq? nm 'LedgerLineSpanner)
                                      (let ((st (ly:grob-property g 'stencil)))
                                        (if (ly:stencil? st)
                                            (let ((ext (ly:stencil-extent st X)))
                                              (format #t "PROBEN GROB ~a x=(~a . ~a)\n"
                                                      nm
                                                      (+ (ly:grob-relative-coordinate g sg X)
                                                         (car ext))
                                                      (+ (ly:grob-relative-coordinate g sg X)
                                                         (cdr ext))))
                                            (format #t "PROBEN GROB ~a NO-STENCIL\n" nm))))
                                     ((or (eq? nm 'NoteHead) (eq? nm 'Dots) (eq? nm 'Fingering))
                                      (format #t "PROBEN GROB ~a x=(~a . ~a)\n"
                                              nm
                                              (+ (ly:grob-relative-coordinate g sg X)
                                                 (car (ly:grob-extent g g X)))
                                              (+ (ly:grob-relative-coordinate g sg X)
                                                 (cdr (ly:grob-extent g g X))))))))
                                (ly:grob-array->list all))))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEN BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

%% LDG — generated from ldg.lys by `lysc ly`; two ledger lines above the treble staff.
\book {
  \probeTag "LDG"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 4/4 \key c \major c''1 | } }
    \layout { indent = 0\mm }
  }
}

%% DOT — generated from dot.lys; a dotted WHOLE note, on a space so no dot shift fires.
\book {
  \probeTag "DOT"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 6/4 \key c \major c'1. | } }
    \layout { indent = 0\mm }
  }
}

%% FNG — generated from fng.lys; the `-2` is HAND-INSERTED (see the header: `lysc ly`
%%       drops @finger, and the generated twin carried no Fingering grob at all).
\book {
  \probeTag "FNG"
  \paper { ragged-bottom = ##t indent = 0 }
  \score {
    \new Staff { \clef treble \fixed c' { \time 4/4 \key c \major c'1-2 | } }
    \layout { indent = 0\mm }
  }
}
