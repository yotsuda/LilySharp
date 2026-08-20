\version "2.26.0"
%% LP FIDELITY PROBE — THE DOTTED NOTE'S LEFT HEAD: WHICH GLYPH DOES THE
%% NOTE-SPACING REFINEMENT PRICE, THE DRAWN HEAD OR THE SCALED DURATION'S?
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe dotted-head-spacing.ly -Prefix PROBEX
%%
%% THE QUANTITY THIS ISOLATES (HANDOFF 1 item M, opened 2026-08-21 when item L's
%% trial port unmasked it): Lily#'s SpacingRules.GetNoteValue derives the head
%% glyph from the SCALED duration's denominator, so a dotted half (3/4 -> 4)
%% prices its left-head refinement with the BLACK head 1.3042 while the renderer
%% draws the HALF head 1.3774 — and a tuplet whole (2/3 -> 3) the same way
%% (probe multi-voice-head-spacing.ly's books measured that side). LilyPond's
%% refinement reads the DRAWN stencil (note-spacing.cc:46-70 first_head), so the
%% forks are the head-width differences, to the digit.
%%
%% THE BOOKS (twin-exported by `lysc ly` from scratch/p226/DHD.lys / DHC.lys):
%%   DHD  c'2. c'4  — the dotted half's gap to the quarter: the drawn head is the
%%        HALF's; Lily# prices the black's.
%%   DHC  c'2 c'4 c'4 — the control: a plain half's gap (drawn == priced head,
%%        both engines) and a quarter-quarter gap.
%%
%% THE QUANTITY: notehead anchor steps (single voice, one head per column).
%%
%% PREDICTIONS, written before running (RULES 5.0-2):
%%  * Lily# DHD head0->head1 reads NARROWER than LilyPond by the half-vs-black
%%    head difference 0.073200 (1.377400 - 1.304200): the duration term is
%%    engine-shared, the head term is the fork. FALSIFIER: a residual off the
%%    0.0732 digit means LilyPond's dotted-left refinement reads something other
%%    than the bare drawn head (the dot? -- the LSD probe already measured the
%%    dot OUT of the alignment extent, but the note-spacing extent is its own
%%    walk) and item M's dotted arm is mis-modelled.
%%  * Lily# DHC head0->head1 (half left): 0.000000 — drawn == priced there
%%    today (1/2 -> denominator 2 either way), the frame control.
%%  * Lily# DHC head1->head2 (quarter left): 0.000000 — the family's digit.
%%  * goes away when: GetNoteValue reads the NOTATED value
%%    (BaseDuration.Denominator) for its drawn-ink consumers.
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

dhd = \fixed c' {
  \time 4/4
  \key c \major
  c'2. c'4 |
}

dhc = \fixed c' {
  \time 4/4
  \key c \major
  c'2 c'4 c'4 |
}

%% DHD — THE DOTTED-HEAD BOOK.
\book {
  \probeTag "DHD"
  \paper { ragged-right = ##t indent = 0 }
  \score { \new Staff { \clef "treble" \dhd } }
}

%% DHC — THE PLAIN-HALF CONTROL.
\book {
  \probeTag "DHC"
  \paper { ragged-right = ##t indent = 0 }
  \score { \new Staff { \clef "treble" \dhc } }
}
