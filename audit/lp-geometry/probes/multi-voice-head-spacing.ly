\version "2.26.0"
%% LP FIDELITY PROBE — MULTI-VOICE NATURAL SPACING vs THE FOREIGN HEAD'S WIDTH:
%% WHOSE HEAD DOES THE IDEAL READ WHEN TWO VOICES SHARE A COLUMN?
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe multi-voice-head-spacing.ly -Prefix PROBEX
%%
%% THE QUANTITY THIS ISOLATES (HANDOFF 1's leftover from item K, the +0.0732 term of
%% lyrics.column.bound-voice.no-bind.skip-gap): on a two-voice bar whose column 0
%% carries a HALF note in one voice (head 1.3774) under a QUARTER in the other
%% (head 1.3042), Lily#'s natural col0->col1 gap stands 0.073200 wider than its
%% quarter-only gaps — the head-width DIFFERENCE, to the digit — while LilyPond's
%% dump (probe lyric-bound-voice-mapping.ly, book LBIC, narrow lyrics binding
%% nothing) read ALL THREE gaps equal at the 3.002245 family: LilyPond's natural
%% spacing is blind to the wider foreign head. That observation rode a LYRIC book;
%% this pair re-takes it with NO lyrics anywhere, so the number is the multi-voice
%% NOTE-SPACING model's alone.
%%
%% THE BOOKS (twin-exported by `lysc ly` from scratch/p226/MVH.lys / MVQ.lys —
%% the LBI family's music with the lyrics struck):
%%   MVH  sop c'4 x4 against alt e2 e4 e4 — the half's wide head shares column 0,
%%        the alt voice has NO note on column 1 (its next note is column 2).
%%   MVQ  the same music with alt e4 x4 — every column carries quarter heads only,
%%        the head-width control (the changed variable is ONLY the first alt head's
%%        glyph and the alt rhythm; LilyPond reads neither into the gaps).
%%
%% THE QUANTITY: notehead anchors. Column X = the DISTINCT anchor values in
%% ascending order (two voices put two heads on one X; the value, not the count,
%% is the column).
%%
%% PREDICTIONS, written before running (RULES 5.0-2), mechanism first:
%%  * LP MVH: col1-col0 = col2-col1 = col3-col2, all in the 3.002245 family —
%%    the LBIC dump already read heads 8.585/11.587/14.590/17.592 with the half
%%    present, so the wide head must leave NO trace. FALSIFIER: MVH's col0->col1
%%    wider than its other gaps means LilyPond DOES price the foreign head into
%%    the natural gap and Lily#'s +0.0732 is partially faithful — a different
%%    (smaller) defect than the one item K's leftover names.
%%  * LP MVQ == LP MVH gap-for-gap to the digit (the strong identity form: the
%%    changed variable is one LilyPond does not read).
%%  * Which side diverges (HANDOFF 5.0): Lily# MVH col0->col1 reads 3.075445
%%    (= 3.002245 + 0.073200, the number the bound-voice pair measured on
%%    2026-08-20 from the lyric side; suspected site: the cross-voice column
%%    floor pricing the half head's width — suspicion, not observation, to be
%%    named at port time); Lily# MVH's other gaps and ALL of MVQ exact.
%%
%% ragged-right, indent 0 (from the twin): force 0 = ideals and floors alone
%% decide every X — the regime every note-to-note point in the ledger uses.
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
                                  (if (memq nm '(NoteHead BarLine))
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

mvh = \fixed c' {
  \time 4/4
  \key c \major
  << { c'4 c' c' c' | } \\ { e2 e4 e4 | } >>
}

mvq = \fixed c' {
  \time 4/4
  \key c \major
  << { c'4 c' c' c' | } \\ { e4 e4 e4 e4 | } >>
}

%% MVH — THE WIDE-HEAD BOOK: a half under the first quarter.
\book {
  \probeTag "MVH"
  \paper { ragged-right = ##t indent = 0 }
  \score { \new Staff { \clef "treble" \mvh } }
}

%% MVQ — THE HEAD-WIDTH CONTROL: quarters everywhere.
\book {
  \probeTag "MVQ"
  \paper { ragged-right = ##t indent = 0 }
  \score { \new Staff { \clef "treble" \mvq } }
}
