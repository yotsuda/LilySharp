\version "2.26.0"
%% LP FIDELITY PROBE — THE LYRIC COLUMN DISTANCE: INK AT THE DRAWN SIZE, A WORD SPACE,
%% AND A HYPHEN SPACE — NOT ONE INVENTED PADDING AT AN OBSOLETE SIZE.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe lyric-column-spacing.ly -Prefix PROBEX
%%
%% THE DEFECT THIS MEASURES (HANDOFF 2H, found 2026-08-20 while diagnosing the row-vs-sings
%% X drift): Lily#'s LyricSpacing reserves EVERY adjacent-syllable pair as
%%   advance(text at 3.2 ss) halves + lyricPadding 1.0
%% — the 3.2 is the pre-em-correction lyric size (the syllable is DRAWN at 2.469417), and
%% the 1.0 is an invented constant whose stated rationale ("cannot measure the face at
%% layout time") has been false since the bundled-face port. LilyPond has no such knob.
%% Its lyric column distance is built from THREE ported facts (all lily/ sources):
%%   (1) ink at the drawn size — the syllable's stencil extent joins its paper column's
%%       spacing boxes verbatim (extra-spacing-width (0.0 . 0.0), scm/define-grobs.scm
%%       LyricText) and every adjacent column pair is rodded at skyline distance + padding
%%       0.1 (spacing-spanner.cc:315-316 set_column_rods -> separation-item.cc:56);
%%   (2) BETWEEN WORDS a LyricSpace spanner rods the two syllables' INK apart by
%%       minimum-distance 0.45 (hyphen-engraver.cc:107 makes one wherever no hyphen/vowel
%%       transition stands; lyric-hyphen.cc:163-179 set_spacing_rods adds
%%       minimum-distance + bounds_protrusion, i.e. ink edge to ink edge);
%%   (3) BETWEEN HYPHENATED SYLLABLES the LyricSpace is replaced by a LyricHyphen whose
%%       rod is minimum-distance 0.1 (define-grobs.scm LyricHyphen) — and the dash itself
%%       claims NO space mid-line: print VANISHES when l < dash+2*padding
%%       (lyric-hyphen.cc:108-121), so the fork word-vs-hyphen is 0.45 - 0.1 = 0.35 per gap.
%% And a fourth fact ABOUT THE BAR LINE: a syllable's box and a bar line's box do not
%% overlap in Y (LyricText even RECEDES 0.2 each side, extra-spacing-height (0.2 . -0.2)),
%% so the skyline distance between a lyric and a bar line is minus infinity — LilyPond
%% reserves NOTHING between a syllable and a bar line; only the next SYLLABLE binds,
%% straight across the bar. Lily# instead cuts every reservation AT the bar line
%% (leading/trailing extents + MinItemGap 0.4) — the "measured at the barline" model.
%%
%% THE BOOKS (HANDOFF 5.0-1 — the LP-identical pair is LCW's own two gaps, and LCN/LCC):
%%   LCW  eight quarters a', "mum" under every note, all separate words. Points: the
%%        in-bar word gap (head 0 -> 1) and the CROSS-BARLINE word gap (head 3 -> 4).
%%        LP-side identity predicted: the bar line is invisible to lyric rods, so the two
%%        gaps read the SAME number. Lily# splits the cross-bar gap at the bar line with
%%        different constants — it forks where LilyPond is identical.
%%   LCH  the same eight quarters, the same "mum", hyphenated WITHIN each bar and a word
%%        boundary ACROSS it (that is the .lys twin's spelling: its lyric bars are
%%        `mum -- mum -- mum -- mum | ...`, so the LP side spells the same words — the
%%        first draft hyphenated all seven gaps and was one connector different from its
%%        twin at the bar, the band-floor pair's incident shape, caught before measuring
%%        the Lily# side). ONE variable from LCW inside the bar: the connectors.
%%        LilyPond forks by 0.35 exactly; Lily# reads the SAME number on LCW and LCH
%%        (its 1.0 is connector-blind) — the same one-number-for-two-arrangements
%%        signature as the band-floor pair. And the book's OWN cross-barline gap is a
%%        word gap, so in-book it re-reads LCW's 6.322649 next to the hyphen's 5.972649.
%%   LCM  the same eight quarters, "run" — a narrower word. The LCW-LCM fork is the ink
%%        width difference at the DRAWN size on the LP side; Lily#'s fork is the same
%%        difference at 3.2/2.469417 = 1.2959x — the size invention, isolated from the
%%        padding invention by the fork (the 1.0-vs-0.45 constant cancels in it).
%%   LCN  four halves a', "nu" under each: a syllable chosen so TRUE ink + 0.45 sits
%%        UNDER the natural half-note gap (no LilyPond trace) while Lily#'s 3.2-size
%%        measure + 1.0 sits OVER it (its reservation binds). The real-corpus face:
%%        books where LilyPond's lyric terms leave no mark and Lily# still widens.
%%        ⚠️ FIRST MEASURED WITH "nun" AND THAT BOOK WAS IN THE WRONG REGIME: Schola's
%%        "nun" inks 4.506917 (guessed ~3.7), + 0.45 = 4.956917 OVER the natural half
%%        gap 4.275445 (guessed ~4.7 from note-to-note.half, which lives in a
%%        quarter-shortest book — the all-half book's shortest is the half, so its
%%        natural gap is SMALLER). The rod bound and LCN forked from LCC by 0.681472.
%%        The no-bind regime must be verified against the book's OWN control, not
%%        against a gap borrowed from another shortest-context — with "nu" (ink
%%        3.004611 + 0.45 = 3.454611 < 4.275445) LCN reads LCC's number to the DIGIT.
%%   LCC  LCN's notes with no lyrics at all — the identity control: LilyPond must read
%%        LCN == LCC to the digit.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2), mechanism first:
%%  * LCW in-bar = ink("mum" at 2.469417) + 0.45, order 6-7 ss (Schola m ~0.94 em);
%%    well over the all-quarter natural gap ~3.0 (note-to-note.quarter-shortest), so the
%%    rod BINDS — that is the regime, and the falsifier is a reading at the natural gap.
%%  * LCW cross-barline == LCW in-bar (the bar line does not participate). FALSIFIER:
%%    a fork here means the bar line DOES cost something and fact (4) above is wrong —
%%    then Lily#'s barline-split model is not an invention but a different constant.
%%  * LCH = LCW - 0.35 per gap (0.45 -> 0.1), and the mid-line hyphens DO NOT PRINT
%%    (ink gap 0.1 < dash 0.66 + 2*0.07).
%%  * LCM = LCW - (ink "mum" - ink "run") at the drawn size, order 2 ss.
%%  * LCN == LCC to the digit (the lyric terms leave no trace once the syllable is
%%    narrow enough — see the "nun" incident on LCN above for the first, wrong take).
%%  * Which side diverges (HANDOFF 5.0): Lily# reads LCW in-bar == LCH (connector-blind),
%%    forks LCW's two gaps (barline-split), and reads LCN > LCC (the 30%-oversized
%%    reservation binds where LilyPond's true one does not).
%%
%% ⚠️ The .lys twins were exported with `lysc ly` (scratch/p222) and the music below is
%% pasted from those twins verbatim (\fixed c' spelling). The LYRIC lines are hand-added:
%% the exporter does not export lyrics rows yet (HANDOFF 1 small-item "twin の歌詞行") —
%% they are one word repeated, so cross-check them against the .lys by eye AND by the
%% dumped LyricText count (8/8/8/4/0).
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
                                  (if (memq nm '(NoteHead LyricText LyricHyphen BarLine))
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

%% Both text faces pinned (pedal-lyric-stack.ly's reason): the syllables ARE the binding
%% ink here, so the face must be the one Lily# measures with.
probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEX BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

quarters = \fixed c' {
  \time 4/4
  \key c \major
  a4 a a a |
  a4 a a a |
}

halves = \fixed c' {
  \time 4/4
  \key c \major
  a2 a |
  a2 a |
}

%% LCW — WORD GAPS, in-bar and cross-barline. The LP identity book.
\book {
  \probeTag "LCW"
  \paper { ragged-right = ##t indent = 0 }
  \score {
    <<
      \new Staff { \new Voice = "mel" \quarters }
      \new Lyrics \lyricsto "mel" { mum mum mum mum mum mum mum mum }
    >>
  }
}

%% LCH — THE SAME BOOK HYPHENATED. One variable from LCW: the connectors.
\book {
  \probeTag "LCH"
  \paper { ragged-right = ##t indent = 0 }
  \score {
    <<
      \new Staff { \new Voice = "mel" \quarters }
      \new Lyrics \lyricsto "mel" {
        mum -- mum -- mum -- mum  mum -- mum -- mum -- mum }
    >>
  }
}

%% LCM — A NARROWER WORD. The LCW-LCM fork is the ink difference at the drawn size.
\book {
  \probeTag "LCM"
  \paper { ragged-right = ##t indent = 0 }
  \score {
    <<
      \new Staff { \new Voice = "mel" \quarters }
      \new Lyrics \lyricsto "mel" { run run run run run run run run }
    >>
  }
}

%% LCN — THE NO-BIND BOOK: true ink + 0.45 under the natural half gap.
\book {
  \probeTag "LCN"
  \paper { ragged-right = ##t indent = 0 }
  \score {
    <<
      \new Staff { \new Voice = "mel" \halves }
      \new Lyrics \lyricsto "mel" { nu nu nu nu }
    >>
  }
}

%% LCC — LCN'S NOTES, NO LYRICS. LilyPond must read LCN == LCC to the digit.
\book {
  \probeTag "LCC"
  \paper { ragged-right = ##t indent = 0 }
  \score {
    \new Staff { \new Voice = "mel" \halves }
  }
}
