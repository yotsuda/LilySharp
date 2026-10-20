\version "2.26.0"

%% A HAIRPIN OVER A GRACE NOTE (session 916, HANDOFF §2 U8 ⒟).
%%
%% The grace column is a support of the DynamicLineSpanner like any note column, with its own
%% head and its own (UP) stem. Until session 916 Lily# read the grace item's head and stem at
%% full size in the ungraced direction (the b' a middle-line DOWN stem 3.5 long), and the
%% wedge stood 0.24 lower than LilyPond's. Found by the sweep in samples/nocturne.lys bar 5.
%%
%% The music came out of `lysc ly` (RULES 6); the dump is the page's final hairpin centre
%% below the staff's middle line, the ledger's HairpinWedgeCentreBelowStaff.
%%
%% Output (LilyPond 2.26.0):  PROBE HAIRPIN y=-4.021815

\paper {
  page-post-process = #(lambda (layout pages)
    (for-each
     (lambda (page)
       (for-each
        (lambda (sys)
          (let ((sg (ly:prob-property sys 'system-grob)))
            (if (ly:grob? sg)
                (let* ((all (ly:grob-array->list (ly:grob-object sg 'all-elements)))
                       (staff (car (filter (lambda (g) (eq? 'StaffSymbol (assq-ref (ly:grob-property g 'meta) 'name))) all))))
                  (for-each
                   (lambda (g)
                     (if (eq? 'Hairpin (assq-ref (ly:grob-property g 'meta) 'name))
                         (format #t "\nPROBE HAIRPIN y=~,6f\n"
                                 (- (ly:grob-relative-coordinate g sg Y)
                                    (ly:grob-relative-coordinate staff sg Y)))))
                   all)))))
        (ly:prob-property page 'lines)))
     pages))
}

rh = \relative c' {
  a'4 cis e\< d |
  \grace { b16 } a4 cis8 e g4 fis8 e |
  <fis a>1\f |
}

\score {
    \new Staff { \clef "treble" \rh }
  \layout { indent = 15\mm \context { \Score printInitialRepeatBar = ##t } }
}
