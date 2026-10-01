\version "2.26.0"
%% LP FIDELITY PROBE — WHERE A JUMP SCRIPT STANDS ACROSS ITS BAR LINE. jump-mark-em.ly priced
%% the string's width and size; this file prices its X.
%%
%% Run: lilypond -dbackend=null jump-mark-x.ly (cmd /d /s /c "… < NUL", RULES §5.5).
%%
%% LILYPOND-REF: scm/define-grobs.scm:1898-1926 JumpScript — (break-align-symbols . (staff-bar
%%   key-signature clef)), (self-alignment-X . RIGHT), (X-offset .
%%   self-alignment-interface::self-aligned-on-breakable). Mid-line the staff-bar is visible
%%   and wins the list, so the grob's RIGHT edge should land on the bar line's
%%   break-align-anchor (the centre of its strokes, scm/bar-line.scm calc-anchor).
%%
%% THE BOOKS — the jump at a mid-line plain bar line, music after it, two string lengths so a
%% right alignment (both read 0) cannot be mistaken for a centring (they would differ by half
%% the difference of the widths):
%%   JXJ  \jump "D.S. al Coda"   (12.906170 ss wide, jump-mark-em.ly JMJ)
%%   JXC  \jump "D.C."
%%
%% PREDICTION (written before the run): right − bar anchor = 0.000000 in both books.
%%
%% MEASURED 2026-10-01 (session 733) — see the PROBEV rows; the ledger entries
%% mark.jump.right-from-bar-anchor.* carry the numbers.
%%
%% NOTE: inside #(...) the comment character is `;`, not `%%`.

#(define (probe-grob-row n i g)
   (let* ((nm (assq-ref (ly:grob-property g 'meta) 'name))
          (sys (ly:grob-system g))
          (l (+ (ly:grob-relative-coordinate g sys X)
                (car (ly:grob-extent g g X))))
          (r (+ (ly:grob-relative-coordinate g sys X)
                (cdr (ly:grob-extent g g X)))))
     (format #t "PROBEV GROB ~a ~a name=~a x=(~a . ~a)\n" n i nm l r)))

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
                                          '(JumpScript BarLine))
                                    (probe-grob-row n i g)))
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
  \probeTag "JXJ"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4 c'' c'' c'' \jump "D.S. al Coda" |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}

\book {
  \probeTag "JXC"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { \new Staff { c''4 c'' c'' c'' | c''4 c'' c'' c'' \jump "D.C." |
                        c''4 c'' c'' c'' | c''1 \bar "|." } }
}
