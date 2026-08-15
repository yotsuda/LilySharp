%% What did \partCombine actually engrave? One record per grob, one line each
%% (§5.3: split records over lines and LilyPond's own diagnostics interleave).
%% NullVoice (the "null" context \partCombine routes a dropped part to) still MAKES
%% grobs, so every record says whether it carries ink.
%%
%% ⚠️ "ink" only says the 'stencil PROPERTY DATA is not #f — that is exactly what
%% NullVoice clears, which is what this flag was built to see. It is NOT a claim that
%% the grob draws something: Stem keeps its stencil callback even when the callback
%% returns empty (all-rest scores still print STEM records).
%%
%% y= is the grob's own Y-offset, i.e. staff spaces from the staff centre line (the
%% same frame Lily# SVG is read in). Rest carries no usable 'staff-position at
%% after-line-breaking (it reads back as '()), so y= is the only position a silence
%% book can be compared on.
#(define (ink g)
   (if (ly:grob-property-data g 'stencil) "ink" "NOINK"))
%% ⚠️ DO NOT read 'Y-offset for a Rest. Rest_collision chains force_shift_callback_rest
%% onto the rest's Y offset (rest-collision.cc:46-66 / :71-85); that callback APPLIES the
%% incoming offset with translate_axis and then RETURNS 0.0, so every rest that shares a
%% moment with another rest reports Y-offset = 0 no matter where it is drawn. Measured:
%% pcsil-ctl.ly, plain \voiceOne/\voiceTwo, reported 0.0 for the partnered voice-one rests
%% and 2.0 for the one lone rest — and 0.0 is not where the partnered ones are drawn.
%% Every position below is therefore system-relative and turned into staff spaces above
%% the centre line by subtracting the STAFF record's sy.
#(define (yof g)
   (ly:grob-relative-coordinate g (ly:grob-system g) Y))
%% src= is the line:column the grob's causing event was WRITTEN at. For \partCombine
%% this is the only way to tell which of the two parts a printed grob came from —
%% the combiner rewrites the voices, so the printed voice number says nothing about
%% the source part.
#(define (src-of g)
   (let ((c (event-cause g)))
     (if c (let ((o (ly:event-property c 'origin #f)))
             ;; ⚠️ ly:input-file-line-char-column returns ONE list (file line char column),
             ;; not four values — call-with-values on it dies with "wrong number of
             ;; arguments", and LilyPond reports that as a Guile error in init.ly.
             (if o (let ((v (ly:input-file-line-char-column o)))
                     (format #f "~a:~a" (list-ref v 1) (list-ref v 3)))
                 "-"))
         "-")))
%% The Note_spacing WISHES themselves. note-spacing.cc:77 (the left-head refinement) runs
%% for a column pair only when some wish on the LEFT column names the RIGHT column in its
%% 'right-items (spacing-spanner.cc:350-378), and the chain that fills 'right-items is
%% `last_spacing_`, an instance member of Note_spacing_engraver — a VOICE engraver
%% (engraver-init.ly:419), so the chain is per Voice CONTEXT and survives the stretches
%% where that context is silent. Printing both ends of every wish is the only way to see
%% which pairs of a \partCombine score are linked without guessing at the contexts.
#(define (wish-cols g sym)
   (let ((a (ly:grob-object g sym #f)))
     (if (ly:grob-array? a)
         (map (lambda (i)
                (ly:grob-relative-coordinate (ly:item-get-column i) (ly:grob-system g) X))
              (ly:grob-array->list a))
         '())))
#(define (pitch-of g)
   (let ((c (event-cause g)))
     (if c (let ((p (ly:event-property c 'pitch #f)))
             (if p (ly:pitch-notename p) "-"))
         "-")))

\layout {
  \context {
    \Score
    \override NoteHead.after-line-breaking =
      #(lambda (g) (format #t "NH pos=~a x=~a y=~a ~a note=~a src=~a\n"
                           (ly:grob-property g 'staff-position)
                           (ly:grob-relative-coordinate g (ly:grob-system g) X)
                           (yof g)
                           (ink g) (pitch-of g) (src-of g)))
    \override Stem.after-line-breaking =
      #(lambda (g) (format #t "STEM dir=~a x=~a ~a\n"
                           (ly:grob-property g 'direction)
                           (ly:grob-relative-coordinate g (ly:grob-system g) X)
                           (ink g)))
    \override Rest.after-line-breaking =
      %% dir= is the voice direction the Rest inherited. It names the CONTEXT the part
      %% combiner routed the part into without having to guess: "one" is \voiceOne (1),
      %% "two" is \voiceTwo (-1), and "shared"/"solo" carry no voice settings at all (0).
      %% headend= is note-spacing.cc's left_head_end for a column whose left item is this
      %% rest: g->extent (col, X_AXIS)[RIGHT], i.e. the rest's right edge measured from its
      %% OWN paper column. note-spacing.cc:51-56 takes the "rest" object in preference to
      %% the first note head, so a rest column carries this term just like a note column
      %% does — and ideal = base - increment + left_head_end (:77) is the only place the
      %% left column's width reaches the spring.
      %% yext= is the DRAWN INK, in the same system-relative frame as y=. It is here because
      %% y= alone cannot answer "is this symbol at the same height as that one": a rest glyph
      %% hangs from its reference position, and lily/multi-measure-rest.cc:284-292 picks a
      %% DIFFERENT glyph variant for a multi-measure rest than for the ordinary rest of the
      %% same duration. Two records with different y= can draw the same ink and vice versa.
      #(lambda (g) (format #t "REST x=~a y=~a yext=~a dur=~a dir=~a ~a src=~a headend=~a\n"
                           (ly:grob-relative-coordinate g (ly:grob-system g) X)
                           (yof g)
                           (ly:grob-extent g (ly:grob-system g) Y)
                           (ly:grob-property g 'duration-log)
                           (ly:grob-property g 'direction)
                           (ink g) (src-of g)
                           (cdr (ly:grob-extent g (ly:item-get-column g) X))))
    \override MultiMeasureRest.after-line-breaking =
      #(lambda (g) (format #t "MMREST x=~a y=~a yext=~a bars=~a ~a\n"
                           (ly:grob-relative-coordinate g (ly:grob-system g) X)
                           (yof g)
                           (ly:grob-extent g (ly:grob-system g) Y)
                           (ly:grob-property g 'measure-count)
                           (ink g)))
    %% The count printed ABOVE a multi-measure rest, so "did LilyPond draw a number here?"
    %% is a record and not a look at the picture. ⚠️ A MultiMeasureRest's own x is its LEFT
    %% BOUND (the bar line), not the centre of the symbol.
    \override MultiMeasureRestNumber.after-line-breaking =
      #(lambda (g) (format #t "MMNUM ~s x=~a\n"
                           (ly:grob-property g 'text)
                           (ly:grob-relative-coordinate g (ly:grob-system g) X)))
    %% ⚠️ A text written on a MULTI-MEASURE REST IS NOT A TextScript. R1^"R" makes a
    %% MultiMeasureRestText, a different grob with its own engraver, so a book that puts the
    %% same label on `R1` and on `r1` prints two records only if BOTH are dumped — with only
    %% the TextScript override in place, part-combine-silence-mixed showed every "s" and "r"
    %% and not one "R", which reads as "LilyPond dropped the label" and is false.
    \override MultiMeasureRestText.after-line-breaking =
      #(lambda (g) (format #t "MMTXT ~s x=~a y=~a dir=~a src=~a\n"
                           (ly:grob-property g 'text)
                           (ly:grob-relative-coordinate g (ly:grob-system g) X)
                           (yof g)
                           (ly:grob-property g 'direction)
                           (src-of g)))
    %% Bar lines, so a record can be ATTRIBUTED to a measure instead of guessed into one. A
    %% MultiMeasureRest's own x is its LEFT BOUND, which is a bar line, and reading that as
    %% "the measure the rest is in" without the bar lines to check against is how the first
    %% pass at this book put every multi-measure rest one measure late.
    \override BarLine.after-line-breaking =
      #(lambda (g) (format #t "BAR x=~a\n"
                           (ly:grob-relative-coordinate g (ly:grob-system g) X)))
    %% A PLAIN text script (r1^"r"), which is NOT a CombineTextScript: the combiner's own
    %% labels come from default-part-combine-mark-alist, while this one is written on the
    %% note. Both have to be visible or a book that carries both cannot be read.
    \override TextScript.after-line-breaking =
      #(lambda (g) (format #t "TXT ~s x=~a y=~a dir=~a src=~a\n"
                           (ly:grob-property g 'text)
                           (ly:grob-relative-coordinate g (ly:grob-system g) X)
                           (yof g)
                           (ly:grob-property g 'direction)
                           (src-of g)))
    %% A tuplet's bracket and its number. This book (part-combine-tuplet-end) asks whether
    %% a tuplet that ENDS where the combiner switches contexts still closes, so what has to
    %% be readable is HOW MANY brackets there are and WHERE each one starts and stops —
    %% hence the X extent (a spanner has no single x) rather than one coordinate.
    %% ⚠️ 'positions is in the bracket's own Y frame (staff spaces, centre line), while
    %% yof is system-relative like every other record here; both are printed so the pair
    %% can be checked against each other.
    \override TupletBracket.after-line-breaking =
      #(lambda (g) (format #t "TUPBR xext=~a positions=~a dir=~a ~a src=~a\n"
                           (ly:grob-extent g (ly:grob-system g) X)
                           (ly:grob-property g 'positions)
                           (ly:grob-property g 'direction)
                           (ink g) (src-of g)))
    \override TupletNumber.after-line-breaking =
      #(lambda (g) (format #t "TUPNUM ~s x=~a y=~a ~a\n"
                           (ly:grob-property g 'text)
                           (ly:grob-relative-coordinate g (ly:grob-system g) X)
                           (yof g) (ink g)))
    %% Manual beams are beam EVENTS and so enter the combiner's span state (see
    %% pcglobal-probe.lys). positions are the two ends on the staff's centre-line scale.
    \override Beam.after-line-breaking =
      #(lambda (g) (format #t "BEAM positions=~a xext=~a ~a\n"
                           (ly:grob-property g 'positions)
                           (ly:grob-extent g (ly:grob-system g) X)
                           (ink g)))
    \override NoteSpacing.after-line-breaking =
      #(lambda (g) (format #t "WISH left=~a right=~a\n"
                           (wish-cols g 'left-items)
                           (wish-cols g 'right-items)))
    \override StaffSymbol.after-line-breaking =
      #(lambda (g) (format #t "STAFF sy=~a\n"
                           (ly:grob-relative-coordinate g (ly:grob-system g) Y)))
    \override CombineTextScript.after-line-breaking =
      #(lambda (g) (format #t "TEXT ~s x=~a y=~a series=~a shape=~a size=~a\n"
                           (ly:grob-property g 'text)
                           (ly:grob-relative-coordinate g (ly:grob-system g) X)
                           (ly:grob-property g 'Y-offset)
                           (ly:grob-property g 'font-series)
                           (ly:grob-property g 'font-shape)
                           (ly:grob-property g 'font-size)))
  }
}
