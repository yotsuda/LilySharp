\version "2.26.0"

%% ⚠️⚠️ THIS IS NOT THE SAME MUSIC AS ties-slurs-breaks.ly, AND ITS WIDTH IS NOT THAT
%% SCORE'S NATURAL WIDTH. Discovered 2026-07-25. The music below has NO `\bar "|."`, which
%% TSJ carries, and the final bar line is worth exactly 0.900000: this score's 101.907014 is
%% TSJ's real natural width 102.807014 minus 0.900000. Worse, with the final bar line in place
%% LilyPond will not keep the eight bars on one ragged line at all -- it takes two systems --
%% so the per-column decomposition below CANNOT be subtracted from TSJ's justified columns,
%% and the conclusion drawn from it ("the natural widths agree to 2e-5 over all eight bars")
%% was wrong and is retracted in lp-geometry.json.
%% ⇒ USE probes/compressed-line-force.ly INSTEAD. Its CLW engraves TSJ's music, final bar
%% line and all, ragged on a line wide enough not to break, which is the natural width; and
%% CLW - CLJ is then |force| * inverse_compress_strength per spring.
%% What this file is still good for: the LINE-COUNT question in the paragraph below, and the
%% per-column dump of THIS music (which is the fixture's music minus its final bar line).
%%
%% Control for ties-slurs-breaks.ly: the SAME music ragged-right, so the system's
%% natural (force 0) width can be read off directly. If it exceeds the justified
%% line width of 102.3799 ss, LilyPond COMPRESSED that line -- i.e. LilyPond accepts
%% a compressed line where Lily#'s OverfullPenalty makes one prohibitively expensive.
%%
%% AND, since 2026-07-25, the DECOMPOSITION of that natural width. Wiring the
%% line-start spring into the break gate splits this fixture into two systems where
%% LilyPond takes one, and the reason is not the line start: Lily#'s own natural width
%% for these eight bars is WIDER than LilyPond's, so a correctly priced line start
%% pushes an already-too-wide line over. The excess has to be localised before the gate
%% correction can go in, and a total says only that it exists.
%%
%% So this dumps ONE RECORD PER MUSICAL COLUMN -- its X in the system and what is in it
%% -- which is directly comparable against RenderedGeometry's glyph anchors on the Lily#
%% side (both are anchors, both are the paper column's origin; COORDINATE_AUDIT.md 2.4).
%% Consecutive differences are then the per-column spacing, and whichever difference
%% carries the excess names the regime that needs a ledger pair.
%%
%% The music is the LilyPond twin of LilySharp.Tests/Fixtures/test/ties-slurs.lys.
%% What it contains that the corpus has never measured: HALF notes (the ledger has only
%% note-to-note.quarter and .eighth) and RESTS in mid-measure. The score's shortest
%% duration is a QUARTER, unlike every other probe here, so it also exercises
%% get_duration_space at a different global_shortest.

\paper {
  indent = 0
  ragged-right = ##t
}

#(define probe-done (make-hash-table))

#(define (nf x)
   (cond ((not (real? x)) "?")
         ((inf? x) (if (> x 0) "+inf" "-inf"))
         (else (format #f "~,6f" x))))

#(define (grobs-of col sym)
   (let ((ga (ly:grob-object col sym #f)))
     (if (ly:grob-array? ga) (ly:grob-array->list ga) '())))

#(define ((dump-columns tag) g)
   (if (not (hash-ref probe-done tag #f))
       (begin
         (hash-set! probe-done tag #t)
         (let* ((sys (ly:grob-system g))
                (cols (ly:grob-array->list (ly:grob-object sys 'columns)))
                (i 0))
           ;; The line's total reach, so a change in the sum can be told apart from a
           ;; change in where the columns sit inside it.
           (format #t "\nPROBE ~a WIDTH x=~a ncols=~a\n"
                   tag
                   (nf (apply max (cons 0.0 (map (lambda (c)
                                                   (ly:grob-relative-coordinate c sys X))
                                                 cols))))
                   (length cols))
           (for-each
            (lambda (c)
              (let* ((musical? (grob::has-interface
                                c 'musical-paper-column-interface))
                     (rl (ly:grob-property c 'rhythmic-location))
                     ;; grob::name reads 'meta and can hand back #f; a dump that dies
                     ;; halfway looks like a short column list rather than an error.
                     (names (string-join
                             (map (lambda (e)
                                    (let ((n (grob::name e)))
                                      (if (symbol? n) (symbol->string n) "?")))
                                  (grobs-of c 'elements))
                             ",")))
                (format #t "\nPROBE ~a COL i=~a musical=~a x=~a bar=~a mom=~a names=~a\n"
                        tag i (if musical? 1 0)
                        (nf (ly:grob-relative-coordinate c sys X))
                        (if (pair? rl) (car rl) "?")
                        (if (pair? rl) (format #f "~a" (cdr rl)) "?")
                        (if (string-null? names) "-" names))
                ;; Where the BAR LINE's ink sits INSIDE its column. Interior columns put
                ;; it at the origin; the LAST one is break-aligned by its right edge, and
                ;; that difference is the whole of the line-end disagreement.
                (for-each
                 (lambda (e)
                   (if (grob::has-interface e 'bar-line-interface)
                       (let ((xe (ly:grob-extent e c X)))
                         (format #t "\nPROBE ~a BAR i=~a ext=~a..~a glyph=~a bdir=~a\n"
                                 tag i (nf (car xe)) (nf (cdr xe))
                                 (ly:grob-property e 'glyph-name)
                                 (ly:item-break-dir e)))))
                 (grobs-of c 'elements))
                (set! i (1+ i))))
            cols)))
   '()))

\header { tagline = ##f }

\score {
  \new Staff \with {
    \override NoteHead.after-line-breaking = #(dump-columns "TSR")
  } {
    \time 4/4
    \key c \major
    c'4~ c'4 d'2 |
    d'2 e'2~ | e'4 f' g' a' |
    c'4( d' e' f') |
    g'4( f' e' d') | c'2 r2 |
    c'4~ c'4 r2 |
    b'4~ b'4 r2 |
  }
}
