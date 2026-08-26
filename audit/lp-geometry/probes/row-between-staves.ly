\version "2.26.0"
%% LP FIDELITY PROBE - an INDEPENDENT lyric row standing unfolded between two staves.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe row-between-staves.ly -Prefix PROBEIO
%%
%% THE SHAPE THIS MEASURES: input/regression/input-order-alignment.ly, the ONE book in
%% the tracked corpus whose lyric row does not fold into a staff (its track name matches
%% no part, so RenderSpecParser.FoldAdjacentRows leaves it a row of its own between the
%% two staves). It is the book the session-258 pair-placement port moves - the pair with
%% rows between it is placed at max(basic-distance, the alignment walk over the rows)
%% instead of at the rows' stacked bands - so this probe is what prices that move in
%% LilyPond's own numbers.
%%
%% MEASURED (2026-08-26, 2.26.0, fonts pinned), refpoint to refpoint:
%%   IOA  staff1 -> lyric1   5.653448349
%%        lyric1 -> staff2   3.587044154
%%        staff1 -> staff2   9.240492503  (= the sum EXACTLY - no slack, every spring
%%                                          at its floor; barely over basic-distance 9)
%%        staff2 -> lyric2   5.653448349  (= staff1 -> lyric1: the per-line invariant)
%%
%% ★★★ THE CLOSING STEP'S BINDING IS NOT THE LYRIC'S INK. 3.587044154 decomposes as
%% lyric descender 0.037044154 + padding 1.5 + staff line top 2.05 - but the walk that
%% produces the 9.240492503 room ALSO faces staff1's own down ink (raised into the run's
%% accumulation) against staff2's up ink, and staff2 carries a ^"Text" textscript
%% reaching 4.138464013 above its refpoint. Lily#'s Text stands ~0.35 higher (its
%% textscript box island), which is why its port reads the pair +0.083 over LilyPond:
%% the walk is right, the Text height is the open term, and the OLD pairwise spelling
%% (row ink against staff2 alone) could never see that collision at all - it matched
%% LilyPond here by structural blindness, not by fidelity.
%%
%% ⚠️ THE PITCHES: Lily# absolute is LilyPond minus one apostrophe (probe trap 5 - the
%% CHL family shipped a session with five notes an octave high). The .lys twin spells
%% <b c'> for LilyPond's <b' c''>.

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
                          (if (memq nm '(VerticalAxisGroup StaffSymbol))
                              (format #t "PROBEIO ~a ~a rel=~a ext=(~a . ~a) aff=~a\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))
                                      (ly:grob-property g 'staff-affinity 'none)))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))

\book {
  \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
           property-defaults.fonts.serif = "LilyPond Serif"
           property-defaults.fonts.sans = "LilyPond Sans Serif"
           page-post-process = #(lambda (layout pages)
                                  (format #t "\nPROBEIO BOOK IOA\n")
                                  (dump "IOA" layout pages)) }
  \score { <<
    \new Staff {
      <b' c''>2 s
      <b' c''>\f s
      <b' c''>^"Text" s
      <b' c''>-! s
    }
    \addlyrics { blah }
    \new Staff {
      <c'' b'>2 s
      <c'' b'>\f s
      <c'' b'>^"Text" s
      <c'' b'>-! s
    }
    \addlyrics { blah }
  >> } }
