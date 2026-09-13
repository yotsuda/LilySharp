\version "2.26.0"
%% LP FIDELITY PROBE — the LEDGER-LINE spacing rod between two ledgered columns.
%%
%% WHY THIS PROBE EXISTS (session 379). LilyPond raises a rod between consecutive columns whose
%% heads carry ledger lines on the same side of the staff:
%%   lily/ledger-line-spanner.cc:39-61 set_rods —
%%     distance = 2 * min_length - previous_extents[d][LEFT] + current_extents[d][RIGHT]
%%   with min_length = head width x minimum-length-fraction (0.25, scm/define-grobs.scm:2069).
%% For two black heads that is 2 x 1.3042 x 0.25 + 1.3042 = 1.9563. It binds only where the
%% spring would otherwise set the heads closer: a short note in get_duration_space's linear
%% branch (here 32nds under a common shortest of an eighth), whose ideal 1.4042 and skyline
%% minimum 1.5042 + merge_springs' 0.3 = 1.80 both fall under it. Lily# had no such rod and
%% drew 1.80 (scratch/p380/incr/cols.ps1 found it on floor2.lys: three ledgered 32nd gaps at
%% 1.9563 in LilyPond, 1.80 in Lily#, constant over increments 1.0-1.5).
%%
%% TWO SCORES, ONE PITCH EACH, so a row is one column chain with no pitch-dependent term:
%%   LR  a''  — staff position 6, ONE ledger line above the staff: the rod binds.
%%   LN  g''  — staff position 5, NO ledger line (a space above the top line): the CONTROL,
%%              whose 32nd gaps must stay at the skyline minimum + headroom, 1.80.
%% Each is three bars, the middle one holding the 32nds, so the per-bar shortest is an eighth,
%% a 32nd and an eighth and the common shortest (their mode) is an eighth. Lily# twins:
%% LpGeometryProbes.cs, scores LR and LN (Lily# octave absolute `a'` = LilyPond `a''`).
%%
%% READING: heads 12..15 are the 32nds of bar 2 (bar 1 holds 0..7, bar 2 opens with four eighths
%% 8..11). The ledger points read gap 13 -> 14, between two 32nds with a 32nd on each side.
%%
%% Dumps go to STDOUT, ONE RECORD PER LINE, once per system:
%%   PROBE <tag> ROW n=<heads> xs=<head x on the system> gaps=<successive differences>

\header { tagline = ##f }

#(define probe-done (make-hash-table))

#(define (nf x)
   (cond ((not (real? x)) "?")
         (else (format #f "~,6f" x))))

#(define ((dump-heads tag) g)
   (let ((sys (ly:grob-system g)))
     (if (not (hash-ref probe-done (cons tag sys) #f))
         (begin
           (hash-set! probe-done (cons tag sys) #t)
           (let* ((cols (ly:grob-array->list (ly:grob-object sys 'columns)))
                  (heads '()))
             (for-each
              (lambda (c)
                (if (grob::has-interface c 'musical-paper-column-interface)
                    (let ((ga (ly:grob-object c 'elements #f)))
                      (if (ly:grob-array? ga)
                          (for-each
                           (lambda (e)
                             (if (grob::has-interface e 'note-head-interface)
                                 (set! heads (cons (ly:grob-relative-coordinate e sys X) heads))))
                           (ly:grob-array->list ga))))))
              cols)
             (let* ((xs (reverse heads))
                    (gaps (if (< (length xs) 2) '()
                              (map - (cdr xs) (reverse (cdr (reverse xs)))))))
               (format #t "\nPROBE ~a ROW n=~a xs=~a gaps=~a\n"
                       tag (length xs)
                       (string-join (map nf xs) " ")
                       (string-join (map nf gaps) " ")))))))
   '())

lrlay =
#(define-scheme-function (tag) (string?)
   #{ \layout {
        indent = 0
        ragged-right = ##t
        \context { \Score \override NoteHead.after-line-breaking = #(dump-heads tag) }
      } #})

\score {
  \new Staff { \time 4/4
    a''8 a'' a'' a'' a'' a'' a'' a'' |
    a''8 a'' a'' a'' a''32 a'' a'' a'' a''8 a'' a'' |
    a''8 a'' a'' a'' a'' a'' a'' a'' \bar "|." }
  \lrlay "LR"
}

\score {
  \new Staff { \time 4/4
    g''8 g'' g'' g'' g'' g'' g'' g'' |
    g''8 g'' g'' g'' g''32 g'' g'' g'' g''8 g'' g'' |
    g''8 g'' g'' g'' g'' g'' g'' g'' \bar "|." }
  \lrlay "LN"
}
