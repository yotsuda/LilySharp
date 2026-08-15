\version "2.26.0"
#(define (probe-dump-pages layout pages)
   (let loop ((ps pages) (n 1))
     (if (pair? ps)
         (let* ((page (car ps)) (lines (ly:prob-property page 'lines)))
           (let inner ((ls lines) (i 0))
             (if (pair? ls)
                 (let* ((sys (car ls))
                        (staff (ly:prob-property sys 'staff-refpoint-extent '(0 . 0))))
                   (format #t "PROBEV SYS ~a ~a staff=(~a . ~a)\n" n i (car staff) (cdr staff))
                   (inner (cdr ls) (1+ i)))))
           (loop (cdr ps) (1+ n))))))
probeTag =
#(define-scheme-function (tag) (string?)
   #{ \paper { page-post-process = #(lambda (layout pages)
                                      (format #t "\nPROBEV BOOK ~a\n" tag)
                                      (probe-dump-pages layout pages)) } #})
zeroStaffStaff = \layout {
  \context { \Staff
    \override VerticalAxisGroup.default-staff-staff-spacing =
      #'((basic-distance . 0) (minimum-distance . 0) (padding . 1)) } }
\book { \probeTag "CTRL-NOTUP"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { <<
      \new Staff { b'1 | b'1 | b'1 \bar "|." }
      \new Staff { c''2 g'4 e' | c''2 g'4 e' | b'1 \bar "|." }
    >> \layout { \zeroStaffStaff } } }
\book { \probeTag "CTRL-NONUM"
  \paper { ragged-bottom = ##t indent = 0 }
  \score { <<
      \new Staff { b'1 | b'1 | b'1 \bar "|." }
      \new Staff { \omit TupletNumber \omit TupletBracket \repeat unfold 2 { c''2 \tuplet 3/2 { g'4 e' c' } } b'1 \bar "|." }
    >> \layout { \zeroStaffStaff } } }
