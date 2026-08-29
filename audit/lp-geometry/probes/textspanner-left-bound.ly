\version "2.26.0"
%% LP FIDELITY PROBE — WHERE DOES A TEXT SPANNER'S LEFT BOUND SIT WHEN THE START IS
%% NOT ON THE MEASURE'S FIRST NOTE?
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe textspanner-left-bound.ly -Prefix PROBETX
%%
%% WHAT THIS OPENS (2026-08-29, session 290, from session 289's ▶). Lily#'s
%% TextSpannerEngraver.PairTextSpanners passes `StartItemIndex: 0` AS A CONSTANT, so the
%% dashed line and its "rit." start at the MEASURE'S FIRST ITEM however late in the
%% measure the mark was written. Session 289 gave the span a terminator (`@!rit`), and
%% the terminator DID take its item index (`EndItemIndex: Math.Max(mark.AnchorItemIndex,
%% 0)`) — so the two ends of one span are now spelled differently, and the left one is
%% the one that is not the writer's.
%%
%% ⚠️ THE ASYMMETRY IS INSIDE ONE ENGINE, NOT BETWEEN TWO ENGINES. The ottava — the
%% other span this language draws with a left bound — already reads the mark's own
%% column (`OttavaBracketEngraver.BracketFrom`: `StartItemIndex: start.Mark.
%% AnchorItemIndex`), and session 289 confirmed it. So this is the "one quantity, two
%% spellings" shape (RULES §5.2.1②) and not a porting question.
%%
%% ⚠️ NOTHING IN THE LEDGER READS A TEXT SPANNER'S X. The four `textspanner.*` points
%% are all HEIGHTS (floor.staff-to-line, support.staff-to-line and the two row pairs),
%% so the defect above cannot be seen by any observer this corpus owns. That is what
%% these two points are for, and it is why they are placed BEFORE the repair.
%%
%% THE PAIR, and it is a pair in the sense §0's README means — the SAME quantity read in
%% two books that differ in ONE thing:
%%   TXO — the spanner opens on the measure's THIRD note (`c c c\startTextSpan c`).
%%   TXH — THE CONTROL: the same book with the spanner opening on the FIRST note and
%%         nothing else changed.
%% The quantity is THE LABEL'S PEN MINUS THE ANCHOR OF THE NOTEHEAD THE SPAN OPENED ON
%% (head #2 in TXO, head #0 in TXH). If LilyPond binds the span to the note it was
%% written on, the two books return THE SAME NUMBER; the pair is therefore its own
%% falsifier, and the reading needs no arithmetic to interpret.
%%
%% ★ LilyPond's answer is not in doubt as a MECHANISM — lily/text-spanner-engraver.cc:
%% 108-115 `Text_spanner_engraver::stop_translation_timestep` sets the LEFT bound to the
%% `currentMusicalColumn` of the timestep the START was seen in, and
%% lily/line-spanner.cc:149-176 `Line_spanner::calc_bound_info` takes that column's
%% `generic_bound_extent` and reads it at `attach-dir` (LEFT for TextSpanner,
%% scm/define-grobs.scm TextSpanner bound-details.left) — but the NUMBER (the
%% `padding . 0.25` of that same alist, and whether it lands on the column's left edge
%% or the head's) has never been measured here, and Lily# spends NO padding at all
%% (`startX = measureLayouts[..].X + startItem.X`). The pair reads the number.
%%
%% PREDICTIONS, written before running (RULES §5.0), signs asserted:
%%   (a) TXO == TXH to every printed digit. This is the CLAIM. Falsifier: a difference
%%       of about one column step (~2.6 ss in this book) would mean LilyPond ALSO pins
%%       the left edge to something measure-wide, and then Lily#'s constant 0 is not a
%%       defect and this whole ▶ dissolves.
%%   (b) Both are SMALL AND POSITIVE — the label's pen sits a little RIGHT of the head's
%%       anchor, because attach-dir LEFT reads the bound COLUMN's left edge (which for a
%%       stemmed up-stem column is the head's own ink left = the head anchor) and then
%%       `padding . 0.25` moves the text right. Expected ≈ +0.25. Falsifier for the
%%       reading (not for (a)): a NEGATIVE number would mean the column's extent reaches
%%       left of the head anchor — the accidental/dot side of `calc_bound_info` — and
%%       then the point must name the column, not the head.
%%   ⚠️ (a) and (b) are independent: (a) can hold with (b) false, and (a) is the one the
%%   repair turns on.
%%
%% ⚠️ THE SERIF PIN IS LOAD-BEARING, exactly as in spanner-floors.ly and
%% textspanner-under-row.ly: without it the svg backend resolves fonts.serif through this
%% machine's fontconfig and the "rit." run's pen stops being reproducible.
%%
%% ⚠️ THE MUSIC IS QUIET ON PURPOSE (drawn third-space c'' throughout, no accidentals, no
%% dots): every extra grob in the bound column is a term `calc_bound_info` can pick up
%% (`end-on-accidental`, `start-at-dot`), and this pair is about WHICH COLUMN, not about
%% what is in it.
%%
%% Everything printed is in STAFF SPACES.

#(define (dump tag layout pages)
   (for-each
    (lambda (page)
      (for-each
       (lambda (sys)
         (let ((sg (ly:prob-property sys 'system-grob)))
           (if (ly:grob? sg)
               (let ((all (ly:grob-object sg 'all-elements)))
                 (if (ly:grob-array? all)
                     (for-each
                      (lambda (g)
                        (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                          (if (memq nm '(NoteHead NoteColumn TextSpanner))
                              (format #t "PROBETX ~a ~a relx=~a x=(~a . ~a)\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg X)
                                      (+ (ly:grob-relative-coordinate g sg X)
                                         (car (ly:grob-extent g g X)))
                                      (+ (ly:grob-relative-coordinate g sg X)
                                         (cdr (ly:grob-extent g g X)))))
                          ;; The bound alist itself, so the reading can be told apart
                          ;; from the arithmetic that produced it: X is what
                          ;; calc_bound_info resolved, padding is what print then spends.
                          (if (eq? nm 'TextSpanner)
                              (let ((lbi (ly:grob-property g 'left-bound-info))
                                    (rbi (ly:grob-property g 'right-bound-info)))
                                (format #t "PROBETX ~a BOUND leftX=~a leftPad=~a rightX=~a rightPad=~a\n"
                                        tag
                                        (assq-ref lbi 'X)
                                        (assq-ref lbi 'padding)
                                        (assq-ref rbi 'X)
                                        (assq-ref rbi 'padding))))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))

probeTX =
#(define-scheme-function (tag) (string?)
   #{ \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
               property-defaults.fonts.serif = "LilyPond Serif"
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBETX BOOK ~a\n" tag)
                                      (dump tag layout pages)) } #})

%% TXO — the spanner opens on the measure's THIRD note.
\book {
  \probeTX "TXO"
  \score {
    \new Staff {
      \override TextSpanner.bound-details.left.text = \markup \italic "rit."
      c''4 c'' c''\startTextSpan c'' | c''4 c'' c'' c''\stopTextSpan \bar "|."
    }
  }
}

%% TXH — THE CONTROL: the same book opening on the FIRST note, nothing else changed.
\book {
  \probeTX "TXH"
  \score {
    \new Staff {
      \override TextSpanner.bound-details.left.text = \markup \italic "rit."
      c''4\startTextSpan c'' c'' c'' | c''4 c'' c'' c''\stopTextSpan \bar "|."
    }
  }
}
