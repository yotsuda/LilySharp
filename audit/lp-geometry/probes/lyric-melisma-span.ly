\version "2.26.0"
%% LP FIDELITY PROBE — THE MELISMA-SPAN RESERVATION: A ROD ACROSS INTERMEDIATE COLUMNS
%% READS max(natural, need); A MIN-BUMP ON THE LAST SPRING OVER-OPENS BY THE IDEALS.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe lyric-melisma-span.ly -Prefix PROBEX
%%
%% THE DEFECT THIS MEASURES (HANDOFF 1 residual item ⒥, named 2026-08-20 when the
%% bound-voice pair's skip-gap refused to close past +1.507845): Lily#'s ReserveLyricLine
%% prices the distance between two syllable-carrying columns with BumpSpanMin, whose
%% have-check sums the spanned springs' MINIMUMS and puts the deficit on the LAST spring
%% — but a ragged line stands at the springs' IDEALS, so whenever the pair spans an
%% INTERMEDIATE column (a melisma's held notes) the drawn span over-opens by
%% (ideal − min) of each non-final spring. LilyPond's LyricSpace rod between the two
%% syllables (lily/lyric-hyphen.cc:163-179 set_spacing_rods -> Rod::add_to_cols) is a
%% RANGE constraint the spacer solves: the span reads max(natural, need) and the stretch
%% distributes over the springs. On an ADJACENT pair (one spring) the two models agree —
%% max(ideal, need) — which is why every lyrics.column.* point closed exact while the
%% bound-voice skip-gap kept the term: single-voice adjacent-syllable books can never
%% see it, and every MELISMA book can.
%%
%% THE BOOKS (music identical, twin-exported by `lysc ly` from scratch/p225 — a4( a a) a,
%% the slur so BOTH engines treat the first syllable as a melisma and LEFT-align it;
%% lyric lines hand-added, the exporter does not export lyrics):
%%   LMS  "mumum" held over columns 0-2 (a slur melisma; the .lys twin spells the holds
%%        `~ ~`), "mum" on column 3. The reservation spans springs 1..3 with springs 1,2
%%        intermediate. Wide enough to bind on both engines.
%%   LMN  the same music, "u" held the same way — the no-bind control: need ~2.1 under
%%        the natural 3-quarter span 9.007, so the lyric terms must leave no trace and
%%        the slur's own (null) spacing effect cancels.
%%
%% THE QUANTITY: notehead anchor steps (single voice, no collision — the same reading
%% every note-to-note point uses; syllable alignment cancels out of head positions).
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2), mechanism first. From Lily#'s
%% own live numbers (scratch/p225, measured before this probe was written):
%% need = W("mumum") + LyricSpace 0.45 + (w("mum")/2 − he.centre 0.6521) = 12.294 ss,
%% natural quarter spring: ideal 3.002245 / min 1.604200 (head 1.3042 + 0.3).
%%  * LP LMS width (head0→head3) = max(9.006735, 12.294…) = the rod's need, measured
%%    here to the digit. FALSIFIER: a reading at the natural 9.007 means LilyPond does
%%    NOT rod a melisma pair across held columns and Lily#'s whole melisma reservation
%%    is the invention — a different (larger) defect than the span-bump.
%%  * LP LMS first-gap (head0→head1) = width / 3 EXACTLY: three identical quarter
%%    springs stretch equally under a range rod. FALSIFIER: an unequal split means the
%%    held columns' springs are not plain quarter springs on the LP side.
%%  * LP LMN = natural: width 9.006735-class (3 × 3.002245), first-gap 3.002245 — the
%%    same digits as lyrics.column.bound-voice.no-bind.step-gap's family.
%%  * Which side diverges (HANDOFF 5.0): Lily# LMN exact (nothing binds); Lily# LMS
%%    first-gap reads its NATURAL 3.002245 (the bump leaves intermediate springs at
%%    ideal — LilyPond distributes, Lily# does not: residual ≈ 3.002 − width/3 ≈ −1.1)
%%    and width reads need + 2 × (3.002245 − 1.604200) = need + 2.796090 (residual
%%    ≈ +2.796090, up to any need-arithmetic sliver between the engines — Lily#'s
%%    drawn width is 15.09 in the pre-probe measurement, i.e. 12.294 + 2.796).
%%
%% ⚠️ indent = 0 comes from the twin itself. ragged-right so force 0 = the rods and
%% ideals alone decide every X (the regime every note-to-note point in the ledger uses).
%%
%% NOTE: inside #(...) the comment character is `;`, not `%%`.

#(define (probe-dump-pages layout pages)
   (format #t "\nPROBEX PAPER line-width=~a indent=~a\n"
           (ly:output-def-lookup layout 'line-width)
           (ly:output-def-lookup layout 'indent))
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (format #t "PROBEX PAGE ~a systems=~a\n" n (length lines))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let* ((sys (car ls))
                        (sg (ly:prob-property sys 'system-grob)))
                   (if (ly:grob? sg)
                       (let ((all (ly:grob-object sg 'all-elements)))
                         (if (ly:grob-array? all)
                             (for-each
                              (lambda (g)
                                (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                                  (if (memq nm '(NoteHead LyricText BarLine))
                                      (format #t "PROBEX GROB ~a ~a name=~a anchor=~a x=(~a . ~a) text=~a\n"
                                              n i nm
                                              (ly:grob-relative-coordinate g sg X)
                                              (+ (ly:grob-relative-coordinate g sg X)
                                                 (car (ly:grob-extent g g X)))
                                              (+ (ly:grob-relative-coordinate g sg X)
                                                 (cdr (ly:grob-extent g g X)))
                                              (if (eq? nm 'LyricText)
                                                  (ly:grob-property g 'text)
                                                  "")))))
                              (ly:grob-array->list all)))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

%% Both text faces pinned (lyric-column-spacing.ly's reason): the syllables ARE the
%% binding ink here, so the face must be the one Lily# measures with.
probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEX BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

melody = \fixed c' {
  \time 4/4
  \key c \major
  a4 ( a a ) a |
}

%% LMS — THE SPAN BOOK: a wide syllable held over two columns, then a word.
\book {
  \probeTag "LMS"
  \paper { ragged-right = ##t indent = 0 }
  \score {
    <<
      \new Staff { \new Voice = "mel" \melody }
      \new Lyrics \lyricsto "mel" { mumum mum }
    >>
  }
}

%% LMN — THE NO-BIND CONTROL: the same span, too narrow to bind on either engine.
\book {
  \probeTag "LMN"
  \paper { ragged-right = ##t indent = 0 }
  \score {
    <<
      \new Staff { \new Voice = "mel" \melody }
      \new Lyrics \lyricsto "mel" { u u }
    >>
  }
}
