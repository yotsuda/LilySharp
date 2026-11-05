\version "2.26.0"
%% LP FIDELITY PROBE: a drum chord of different head shapes attaches them all to one stem.
%%
%% Run with ../Measure-LilyPondProbe.ps1 -Probe mixed-head-chord.ly -Prefix PROBEX (ten books).
%% The quantities are the steps between head lefts, and from the leftmost head to the first stem.
%%
%% lily/stem.cc:627-664 Stem::calc_positioning_done moves every head by the support head's stem
%% attachment less its own; :181-204 Stem::support_head is the one with the most of itself inside
%% the stem; :684 a reversed head moves by its OWN right edge; :619-623 the walk reads the sorted heads,
%% turned over for a down stem. lily/note-spacing.cc:46-77 reads the next column's ideal off the
%% first head, Stem::first_head, which is the later written of two on one row.
%%
%% THE BOOKS:
%%   MHA: <cb sn>4, a triangle and a plain head on a stem down: the triangle attaches 0.2186 inside
%%        its left edge, so the plain head moves right to meet the stem.
%%        The stem stands at that attachment, MHS in the ledger.
%%   MHD: <hh cymc>4, a cross and a circled cross on a stem down: the next column reads the wider head.
%%   MHU: <cb cymc>4, one row, down: the triangle, moved past the wider head.
%%   MHR: <cymc cymr>4, one row, down: the circled cross moved, the plain cross first.
%%   MHC: THE CONTROL: <hh sn>4, whose heads attach alike and stand together.
%%   MHP: <cb sn>4, turned up: the triangle is the support, its attachment 1.38 in.
%%   MHB: <sn cb>4, the same chord written the other way: the support is the shape, not the order.
%%   MHO: <hh cymc>4., the dots past the wider head.
%%   MHT: <cb cymc>4, turned up, one row: the first head is the earlier written.
%%   MHQ: <hh cymc>32, short values: the skyline's boxes against the next column.
%%
%% PREDICTIONS, written before running: MHA 0.22, MHS 0.22, MHD 3.00 (1.449 + the circled cross's
%% 1.551), MHU 1.46, MHR 1.41, MHC 0. FORK: MHD at 2.75 would mean the column reads a plain head.

#(define (probe-dump-pages layout pages)
   (for-each
    (lambda (page)
      (for-each
       (lambda (sys)
         (let ((sg (ly:prob-property sys 'system-grob)))
           (if (ly:grob? sg)
               (for-each
                (lambda (g)
                  (let ((nm (assq-ref (ly:grob-property g 'meta) 'name)))
                    (if (memq nm '(NoteHead Stem Dots))
                        (format #t "PROBEX GROB name=~a left=~a right=~a\n" nm
                                (car (ly:grob-extent g sg X)) (cdr (ly:grob-extent g sg X))))))
                (ly:grob-array->list (ly:grob-object sg 'all-elements))))))
       (ly:prob-property page 'lines)))
    pages))

probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEX BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})

\book {
  \probeTag "MHA"
  \score { \new DrumStaff \drummode { <cb sn>4 sn4 sn4 sn4 | } }
}

\book {
  \probeTag "MHD"
  \score { \new DrumStaff \drummode { <hh cymc>4 sn4 sn4 sn4 | } }
}

\book {
  \probeTag "MHU"
  \score { \new DrumStaff \drummode { <cb cymc>4 sn4 sn4 sn4 | } }
}

\book {
  \probeTag "MHR"
  \score { \new DrumStaff \drummode { <cymc cymr>4 sn4 sn4 sn4 | } }
}

\book {
  \probeTag "MHP"
  \score { \new DrumStaff \drummode { \once \override Stem.direction = #UP <cb sn>4 sn4 sn2 | } }
}

\book {
  \probeTag "MHB"
  \score { \new DrumStaff \drummode { \once \override Stem.direction = #UP <sn cb>4 sn4 sn2 | } }
}

\book {
  \probeTag "MHO"
  \score { \new DrumStaff \drummode { <hh cymc>4. sn8 sn2 | } }
}

\book {
  \probeTag "MHT"
  \score { \new DrumStaff \drummode { \once \override Stem.direction = #UP <cb cymc>4 sn4 sn2 | } }
}

\book {
  \probeTag "MHQ"
  \score { \new DrumStaff \drummode { <hh cymc>32 cymc32 cymc16 sn8 sn4 sn2 | } }
}

\book {
  \probeTag "MHC"
  \score { \new DrumStaff \drummode { <hh sn>4 sn4 sn4 sn4 | } }
}
