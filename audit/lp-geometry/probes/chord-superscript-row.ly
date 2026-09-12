\version "2.26.0"
%% LP FIDELITY PROBE — WHAT DOES A *SUPERSCRIPTED* CHORD SYMBOL RESERVE ABOVE ITSELF?
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe chord-superscript-row.ly -Prefix PROBECS
%%
%% WHAT THIS WATCHES (2026-09-12, session 372). LilyPond raises a chord's quality with
%% \super (scm/chord-ignatzek-names.scm ignatzek-format-chord-name), so `Em7' is a root run
%% on the baseline and a `7' one magstep(font-size) higher — and the raised digit is the
%% TOP of the symbol's ink. Lily# ported that draw on 2026-09-11. Ten measuring passes were
%% left behind on the old spelling (the symbol priced as ONE unraised run) and among them is
%% the one that reserves the room a row leading a LATER system needs: the page's per-measure
%% annotation extents. So Lily# drew a symbol whose top it had not reserved for, and the
%% system above came down onto it — by the raised run's own height, 0.59 on this ink.
%%
%% THE PAIR (one variable — the quality, i.e. whether there is a raised run at all):
%%   CSR — ChordNames row / Staff / Lyrics, two systems, chords `Em7' `A7' (a raised digit)
%%   CSN — THE CONTROL: the same book with `E' `A' (no raised run), nothing else changed
%%
%% ⚠️ THE SYMBOLS ARE SPELLED THE SAME BY BOTH ENGINES ON PURPOSE. A major seventh is NOT
%% used here: LilyPond's majorSevenSymbol draws a triangle and Lily#'s `layout
%% chordQualities' spells the quality with letters by default, so a `maj7' book would put a
%% VOCABULARY difference inside a SPACING reading (that is the whole of the open
%% +0.337483977 on lyrics.chord-run.staff-to-chord). A minor seventh and a dominant seventh
%% are the digit `7' in both.
%%
%% ⚠️ THE LYRICS LINE IS NOT DECORATION — the same reason dynamic-under-row.ly gives:
%% Lily#'s BuildLooseChainEnds declines a score with no lyric line at all, so a book without
%% one would not reach the branch under test.
%%
%% ⚠️ THE MUSIC IS QUIET THROUGHOUT (third-space c''), so the row rests on its own floor and
%% not on a note column that moves when the pitches do. The twin's body came out of
%% `lysc ly' on scratch/p373/csr.lys, not out of a head (HANDOFF 5.5).
%%
%% WHAT IS READ: the SYSTEM GAP — system 1's staff reference point to system 2's — which is
%% what the row leading system 2 is reserved inside. The row's own baseline above system 2's
%% staff is read too, as the control that should NOT move: the raised run changes the ink
%% TOP, and the row hangs by its ink BOTTOM.
%%
%% MEASURED (2026-09-12, 2.26.0, fonts pinned; PROBECS dump), system refpoint to system
%% refpoint, i.e. the two Y-offsets subtracted:
%%   CSR  10.775757854      CSN  10.182224744      difference  0.593533110
%% and the difference IS the raised run's own ink: the ChordName ext is (0 . 2.500823590)
%% against the control's (0 . 1.907290480). ⚠️ THE ROW'S BASELINE ABOVE ITS STAFF IS
%% 2.550000000 IN BOTH (the ChordNames VerticalAxisGroup's rel less the Staff's), which is
%% what makes the floor difference a reading of the ink TOP and of nothing else.
%% ⚠️ WITH THE SHIPPING SPACING BOTH BOOKS READ 12.000000 — the system-system
%% basic-distance, measured before the floor paper was cut. A pair can be exactly right and
%% say nothing; this one had to be re-cut before it did.
%%
%% Everything printed is in STAFF SPACES (see page-vertical.ly's header for why).

#(define (dump tag layout pages)
   (for-each
    (lambda (page)
      (for-each
       (lambda (sys)
         (let ((sg (ly:prob-property sys 'system-grob)))
           ;; The system's own place on the page: scm/page.scm:184-192 puts the stencil at
           ;; -(Y-offset + top-margin) from the paper's top edge, so two systems' Y-offsets
           ;; differ by exactly the distance between their reference points.
           (format #t "PROBECS ~a SYSTEM Y-offset=~a\n"
                   tag (ly:prob-property sys 'Y-offset 0.0))
           (if (ly:grob? sg)
               (let ((all (ly:grob-object sg 'all-elements)))
                 (if (ly:grob-array? all)
                     (for-each
                      (lambda (g)
                        (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                          (if (memq nm '(VerticalAxisGroup StaffSymbol ChordName))
                              (format #t "PROBECS ~a ~a rel=~a ext=(~a . ~a) aff=~a\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))
                                      (ly:grob-property g 'staff-affinity 'none)))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))

probeCS =
#(define-scheme-function (tag) (string?)
   #{ \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
               %% ⚠️ THE INTER-SYSTEM SPRING'S IDEAL AND MINIMUM ARE TAKEN AWAY, so the only
               %% thing holding the two systems apart is the skyline floor — which is the
               %% quantity this pair exists to read. MEASURED before the paper was cut this
               %% way: with the shipping spacing both books read 12.000000 exactly, the
               %% basic-distance, and the pair said nothing at all (HANDOFF 5.0 trap 7).
               %% The paper of the SCF/SCC fork, and Lily#'s twin is ClefFloorPaper.
               system-system-spacing.basic-distance = #0
               system-system-spacing.minimum-distance = #0
               property-defaults.fonts.serif = "LilyPond Serif"
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBECS BOOK ~a\n" tag)
                                      (dump tag layout pages)) } #})

%% ⚠️ NOT `chords': that is a LilyPond keyword and the parser says so.
raisedRow = \chordmode { e1:m7 | a1:7 | e1:m7 | a1:7 }
plainRow = \chordmode { e1 | a1 | e1 | a1 }
words = \lyricmode { no no no no no no no no no no no no no no no no }

quiet = \fixed c' {
  \time 4/4
  \key c \major
  c'4 c' c' c' |
  c'4 c' c' c' |
  \break
  c'4 c' c' c' |
  c'4 c' c' c'
}

%% CSR — the row's symbols carry a raised digit.
\book {
  \probeCS "CSR"
  \score {
    <<
      \new ChordNames \raisedRow
      \new Staff { \clef "treble" \new Voice = "mel" \quiet }
      \new Lyrics \lyricsto "mel" \words
    >>
  }
}

%% CSN — THE CONTROL: the same book with no raised run anywhere.
\book {
  \probeCS "CSN"
  \score {
    <<
      \new ChordNames \plainRow
      \new Staff { \clef "treble" \new Voice = "mel" \quiet }
      \new Lyrics \lyricsto "mel" \words
    >>
  }
}
