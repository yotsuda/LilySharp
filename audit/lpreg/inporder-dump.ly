\version "2.24.0"
% inporder.ly + X extent dump (system 相対)。比較器のみの差し替え。
#(define (dump-x grob)
   (let* ((sys (ly:grob-system grob))
          (ext (ly:grob-extent grob sys X)))
     (format (current-error-port) "DUMP ~a ~,4f ~,4f\n"
             (assq-ref (ly:grob-property grob 'meta) 'name)
             (car ext) (cdr ext))))
\paper { indent = 0 }
\score {
  <<
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
  >>
  \layout {
    ragged-right = ##t
    \context {
      \Voice
      \override NoteHead.after-line-breaking = #dump-x
      \override Stem.after-line-breaking = #dump-x
      \override Script.after-line-breaking = #dump-x
      \override TextScript.after-line-breaking = #dump-x
      \override DynamicText.after-line-breaking = #dump-x
    }
    \context {
      \Lyrics
      \override LyricText.after-line-breaking = #dump-x
    }
  }
}
