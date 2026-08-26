\version "2.26.0"
%% LP FIDELITY PROBE - a two-verse lyric row between two staves whose verses have
%% HOLES: verse 1 is silent for a bar where the upper staff's ink is deep, and
%% verse 2 is silent for the bar where verse 1 carries descenders and the lower
%% staff's ink is tall. The one book on which "the row is ONE band element" and
%% "the row's verses are separate elements of the walk" stop agreeing.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe row-verse-hole.ly -Prefix PROBERH
%%
%% WHY THIS SHAPE (HANDOFF §1 session 261, seam (1) of the run unification).
%% Lily#'s pair walk sizes the room between two staves with the row composed as one
%% band - verse 2 rigidly 2.8 below verse 1 (RowSkylinesAboutBaseline) - while the
%% reservation and the solve step the verses live against the accumulated skyline.
%% On every ledger book so far the two spellings agree, because every verse covers
%% every bar: whatever pushes verse 2 pushes verse 1's X too, and the composed
%% drop equals the walked drop. The agreement is a REGIME, not an invariant, and
%% folding the pair walk onto verse granularity is a behaviour change - so the
%% regime's edge needs an LP number BEFORE the fold (HANDOFF 5.2.1 (2)).
%%
%% THE EDGE, by construction (4/4, one system, ragged-right; every quantity is a
%% refpoint-to-refpoint Y):
%%   staff 1:  g'4 a' g' a' | c,4 c, c, c, | g'4 a' g' a'      | g'4 a' g' a' |
%%   verse 1:  no  no no no | (silent)     | gjy gjy gjy gjy   | no no no no  |
%%   verse 2:  no  no no no | no no no no  | (silent)          | no no no no  |
%%   staff 2:  g'4 a' g' a' | g'4 a' g' a' | f'''4 f''' f''' f''' | g'4 a' g' a' |
%% - Bar 2's deep ink (c,) faces verse 2 THROUGH verse 1's hole: a live walk
%%   pushes verse 2 past it and leaves verse 1 at its own floor; a band pushes
%%   BOTH, verse 1 dragged 2.8 above verse 2 wherever verse 2 must go.
%% - Bar 3 is where that difference becomes the ROOM: verse 1's descenders are
%%   the only lyric ink at that X, against the lower staff's tall f''' column.
%%   Dragged down (band), the descenders bind the closing there; at their own
%%   floor (live), bar 2's verse-2 dip binds instead, lower.
%% - RVH2 is the control: verse 1 sings bar 2 too, so verse 1 faces the deep ink
%%   ITSELF, the composed drop equals the walked drop again, and the two
%%   spellings must agree. Any pair difference between RVH1 and RVH2 is the
%%   granularity seam and nothing else.
%%
%% PREDICTIONS, written before running (HANDOFF 5.0-2), in LilyPond's terms:
%%   (a) RVH1 verse-step > 2.800000 - verse 2 is pushed through the hole while
%%       verse 1 is not, so the solved step leaves the rigid minimum. This is the
%%       probe's own sanity check: a step of 2.800000 here means the hole did not
%%       bind and the book measures nothing (the next variable would be a deeper
%%       bar-2 register, not a different arrangement).
%%   (b) RVH1 staff1 -> verse 1 stays at verse 1's own floor - the deep bar does
%%       not reach it (no verse-1 ink stands at that X).
%%   (c) RVH2 verse-step = 2.800000 exactly - both verses face the same
%%       constraint, the spring is rigid (engraver-init.ly:653-657,
%%       spring.cc:205-210), and nothing separates them.
%%   (d) RVH2 staff1 -> verse 1 > RVH1's - verse 1 itself clears the deep bar.
%%   (falsifier for the BOOK: (a) failing. falsifier for the CONTROL: (c) failing,
%%    which would mean the control's own regime is not the one every ledger book
%%    is in, and the pair could not isolate the seam.)
%%
%% ⚠️ THE PITCHES: Lily# absolute is LilyPond minus one apostrophe (probe trap 5).
%% The .lys twin spells g a / c,, / f'' for LilyPond's g' a' / c, / f'''.
%%
%% ★ A THIRD LEDGER BOOK RIDES ON RVH1's NUMBERS WITHOUT A NEW RUN: RVH3
%% (lyrics.row.between-staves.verse-hole.one-staff.*) is the SAME music with the
%% verses spelled as one Lily# track written twice, which stacks them into a single
%% row staff. LilyPond has one model of the music and cannot see that spelling, so
%% RVH1's dump is RVH3's LilyPond side verbatim -- and whatever Lily# reads
%% differently between the two books is its own granularity, isolated.

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
                              (format #t "PROBERH ~a ~a rel=~a ext=(~a . ~a) aff=~a\n"
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
                                  (format #t "\nPROBERH BOOK RVH1\n")
                                  (dump "RVH1" layout pages)) }
  \score { <<
    \new Staff {
      g'4 a' g' a' |
      c,4 c, c, c, |
      g'4 a' g' a' |
      g'4 a' g' a' |
    }
    \new Lyrics \lyricmode {
      no4 no no no
      \skip 4 \skip 4 \skip 4 \skip 4
      gjy4 gjy gjy gjy
      no4 no no no
    }
    \new Lyrics \lyricmode {
      no4 no no no
      no4 no no no
      \skip 4 \skip 4 \skip 4 \skip 4
      no4 no no no
    }
    \new Staff {
      g'4 a' g' a' |
      g'4 a' g' a' |
      f'''4 f''' f''' f''' |
      g'4 a' g' a' |
    }
  >> } }

\book {
  \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
           property-defaults.fonts.serif = "LilyPond Serif"
           property-defaults.fonts.sans = "LilyPond Sans Serif"
           page-post-process = #(lambda (layout pages)
                                  (format #t "\nPROBERH BOOK RVH2\n")
                                  (dump "RVH2" layout pages)) }
  \score { <<
    \new Staff {
      g'4 a' g' a' |
      c,4 c, c, c, |
      g'4 a' g' a' |
      g'4 a' g' a' |
    }
    \new Lyrics \lyricmode {
      no4 no no no
      no4 no no no
      gjy4 gjy gjy gjy
      no4 no no no
    }
    \new Lyrics \lyricmode {
      no4 no no no
      no4 no no no
      \skip 4 \skip 4 \skip 4 \skip 4
      no4 no no no
    }
    \new Staff {
      g'4 a' g' a' |
      g'4 a' g' a' |
      f'''4 f''' f''' f''' |
      g'4 a' g' a' |
    }
  >> } }
