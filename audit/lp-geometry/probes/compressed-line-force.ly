\version "2.26.0"
%% LP FIDELITY PROBE — WHICH spring gives ground on a COMPRESSED line, column by column.
%%
%% WHY THIS PROBE EXISTS. `compressed.line-start.time-to-first-note` is the last unexplained
%% X point in the ledger: on this line LilyPond's line start gives up 0.005535 of its ideal
%% 8.585000 (drawn head 8.579465) while Lily# gives up only 0.001039. The line-start spring
%% itself is NOT the disagreement — both sides build the same one, and both are checked
%% against LilyPond's own dump (ideal 2.000000 prefix-relative, min_distance 7.485000,
%% inverse_compress_strength 0.800000; staff-spacing.cc:210-220, and
%% LineStartColumnTests.MeteredLineStart_SpringIsLilyPonds).
%%
%% Since a compressed line moves every unsaturated spring by |force| * its own
%% inverse_compress_strength (lily/spring.cc:218-237), and Lily#'s line start moves by
%% 0.8 * 0.001298501, the residual IS the solved force:
%%
%%   Lily# (dumped from MultiStaffLayouter): 33 springs, natural 93.487905, target 93.424921,
%%   overflow 0.062984, sum of inverse_compress_strength 48.505489, force -0.001298501
%%   (and 0.062984 / 48.505489 = 0.001298501 exactly, so NOT ONE spring is saturated).
%%
%%   LilyPond, to give up 0.005535 through the same 0.8, must solve force -0.006918750.
%%
%% Simple_spacer::compress_line's force is (overflow / sum of active inverse_compress_strength)
%% (lily/simple-spacer.cc:264-283), so LilyPond's 5.33x larger force means its overflow is
%% ~5.33x larger, or its total compressibility ~5.33x smaller, or both.
%%
%% ⚠️ THE EXISTING RAGGED CONTROL CANNOT ANSWER THIS. ties-slurs-breaks-ragged.ly omits the
%% \bar "|." that ties-slurs-breaks.ly carries, so TSR and TSJ are NOT the same music and
%% TSR's natural width (101.907014) is not TSJ's. This probe fixes that: score CLN is TSJ's
%% music EXACTLY, including the final bar line, engraved ragged; score CLJ is the same music
%% justified on the same paper. The ONLY difference between them is `ragged-right`, so the
%% per-column difference CLN - CLJ is |force| * inverse_compress_strength for that spring and
%% nothing else. LilyPond's springs cannot be read from Scheme (setters only), so this
%% subtraction is how they are read.
%%
%% PREDICTION, written before the run (section 5.0). LilyPond's note springs are SOFTER in
%% compression than Lily#'s, not stiffer: Spacing_spanner::note_spacing builds
%% Spring (fraction*len, fraction*increment) so inverse_compress_strength = fraction*(len -
%% 1.2) (spacing-basic.cc:151-157 with spring.cc:204-210), and Note_spacing::get_spacing then
%% REPLACES the minimum with the skyline distance through set_min_distance, which does NOT
%% recompute the strength (note-spacing.cc:83 with spring.cc:143-153). For a quarter-to-quarter
%% spring that leaves LilyPond on 1.698045 where Lily# recomputes ideal - min = 1.398045. So
%% if the two lines overflowed by the same amount LilyPond would solve a SMALLER force and
%% give up LESS at the line start than Lily# — the opposite of what is measured. Therefore:
%%
%%   * the OVERFLOW is predicted to differ, with LilyPond's natural width for this music
%%     exceeding the 102.429921 line by ~0.335 ss where Lily#'s exceeds it by 0.062984 —
%%     i.e. CLN's width should come out near 102.765, NOT near 102.493;
%%   * the per-column deltas CLN - CLJ should be ROUGHLY PROPORTIONAL to Lily#'s per-spring
%%     inverse_compress_strength (a fifth of Lily#'s deltas each, since force is 5.33x and
%%     the strengths are within ~20%), with NO column pinned at 0 (nothing saturates).
%%
%% If instead CLN's width lands near 102.493 the prediction is WRONG and the mechanism is the
%% compressibility after all, which would then have to be ~5x and cannot be the 20% the source
%% accounts for — that outcome points at a spring Lily# has and LilyPond does not, or at a
%% rod saturating most of LilyPond's line (blocking_force >= 0 excludes a spring from
%% inv_hooke at simple-spacer.cc:259-262).
%%
%% OUTCOME, written after the run. The prediction was RIGHT about the overflow and RIGHT that
%% the compressibility was also wrong, and both were needed:
%%
%%   * CLW came back at 102.807014 against the 102.429921 line, i.e. LilyPond overflows by
%%     0.377093 where Lily# overflowed by 0.062984 — the predicted ~102.765, near enough.
%%   * CLW - CLJ over |force| = 0.006918750 reads LilyPond's springs directly, and they are
%%     the DURATION values to six places: 0.800000 at the line start, 1.698045 for
%%     quarter-to-quarter (= duration_space 2.898045 - increment 1.2, NOT ideal - min
%%     1.398045), 2.898045 for a half, 0.400000 bar-line-to-note, 54.701125 over the line.
%%     Ported; Lily# now produces 54.701125 exactly.
%%   * ⚠️ THE 0.314107 OF MISSING NATURAL WIDTH WAS NOT A SPACING DEFECT. It was the PAIR:
%%     the Lily# side of ledger score TSJ had the fixture's three phrases flattened into one
%%     melody block, and Lily# resets the relative frame at each phrase reference, so bars 4
%%     to 8 were engraved an OCTAVE UP and their stems pointed the other way. Restoring the
%%     phrases closed `compressed.line-start.time-to-first-note` to exact. The per-spring
%%     table this probe produced is what made that visible — bar 3's closing spring was exact
%%     while bar 4's was off by very nearly twice its own stem correction, which is a sign
%%     flip and not a magnitude error, and nothing but a stem direction flips that sign.
%%
%% So this probe stays as the way to read LilyPond's springs, and CLW stays as the natural
%% width of this music (probes/ties-slurs-breaks-ragged.ly is NOT that — it omits the final
%% bar line; see the warning at the top of that file).
%%
%% Dumps go to STDOUT, ONE RECORD PER LINE (a split record is cut in half by LilyPond's own
%% diagnostics on stderr — see the note in barline-spacing.ly).

\header { tagline = ##f }

#(define probe-done (make-hash-table))

#(define (nf x)
   (cond ((not (real? x)) "?")
         ((inf? x) (if (> x 0) "+inf" "-inf"))
         (else (format #f "~,6f" x))))

#(define (grobs-of col sym)
   (let ((ga (ly:grob-object col sym #f)))
     (if (ly:grob-array? ga) (ly:grob-array->list ga) '())))

#(define ((dump-columns tag) g)
   (if (not (hash-ref probe-done tag #f))
       (begin
         (hash-set! probe-done tag #t)
         (let* ((sys (ly:grob-system g))
                (cols (ly:grob-array->list (ly:grob-object sys 'columns)))
                (i 0))
           (format #t "\nPROBE ~a WIDTH x=~a ncols=~a linewidth=~a\n"
                   tag
                   (nf (apply max (cons 0.0 (map (lambda (c)
                                                   (ly:grob-relative-coordinate c sys X))
                                                 cols))))
                   (length cols)
                   (nf (ly:output-def-lookup (ly:grob-layout g) 'line-width)))
           (for-each
            (lambda (c)
              (let* ((musical? (grob::has-interface
                                c 'musical-paper-column-interface))
                     (rl (ly:grob-property c 'rhythmic-location))
                     ;; The RODS on this column, the only part of the spacing model Scheme
                     ;; can read as numbers (lily/spaceable-grob.cc:51-65 stores
                     ;; (other-column . distance) in 'minimum-distances). A rod raises a
                     ;; spring's min_distance through Spring::set_blocking_force, and a
                     ;; spring whose blocking force reaches 0 drops OUT of inv_hooke — which
                     ;; is one of the two ways LilyPond's force could be 5x Lily#'s.
                     (mins (ly:grob-object c 'minimum-distances '()))
                     (names (string-join
                             (map (lambda (e)
                                    (let ((n (grob::name e)))
                                      (if (symbol? n) (symbol->string n) "?")))
                                  (grobs-of c 'elements))
                             ",")))
                (format #t "\nPROBE ~a COL i=~a musical=~a x=~a bar=~a mom=~a rods=~a names=~a\n"
                        tag i (if musical? 1 0)
                        (nf (ly:grob-relative-coordinate c sys X))
                        (if (pair? rl) (car rl) "?")
                        (if (pair? rl) (format #f "~a" (cdr rl)) "?")
                        (if (pair? mins)
                            (string-join
                             (map (lambda (p) (if (pair? p) (nf (cdr p)) "?")) mins) "+")
                            "-")
                        (if (string-null? names) "-" names))
                (set! i (1+ i))))
            cols)))
   '()))

%% TSJ's music, character for character, INCLUDING the final bar line. Kept as one variable
%% so the two scores below cannot drift apart — a pair whose halves differ in the music is
%% what made the old ragged control unusable for this question.
%% ⚠️ NO \tempo, for the reason spelled out in ties-slurs-breaks.ly: a MetronomeMark draws a
%% notehead glyph and the line-start reader finds it first.
clmusic = {
  \time 4/4
  \key c \major
  c'4~ c'4 d'2 |
  d'2 e'2~ | e'4 f' g' a' |
  c'4( d' e' f') |
  g'4( f' e' d') | c'2 r2 |
  c'4~ c'4 r2 |
  b'4~ b'4 r2 \bar "|."
}

%% The line width comes from the paper, not from \layout, so both scores are measured on the
%% page TSJ is measured on (the default a4 that matches Lily#'s snapshot: 210mm / 1.75mm).
\paper {
  indent = 0
}

%% CLN — ragged on the SAME line width. ⚠️ MEASURED FIRST AND IT DOES NOT ANSWER THE
%% QUESTION, recorded so the trap is not walked into again: it comes back with ncols=30 and
%% width 90.310833, i.e. LilyPond BROKE these eight bars into two systems. That is itself the
%% first half of the answer — with the \bar "|." in place the natural width EXCEEDS the
%% 102.429921 line, where the old control (no final bar line) fit it at 101.907014 — but a
%% broken line cannot be subtracted column-for-column from CLJ.
\score {
  \new Staff \with {
    \override NoteHead.after-line-breaking = #(dump-columns "CLN")
  } \clmusic
  \layout { ragged-right = ##t }
}

%% CLW — the natural configuration that CLN cannot show: ragged on a line wide enough that
%% LilyPond keeps all eight bars together, so every spring sits on max (min_distance, ideal)
%% and nothing is broken. A spring's ideal is duration-based and a rod is geometric
%% (spacing-basic.cc:151-157, spacing-spanner.cc:229-296) — neither reads `line-width` — so
%% widening the line changes the SOLVED FORCE to 0 and nothing else. This is Simple_spacer's
%% `neutral_length` decomposed per column, and CLW - CLJ is then
%% |force| * inverse_compress_strength for each spring.
\score {
  \new Staff \with {
    \override NoteHead.after-line-breaking = #(dump-columns "CLW")
  } \clmusic
  \layout { ragged-right = ##t line-width = 250\mm }
}

%% CLJ — the same music JUSTIFIED, i.e. TSJ. Its LINESTART head must come out 8.579465, the
%% number the ledger already holds; if it does not, these two scores are not the pair they
%% claim to be and nothing below can be read.
\score {
  \new Staff \with {
    \override NoteHead.after-line-breaking = #(dump-columns "CLJ")
  } \clmusic
  \layout { ragged-right = ##f }
}
