\version "2.26.0"
%% LP FIDELITY PROBE — WHERE A 16TH REST SITS IN X, AND WHETHER ITS COLUMN IS A NOTE'S.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe rest-column-x.ly -Prefix PROBEX
%%
%% THE REPORT (session 238, scratch/…/r16.lys): "the r16's x looks like it could be a little
%% further right". The music is one 4/4 bar, `e8 e b'16 e,8 e16 r16 e e8 b' gis`, and the rest
%% is the sixth event — onset 8 of 16 sixteenths, i.e. exactly the middle of the bar.
%%
%% WHAT HAS TO BE SEPARATED, because the eye cannot do it: a rest that is drawn in the wrong
%% place INSIDE a correct column, and a column that is itself in the wrong place. The two look
%% identical on paper and have nothing to do with each other in the code — one is the Rest
%% grob's own X-offset, the other is the spacing engine. So this probe dumps BOTH: every
%% NoteHead's and Rest's reference X with its own X-extent, and the PaperColumn each one hangs
%% from.
%%
%% THE PAIR (HANDOFF 5.0-1): RXR and RXN are ONE VARIABLE apart — whether the sixth event is a
%% REST or a NOTE. Same durations, same pitches everywhere else, same bar, same paper. If the
%% column does not move between the books, the spacing engine does not care that the event is
%% a rest, and any Lily# divergence is in the glyph's offset; if it does move, the question is
%% a spacing one and the rest's own offset is a red herring.
%%
%% ⚠️ THE MUSIC IS LILY#'S OWN EXPORT, not a hand transcription: `lysc ly` was run on the
%% reported file and the result pasted here (only the \mark and the \score wrapper trimmed).
%% Hand-converting a relative-octave line with a `b'` and an `e,` in it is exactly where a
%% twin stops being a twin, and RULES 5.5 already carries that trap for octaves.
%%
%% ⚠️ indent = 0 and ragged-right = ##t: the bar must be spaced by its own durations, not
%% stretched to a line width, or both books read a justification artifact instead of the
%% spacing rule. ⚠️ THE TEMPO MARK AND THE REHEARSAL MARK ARE DROPPED on purpose — they are
%% above-staff grobs that change nothing horizontally here, and leaving them in would put two
%% more objects into the dump for no reading.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2), mechanism first:
%%  * P1 — THE COLUMN DOES NOT MOVE between RXR and RXN. LilyPond's spacing is a function of
%%    moments and durations (spacing-spanner.cc), and nothing in it asks whether the event at
%%    a moment is a rest. FALSIFIER: a column shift means rests ARE spaced differently, and
%%    then Lily#'s question is a spacing question rather than an offset one.
%%  * P2 — THE REST'S INK IS NOT CENTRED ON ITS COLUMN. A single-voice rest takes the column
%%    reference the way a notehead does, so its X-extent should start at or near 0 rather than
%%    straddling it. FALSIFIER: an extent like (-0.6 . 0.6) means LilyPond centres it and Lily#
%%    (which draws the glyph from its left edge, measured: notehead left 8.59, its stem 9.82,
%%    one notehead width apart) would be left-shifting every rest by half a glyph.
%%  * P3, THE NUMERIC ONE — Lily# reads the rest's ink left edge 2.510000 after the previous
%%    notehead's left edge and 2.400000 before the next one's, which is very nearly symmetric.
%%    I predict LilyPond agrees within 0.100000 and that THERE IS NOTHING TO FIX — the reported
%%    impression being the 16th rest's own glyph shape, whose ink hangs low and left inside its
%%    box, rather than a placement. FALSIFIER, AND THIS IS THE ONE THAT MATTERS: LilyPond
%%    putting the rest more than 0.150000 further right — a bigger gap before it than after —
%%    means the eye was right and Lily# has a real offset defect.
%%    ⚠️ REGIME (bone 5, this session): P3's falsifier is about the BAR-INTERNAL gaps either
%%    side of THIS rest at THIS onset. It says nothing about rests at a bar start, in a beamed
%%    group, or under a different shortest-duration regime, and must not be read as if it did.
%%  * Both books: 1 page, 1 system, 1 bar. A book that wraps is out of its regime.
%%
%% MEASURED — THE REPORT IS NOT A DEFECT. Lily# reproduces LilyPond's column X on ALL TEN
%% events of the bar, the rest included, to 3.6e-15 — floating-point noise, not a residual:
%%
%%   event    LilyPond RXR   LilyPond RXN   Lily# (origin-aligned)
%%   e8        8.585000       8.585000       8.585000
%%   e8       12.289200      12.289200      12.289200
%%   b'16     16.243400      16.243400      16.243400
%%   e,8      18.497600      18.497600      18.497600
%%   e16      22.201800      22.201800      22.201800
%%   r16      24.706000      24.706000      24.706000   <- the reported glyph
%%   e16      27.106000      27.210200      27.106000
%%   e8       29.610200      29.714400      29.610200
%%   b'8      33.564400      33.668600      33.564400
%%   gis8     37.268600      37.372800      37.268600
%%
%%  * P1 HALF RIGHT, AND THE HALF THAT IS WRONG IS THE INTERESTING ONE. The REST'S OWN column
%%    is at 24.706000 in both books, so the spacing engine indeed does not care that the event
%%    at that moment is a rest. But EVERY COLUMN AFTER IT MOVES, by exactly 0.104200 on all
%%    four — which is the notehead's width 1.304200 less the rest's 1.200000. So the engine is
%%    blind to the KIND of the event and not to its WIDTH: a narrower grob lets everything
%%    downstream slide left by the difference. "Durations decide the columns" was too coarse.
%%  * P2 CONFIRMED: the Rest's X-extent is (0.0 . 1.2) — it starts at its column reference the
%%    way a notehead does, and is NOT centred on it.
%%  * P3 CONFIRMED, and its falsifier did not fire: the two engines agree exactly, far inside
%%    the 0.100000 the prediction allowed and nowhere near the 0.150000 rightward shift that
%%    would have meant a defect.
%%
%% ⚠️ WHY IT CAN STILL LOOK LEFT, since the answer to the report is "nothing to change": the
%% WHITE GAPS either side of the rest are 1.200000 and 1.200000 — dead equal. Previous
%% notehead ink ends at 22.201800 + 1.304200 = 23.506000, rest ink runs 24.706000 to
%% 25.906000, next notehead ink starts at 27.106000. The rest is exactly centred between its
%% neighbours' INK, by construction, on both engines. What is not symmetric is the glyph
%% itself, and a 16th rest is the least symmetric one in the font.
%%
%% ⚠️ NO LEDGER ENTRY, deliberately. The quantity is exact, and reading it would need a
%% column-X helper RenderedGeometry does not have; a helper written to pin a zero is machinery
%% with no defect behind it. This header IS the record — the same standing 22 other probes in
%% this directory have. Add entries the day something here moves.
%%
%% NOTE: inside #(...) the comment character is `;`, not `%%`.

#(define (probe-dump-pages layout pages)
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps))
                (lines (ly:prob-property page 'lines)))
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
                                  (if (memq nm '(NoteHead Rest))
                                      (let* ((col (ly:grob-parent g X))
                                             (pc (if (ly:grob? col) (ly:grob-parent col X) #f)))
                                        (format #t
                                         "PROBEX ~a name=~a x=~a ext=(~a . ~a) col=~a pcol=~a\n"
                                         i nm
                                         (ly:grob-relative-coordinate g sg X)
                                         (car (ly:grob-extent g g X))
                                         (cdr (ly:grob-extent g g X))
                                         (if (ly:grob? col)
                                             (ly:grob-relative-coordinate col sg X) "-")
                                         (if (ly:grob? pc)
                                             (ly:grob-relative-coordinate pc sg X) "-"))))))
                              (ly:grob-array->list all)))))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEX BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

%% RXR — THE REPORTED BAR, exactly as `lysc ly` exported it.
\book {
  \probeTag "RXR"
  \paper { ragged-right = ##t  indent = 0 }
  \score {
    \new Staff { \clef "treble" \relative c' {
      \time 4/4 \key c \major
      e8 e b'16 e,8 e16 r16 e e8 b' gis } }
  }
}

%% RXN — THE SAME BAR WITH THE REST REPLACED BY A NOTE of the same duration. One variable
%% apart from RXR: the sixth event's kind, and nothing else. The pitch is the one the
%% following note already has, so no accidental or ledger appears that RXR does not have.
\book {
  \probeTag "RXN"
  \paper { ragged-right = ##t  indent = 0 }
  \score {
    \new Staff { \clef "treble" \relative c' {
      \time 4/4 \key c \major
      e8 e b'16 e,8 e16 e16 e e8 b' gis } }
  }
}
