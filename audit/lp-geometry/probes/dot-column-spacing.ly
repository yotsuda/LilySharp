\version "2.26.0"
%% LP FIDELITY PROBE — THE DOT COLUMN IN THE SPACING BOX: HOW FAR DOES A DOTTED
%% COLUMN'S RESERVATION REACH, AND WHERE DOES IT START?
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe dot-column-spacing.ly -Prefix PROBEX
%%
%% THE QUANTITY THIS ISOLATES (HANDOFF §1 session 360 ⑺⒞②, opened 2026-09-10,
%% session 361): Lily# DRAWS its augmentation dots where LilyPond does — the head's
%% ink right plus ONE DOT WIDTH (0.45), through DotColumn.OffsetX, a rule the ledger
%% point dots.whole.column-to-dot-ink-left pins — but the column's SPACING BOX
%% (ItemSkylineFactory.AddDots), the keep-inside-line reach
%% (SpacingRules.CalculateNoteheadRightExtent) and the tie outline still read
%% EngravingDefaults.DotGap = 0.3, and none of them asks whether a FLAG pushed the dot
%% column right. Two spellings of one quantity: the drawn dot and the reserved dot are
%% 0.15 apart on every dotted note, and up to 0.76 apart on a flagged one.
%%
%% WHY THE POINT NEEDS TWO VOICES: in one voice a dotted note's own wish prices the
%% column pair by its duration, and at every natural density the ideal sits well above
%% the rod through the dot (a dotted quarter to an eighth: rod 2.60 under an ideal near
%% 3.7), so the box's reach never binds and the 0.15 is invisible. A CROSS-voice pair
%% has no wish (Note_spacing_engraver chains each voice's own rhythmic grobs, HANDOFF
%% session 361 ⑵), so the other voice's ink reaches the pair only as the ROD — and the
%% rod runs through the dot's box. That is where session 361 measured the −0.15 twice
%% (test/dot-force-down and test/dot-cross-voice-spacing) without opening a point.
%%
%% THE BOOKS (twin-exported by `lysc ly` from scratch/p362/lp/{DCX,DCC,DPF}.lys):
%%   DCX  << { c''4 c''4 } \\ { b'4. b'8 } >> in 2/4 — the dotted quarter shares column
%%        0 with the quarter, a second, so the down voice's head is shifted one head
%%        right and its dot stands after THAT; the pair col0 → col1/4 is cross-voice,
%%        its rod 0.1 + (1.3042 shift + 1.3042 head + 0.45 gap + 0.45 dot + 0.2 Dots
%%        extra-spacing-width) + 0.1 = 3.9084 (session 361's dfd dump read exactly this).
%%        col1/4 → col3/8 is the bare eighth (no wish either), the in-book control.
%%   DCC  the same second with the dot REMOVED (b'2 for b'4. b'8): the pair col0 → col1/4
%%        is voice 1's own wish (c''4 → c''4), whose ideal outranks the shifted half's
%%        rod (0.1 + 1.3042 + 1.3774 + 0.1 + 0.1 = 2.98). Anything Lily# is off by here
%%        is the SECOND's charge, not the dot's.
%%   DPF  << { g'8. s16 } \\ { s16 g'16 s8 } >> in 4/16 — g' sits ON a line, so its dot is
%%        lifted one row, and there the up stem's eighth flag is in the way: LilyPond's
%%        Dot_column stands the dot at 2.5174 = 1.2392 (stem) + 0.8282 (flag right) +
%%        0.45 (DotColumn's remarks carry the dump). The sixteenth is in the OTHER voice
%%        ONE SIXTEENTH later, so the pair g'8. → g'16 is a wish-less rod again (and
%%        nothing beams) whose bare ideal is the shortest's own 2.4.
%%        ⚠️ THE FIRST CUT OBSERVED NOTHING, AND THE FALSIFIER SAID SO: with the
%%        sixteenth at 3/16 (<< { g'8. s16 } \\ { s8. g'16 } >>) LilyPond read 4.301955
%%        = 2.4 + 1.2·log2(3), the bare 3/16 ideal, above the 3.3674 rod — a dotted
%%        note followed by half its value is ALWAYS that ratio, at any shortest. The
%%        pair has to be as short as the shortest for the rod to bind.
%%
%% THE QUANTITY: notehead anchor steps, left to right (Lily# sorts heads by X; a
%% shifted head is the second anchor of its column).
%%
%% PREDICTIONS, written before running (RULES 5.0-2):
%%  * DCX col0 → col1/4 (anchor 2 − anchor 0): LilyPond 3.908400 (the dfd dump);
%%    Lily# 3.758400, residual −0.150000 = 0.45 − 0.3, the box's gap. FALSIFIER: a
%%    Lily# residual off −0.15 means the box is missing something OTHER than the gap
%%    (the Dots esw 0.2, or the shift) and this ticket is mis-sized.
%%  * DCX col1/4 → col3/8 (anchor 3 − anchor 2): LilyPond 1.800000 (dfd), Lily# 0 —
%%    the bare eighth, an in-book control the dot cannot reach.
%%  * DCC col0 → col1/4 (anchor 2 − anchor 0): LilyPond near 3.0 (the quarter's wish;
%%    dotted-head-spacing.ly's quarter-gap reads 3.002245), Lily# 0. A residual here
%%    is the SECOND (collision shift refinement, session 361 ⑸⒝), not the dot.
%%  * DPF col0 → col3/16 (anchor step 0): LilyPond 3.367400 = 0.1 + (2.5174 + 0.45 +
%%    0.2) + 0.1 if the rod binds (the bare 3/16 ideal at shortest 1/16 is under it);
%%    Lily# reserves the dot at 1.3042 + 0.3 = 1.6042 with no push, rod 2.4542, so its
%%    ideal wins: residual = ideal − 3.3674, NEGATIVE and larger than 0.15. FALSIFIER:
%%    a LilyPond step under 3.36 means the rod does NOT bind and the book must be
%%    re-cut denser before it observes anything.
%%  * goes away when: the three spacing-side readers ask DotColumn.OffsetX — the
%%    renderer's own house — for the dot column's X, supports included.
%%
%% ragged-right, indent 0 (from the twin): force 0 = ideals and floors alone.
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
                                  (if (memq nm '(NoteHead Dots Flag BarLine))
                                      (format #t "PROBEX GROB ~a ~a name=~a anchor=~a x=(~a . ~a)\n"
                                              n i nm
                                              (ly:grob-relative-coordinate g sg X)
                                              (+ (ly:grob-relative-coordinate g sg X)
                                                 (car (ly:grob-extent g g X)))
                                              (+ (ly:grob-relative-coordinate g sg X)
                                                 (cdr (ly:grob-extent g g X)))))))
                              (ly:grob-array->list all)))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEX BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

dcx = \fixed c' {
  \time 2/4
  \key c \major
  << { c'4 c'4 | } \\ { b4. b8 | } >>
}

dcc = \fixed c' {
  \time 2/4
  \key c \major
  << { c'4 c'4 | } \\ { b2 | } >>
}

dpf = \fixed c' {
  \time 4/16
  \key c \major
  << { g8. s16 | } \\ { s16 g16 s8 | } >>
}

dcw = \fixed c' {
  \time 6/8
  \key c \major
  \once \override Stem.direction = #DOWN <b' c'' d'' e''>4. \once \override Stem.direction = #DOWN <f' g' a' b'>4. |
}

%% DCX — THE DOTTED CROSS-VOICE BOOK.
\book {
  \probeTag "DCX"
  \paper { ragged-right = ##t indent = 0 }
  \score { \new Staff { \clef "treble" \dcx } }
}

%% DCC — THE SAME SECOND, NO DOT.
\book {
  \probeTag "DCC"
  \paper { ragged-right = ##t indent = 0 }
  \score { \new Staff { \clef "treble" \dcc } }
}

%% DPF — THE DOT PUSHED BY ITS FLAG.
\book {
  \probeTag "DPF"
  \paper { ragged-right = ##t indent = 0 }
  \score { \new Staff { \clef "treble" \dpf } }
}

%% DCW — THE DOTS ARE NOT IN THE WISH.
\book {
  \probeTag "DCW"
  \paper { ragged-right = ##t indent = 0 }
  \score { \new Staff { \clef "treble" \dcw } }
}
