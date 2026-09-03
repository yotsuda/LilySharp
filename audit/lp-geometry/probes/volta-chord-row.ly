\version "2.26.0"
%% LP FIDELITY PROBE — A VOLTA BRACKET, AND THE LABELS ON ITS ENDINGS, WHEN A CHORDNAMES
%% LINE LEADS THE SYSTEM.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe volta-chord-row.ly -Prefix PROBEV
%%
%% THE DEFECT THIS MEASURES (owner report, session 328, on `Lambada Complicada`): on a lead
%% sheet written `chords / staff`, Lily# drew the SECOND ending's section label UNDER the
%% volta bracket's line, level with the chord symbols, while the first ending's label rode
%% above the bracket. Bisected on scratch/p328/volta: v3 (no chord row) is right, v2b (the
%% same book with a chord row) is not, so the row is the variable.
%%
%% LILYPOND'S MECHANISM: VoltaBracketSpanner and RehearsalMark are Score-context grobs
%% (ly/engraver-init.ly:764-767, Mark_engraver and Volta_engraver in \Score). The spanner is
%% side-positioned against the staves it spans (lily/volta-engraver.cc:407,:497
%% Side_position_interface::add_support) — its FLOOR is the staff's ink + padding 1 (ledger
%% page.volta.no-ink.staff-to-line) — and then placed by the outside-staff pass
%% (lily/axis-group-interface.cc:648-676 avoid_outside_staff_collisions, priority 600)
%% against a support that INCLUDES the ChordNames line's symbols: a ChordName declares no
%% outside-staff-priority, so it is in inside_staff_skylines (:914-935). The marks (1500) are
%% then placed against the bracket. Nothing about the row's own band enters; only its ink.
%%
%% WHAT LILY# DID: the bracket's floor was measured from the SYSTEM'S TOP EDGE — the chord
%% row's band top when a row leads — so it stood the bracket one band too high, above every
%% symbol; and the row's symbols were deliberately kept OUT of the stacker's support ("a row's
%% symbols live in their own band"). The bracket never met a symbol, and the label's staff-
%% based estimate found a pocket under the hooks that LilyPond's geometry does not have.
%%
%% PREDICTION, written before the port: (1) the bracket line's BOTTOM edge sits above the
%% symbol under its NUMBER by outside-staff-padding + volta-number-offset + the number's ink:
%% 0.46 + 0.5 + 1.2598 - 0.08 (line bottom vs centre) = 2.1398; (2) each ending's label box
%% bottom sits 0.460000 above the line's TOP edge, on both books; (3) the control VCN reads
%% the same 0.460000 for the labels and the plain floor for the bracket.
%%
%% THE PAIR: VCR and VCN are ONE VARIABLE apart — the ChordNames context — and nothing else.

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
                          (if (memq nm '(RehearsalMark StaffSymbol ChordName VoltaBracket VoltaBracketSpanner))
                              (format #t "PROBEV ~a ~a rel=~a ext=(~a . ~a) X=~a xext=(~a . ~a)\n"
                                      tag nm
                                      (ly:grob-relative-coordinate g sg Y)
                                      (car (ly:grob-extent g g Y))
                                      (cdr (ly:grob-extent g g Y))
                                      (ly:grob-relative-coordinate g sg X)
                                      (car (ly:grob-extent g g X))
                                      (cdr (ly:grob-extent g g X))))))
                      (ly:grob-array->list all)))))))
       (ly:prob-property page 'lines)))
    pages))
probeV =
#(define-scheme-function (tag) (string?)
   #{ \paper { indent = 0 ragged-right = ##t ragged-bottom = ##t
               property-defaults.fonts.sans = "LilyPond Sans Serif"
               property-defaults.fonts.serif = "LilyPond Serif"
               page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEV BOOK ~a\n" tag)
                                      (dump tag layout pages)) } #})

%% VCR — a chord row leads; two endings, a boxed mark at each ending's head. The symbol under
%%   the first ending's number is "Am"; the second ending's first bar has no symbol.
\book {
  \probeV "VCR"
  \score {
    <<
      \new ChordNames \chordmode { c2 g | a2:m s | s2 d2:m g4 c2 }
      \new Staff \relative c' {
        \time 2/4
        \mark \markup \box "A"
        \repeat volta 2 { c4 d | e f | }
        \alternative {
          { \mark \markup \box "E1" g4 a | b c | }
          { \mark \markup \box "E2" a4 g | f e | d c | }
        }
        \bar "|."
      }
    >>
    \layout { \context { \Score printInitialRepeatBar = ##t } }
  }
}

%% VCN — the control: the same book without the chord row.
\book {
  \probeV "VCN"
  \score {
    <<
      \new Staff \relative c' {
        \time 2/4
        \mark \markup \box "A"
        \repeat volta 2 { c4 d | e f | }
        \alternative {
          { \mark \markup \box "E1" g4 a | b c | }
          { \mark \markup \box "E2" a4 g | f e | d c | }
        }
        \bar "|."
      }
    >>
    \layout { \context { \Score printInitialRepeatBar = ##t } }
  }
}
