\version "2.26.0"
%% LP FIDELITY PROBE — why JN's line COUNT disagrees, when its division does not.
%%
%% THE OBSERVATION THIS IS BUILT ON (Lily#'s break gate, dumped 2026-07-25). For JN's
%% sixteen bars at the default page (line width 102.429921, first line's available width
%% 95.844900 once the clef+meter prefix is taken off) the Knuth-Plass gate settles on:
%%
%%     k=3  split 5,5,6   demerits 1.12   (has a compressed line)
%%     k=4  split 4,4,4,4 demerits 0.98   <- chosen
%%
%% LilyPond takes THREE systems and cuts them 5,5,6 (line-start-mindist.ly's JN dump:
%% bar=1 n=30, bar=11 n=36). So Lily# BUILDS LilyPond's division and then rejects it on
%% total demerits. The rule that picks the count is already ported literally
%% (constrained-breaking.cc:224-260, best_solution's too_many_lines early return), so what
%% must differ is the FORCE of each line, which is a function of the line's natural length
%% and its springs' flexibility -- not of the choosing rule.
%%
%% WHAT THIS PROBE ASKS. A spring smob cannot be READ from Scheme (only the
%% ly:spring-set-inverse-*! setters exist -- see JZ in line-start-mindist.ly), so the
%% flexibility is not directly dumpable. The NATURAL length is, and it is the other half of
%% the force and the half that can be compared without any hook at all:
%%
%%   JNN  ragged-right, line-width 2000\mm  -- every bar on ONE line at force 0. The head
%%        positions ARE the natural configuration: the natural span of one bar, of five
%%        bars, and the natural quarter and eighth gaps of this music.
%%   JNJ  the real default page, indent 0   -- the CONTROL, which must reproduce the JN
%%        dump (three systems, 30/30/36 heads). Its gaps are the same springs SOLVED, so
%%        JNJ minus JNN is what the force did.
%%
%% PREDICTION, written before the run (section 5.0). Lily#'s natural span of one JN bar is
%% 19.677800 (its gate's cumulative ideals: five bars 99.488900 less four bars 79.811100),
%% so Lily# needs 5 x 19.677800 = 98.389000 for five bars where only 95.844900 is available
%% -- it must COMPRESS a five-bar line (its solved force is -0.1111). LilyPond's own first
%% system is STRETCHED, not compressed: its quarter gap reads 3.765697 against the corpus's
%% exact natural 3.704200. Both cannot be true of the same natural width.
%%   So the prediction is that LilyPond's natural bar span is SMALLER than 19.677800, by
%% enough that five bars fit inside 95.844900 with room to stretch -- i.e. the line-count
%% disagreement is NOTE SPACING and not the breaker's arithmetic. If instead LilyPond's
%% natural bar comes out at 19.6778 too, the prediction is wrong and the defect really is in
%% the demerits, which is the more expensive place for it to be.
%%   The eighth gap is the suspect: note-to-note.quarter is an exact ledger point and this
%% music is two thirds EIGHTHS, which no point in the corpus measures at all.
%%
%% Dumps go to STDOUT, ONE RECORD PER LINE (a split record gets cut in half by LilyPond's
%% own diagnostics on stderr -- see the note in barline-spacing.ly).

\header { tagline = ##f }

#(define probe-done (make-hash-table))

#(define (nf x)
   (cond ((not (real? x)) "?")
         ((inf? x) (if (> x 0) "+inf" "-inf"))
         (else (format #f "~,6f" x))))

#(define (grobs-of col sym)
   (let ((ga (ly:grob-object col sym #f)))
     (if (ly:grob-array? ga) (ly:grob-array->list ga) '())))

%% One record per SYSTEM, keyed on the system so it dumps exactly once and the row says
%% which bar it opens on -- the dump order follows LilyPond's processing, not the page.
#(define ((dump-system tag) g)
   (if (not (hash-ref probe-done (cons tag (ly:grob-system g)) #f))
       (begin
         (hash-set! probe-done (cons tag (ly:grob-system g)) #t)
         (let* ((sys (ly:grob-system g))
                (cols (ly:grob-array->list (ly:grob-object sys 'columns)))
                (heads '()))
           (for-each
            (lambda (c)
              (if (grob::has-interface c 'musical-paper-column-interface)
                  (for-each
                   (lambda (e)
                     (if (grob::has-interface e 'note-head-interface)
                         (set! heads
                               (cons (ly:grob-relative-coordinate e sys X) heads))))
                   (grobs-of c 'elements))))
            cols)
           (let ((xs (reverse heads)))
             (format #t "\nPROBE ~a SYS bar=~a n=~a right=~a xs=~a\n"
                     tag
                     (let ((rl (ly:grob-property (car cols) 'rhythmic-location)))
                       (if (pair? rl) (car rl) "?"))
                     (length xs)
                     (nf (apply max (cons 0.0 (map (lambda (c)
                                                     (ly:grob-relative-coordinate c sys X))
                                                   cols))))
                     (string-join (map nf xs) " "))))))
   '())

jnbar = { c'4 c'8 c' c'4 c'8 c' | }

jnmusic = { \time 4/4
  \jnbar \jnbar \jnbar \jnbar \jnbar \jnbar \jnbar \jnbar
  \jnbar \jnbar \jnbar \jnbar \jnbar \jnbar \jnbar \jnbar
}

%% JNN — the NATURAL configuration. ragged-right puts every spring at force 0, and a line
%% wide enough for all sixteen bars keeps the breaker out of the measurement entirely.
\score { \new Staff \jnmusic \layout {
  indent = 0
  ragged-right = ##t
  line-width = 2000\mm
  \context { \Score \override NoteHead.after-line-breaking = #(dump-system "JNN") }
} }

%% JNJ — the CONTROL: JN's own page, so this row must reproduce line-start-mindist.ly's JN
%% (three systems, 30/30/36). If it does not, the two probes are not measuring one score and
%% nothing below it may be read.
\score { \new Staff \jnmusic \layout {
  indent = 0
  \context { \Score \override NoteHead.after-line-breaking = #(dump-system "JNJ") }
} }

%% TPL — how many SYSTEMS LilyPond's LINE breaker wants for probe TP's forty bars, with the
%% page taken out of the question (a page tall enough that paging can never bind).
%%
%%   WHY IT IS ASKED SEPARATELY. The ledger reads TP on 70-staff-space paper, where
%%   LilyPond prints 5 + 1 over two pages -- six systems. But that six is not necessarily
%%   the LINE breaker's own answer: Optimal_page_breaking::solve sweeps sys_count DOWNWARD
%%   from the line breaker's ideal and keeps the global argmin over PAGE demerits, so the
%%   page result CHOOSES the line breaking (optimal-page-breaking.cc:139-173, quoted on the
%%   ledger entry page.tight.systems-on-first-page). Lily# breaks lines once and pages
%%   afterwards, so if LilyPond's line breaker on its own wanted FIVE and the page breaker
%%   pushed it to six, the two engravers are not comparable here at all and page.tight.*
%%   cannot be read as a verdict on Lily#'s line breaker.
%%
%%   WHAT LILY# DOES (dumped 2026-07-25, with OverfullPenalty removed): it settles on FIVE
%%   systems, demerits 0.29 with a COMPRESSED line, against 0.45 for six uncompressed --
%%   and 8 bars on the first system. With the penalty present it takes six. The forty bars
%%   are plain `c4 d e f`, so the beam fix (4bf72e9e) does not touch this score.
%%
%%   PREDICTION, written before the run: LilyPond's line breaker takes SIX here. If it does,
%%   Lily# prices a compressed line too cheaply and the seven-point regression is its own
%%   defect. If LilyPond's line breaker takes FIVE, then the six in the ledger comes from
%%   the page breaker, and no line-breaker change can reach it -- the entry would be
%%   measuring the unported Optimal_page_breaking, not the breaker.
tpbar = { c'4 d' e' f' | }
tpmusic = { \time 4/4 \key c \major
  \tpbar \tpbar \tpbar \tpbar \tpbar \tpbar \tpbar \tpbar \tpbar \tpbar
  \tpbar \tpbar \tpbar \tpbar \tpbar \tpbar \tpbar \tpbar \tpbar \tpbar
  \tpbar \tpbar \tpbar \tpbar \tpbar \tpbar \tpbar \tpbar \tpbar \tpbar
  \tpbar \tpbar \tpbar \tpbar \tpbar \tpbar \tpbar \tpbar \tpbar \tpbar
}

\book {
  \paper { paper-height = 2000\mm }
  \score { \new Staff \tpmusic \layout {
    indent = 0
    \context { \Score \override NoteHead.after-line-breaking = #(dump-system "TPL") }
  } }
}

%% TPT — the SAME forty bars on the ledger's own tight paper (70 staff spaces =
%% 123.0109mm at the default 20pt staff, LpGeometryProbes.TightPaper). Read against TPL:
%% if the two dumps differ in SYSTEM COUNT, the page breaker re-broke the music, and the
%% ledger's page.tight.* entries are measuring Optimal_page_breaking rather than the line
%% breaker -- which Lily# does not have and, by the section 3 decision, is not getting.
\book {
  \paper { paper-height = 123.0109\mm }
  \score { \new Staff \tpmusic \layout {
    indent = 0
    \context { \Score \override NoteHead.after-line-breaking = #(dump-system "TPT") }
  } }
}

%% SSN — the music of the inter-system SLUR pair (books SSD/SSU, page-vertical.ly:654-664)
%% with LilyPond breaking it FREELY, as the Lily# twin does. Those books force an even split
%% with \break; their Lily# sources (LpGeometryProbes.SSD/SSU) carry no break at all and let
%% the breaker choose, so the pair only ever agreed while Lily#'s breaker happened to land on
%% LilyPond's forced division. With OverfullPenalty removed Lily# now takes THREE systems
%% (first line 5 bars, compressed; demerits 0.17 against 0.73 for four uncompressed).
%% Pitches follow the octave convention: Lily# `b`/`g,,` are LilyPond `b'`/`g,`.
%% PREDICTION: LilyPond also takes three here. If so, the four inter-system points are
%% mis-specified pairs rather than a Lily# line-breaking defect.
ssbar = { b'1 g,1( g,1) | }
\book {
  \paper { paper-height = 2000\mm }
  \score { \new Staff { \time 12/4 \key c \major
    \ssbar \ssbar \ssbar \ssbar \ssbar \ssbar \ssbar \ssbar
    \ssbar \ssbar \ssbar \ssbar \ssbar \ssbar \ssbar \ssbar
  } \layout {
    indent = 0
    \context { \Score \override NoteHead.after-line-breaking = #(dump-system "SSN") }
  } }
}

%% TPD — book T EXACTLY as page-vertical.ly writes it: tight paper and NO indent override,
%% i.e. LilyPond's default 15mm on the first system. Book T (page-vertical.ly:204-208) is
%% the only page book in that file that does not say `indent = 0` — the four slur/tie books
%% at :654-719 all do — while its Lily# twin renders at LayoutOptions.Default, whose indent
%% is 0. Read against TPT: a difference here is an INDENT difference between the two sides
%% of the pair, not a paging one.
\book {
  \paper { paper-height = 123.0109\mm }
  \score { \new Staff \tpmusic \layout {
    \context { \Score \override NoteHead.after-line-breaking = #(dump-system "TPD") }
  } }
}
