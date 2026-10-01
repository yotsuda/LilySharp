\version "2.26.0"
%% LP FIDELITY PROBE — A METRONOME MARK AT A MID-LINE METER CHANGE, AND AT A LINE'S END.
%% tempo-mark.ly pinned the mark's X at a LINE START (on the prefix's meter); this file
%% prices the two places the 01-piano-nocturne line-break divergence turned on (Lab
%% sessions/p734/lb): where the mark stands at a bar that OPENS with a key and meter change
%% mid-line, and how far right its ink may run when that bar ends a line.
%%
%% Run: lilypond -dbackend=null tempo-line-end.ly (cmd /d /s /c "… < NUL", RULES §5.5).
%%
%% LILYPOND-REF: scm/define-grobs.scm MetronomeMark — break-align-symbols (time-signature),
%%   self-alignment-X LEFT, X-offset self-alignment-interface::self-aligned-on-breakable;
%%   lily/metronome-engraver.cc:80-135 — its X-parent is the meter's break-align group, an
%%   element of the NonMusicalPaperColumn.
%% LILYPOND-REF: lily/simple-spacer.cc:431-432 get_column_description, :497-502
%%   get_line_forces — every column but the line starter keeps its extent inside the line.
%%
%% THE BOOKS:
%%   TMM  2/4 bar, then a bar opening with \key d \major \time 4/4 \tempo 4 = 120, mid-line.
%%   TME  a justified line of six 4/4 bars, \break after the sixth, which opens with
%%        \tempo "Molto più mosso, appassionato" — far wider than one bar.
%%
%% PREDICTION (written before the run):
%%   TMM  mark ink-left − TimeSignature ink-left = 0.000000 (the line-start rule, mid-line).
%%   TME  mark ink-right − the line's end BarLine ink-right = 0.000000: the rod binds, the
%%        last bar is widened to hold the mark, and the end column is the bar line's right.
%%
%% MEASURED 2026-10-01 (session 735) — both predictions held to six digits:
%%   TMM  MetronomeMark x=(18.868954 . 26.277847), TimeSignature x=(18.868954 . 20.568954): 0.
%%   TME  MetronomeMark x=(67.501318 . 102.429921), end BarLine x=(102.239921 . 102.429921): 0;
%%        bars 1-5 are 11.81 wide each and bar 6 is 35.69 — the rod widened it.
%% The ledger entries tempo.x.mid-line-meter-change and tempo.line-end.right-from-end-bar
%% carry the numbers (Lily# before the port: +4.046 and +20.174).
%%
%% NOTE: inside #(...) the comment character is `;`, not `%%`.

#(define (probe-grob-row n i g sg)
   (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
     (format #t "PROBEV GROB ~a ~a name=~a x=(~a . ~a)\n" n i nm
             (+ (ly:grob-relative-coordinate g sg X) (car (ly:grob-extent g g X)))
             (+ (ly:grob-relative-coordinate g sg X) (cdr (ly:grob-extent g g X))))))

#(define (probe-dump-pages layout pages)
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let ((sg (ly:prob-property (car ls) 'system-grob)))
                   (if (ly:grob? sg)
                       (let ((all (ly:grob-object sg 'all-elements)))
                         (if (ly:grob-array? all)
                             (for-each
                              (lambda (g)
                                (if (memq (assq-ref (ly:grob-property g 'meta) 'name)
                                          '(MetronomeMark TimeSignature KeySignature BarLine))
                                    (probe-grob-row n i g sg)))
                              (ly:grob-array->list all)))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEV BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

\book {
  \probeTag "TMM"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { \time 2/4 c''4 c'' |
                        \key d \major \time 4/4 \tempo 4 = 120 c''4 c'' c'' c'' |
                        c''1 \bar "|." } }
}

\book {
  \probeTag "TME"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4 c'' c'' c'' | c''4 c'' c'' c'' |
                        c''4 c'' c'' c'' | c''4 c'' c'' c'' |
                        \tempo "Molto più mosso, appassionato" c''4 c'' c'' c'' \break |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}
