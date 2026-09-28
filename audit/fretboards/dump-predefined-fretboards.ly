\version "2.26.0"

%% LILYPOND'S PREDEFINED FRET DIAGRAMS, asked of LilyPond itself.
%%
%% The predefined tables (ly/predefined-guitar-fretboards.ly and the ninth-chord
%% file it includes, ly/predefined-ukulele-fretboards.ly,
%% ly/predefined-mandolin-fretboards.ly) are NOT plain data: many entries are
%% `#(chord-shape 'bes:m guitar-tuning)` or `#(offset-fret 2 (chord-shape ...))`,
%% i.e. a barre shape moved up the neck by Scheme. So the .ly text is not parsed;
%% LilyPond evaluates every file and this records what it stored.
%%
%% \storePredefinedDiagram (ly/predefined-fretboards-init.ly:65-82) stores into
%% default-fret-table (ly/declarations-init.ly:43) under the key
%% (tuning . pitches) — the string tuning as a list of pitches, string 1 first,
%% and the chord's pitches as \chordmode spells them — the VERBOSE fret-diagram
%% definition: (place-fret STRING FRET [FINGER]), (open STRING), (mute STRING),
%% (barre FROM-STRING TO-STRING FRET), strings numbered 1 = the highest.
%%
%% It is redefined here, before the three files are included, to do exactly what
%% it does (the same three lines) and to remember WHERE each entry was written,
%% so every generated entry can cite its file and line. The dump then walks
%% default-fret-table itself, so an entry a later line overwrote is written once,
%% with the later line.
%%
%% Writes predefined-fretboards.txt beside itself, one entry per line:
%%   TUNING-SEMITONES | PITCHES | FILE:LINE | VERBOSE-DEFINITION
%% semitones counted from middle C (c' = 0), string 1 first; each pitch as
%% notename(0=c..6=b),alteration-in-semitones,octave (c' is octave 0).
%% A file port rather than stderr (LilyPond's progress output interleaves).
%%
%% Regenerate LilySharp.Core/Music/PredefinedFretboardsGenerated.cs with
%% Generate-PredefinedFretboards.ps1 (it runs this file).

#(define lys-origins (make-hash-table 1031))

storePredefinedDiagram =
#(define-void-function
   (fretboard-table chord tuning diagram-definition)
   (hash-table? ly:music? pair? string-or-pair?)
   (let* ((pitches (event-chord-pitches
                    (car (extract-named-music chord 'EventChord))))
          (hash-key (cons tuning pitches))
          (verbose-definition (if (string? diagram-definition)
                                  (parse-terse-string
                                   (remove-whitespace diagram-definition))
                                  diagram-definition))
          (where (ly:input-file-line-char-column (*location*))))
     (hash-set! fretboard-table hash-key verbose-definition)
     (hash-set! lys-origins hash-key
                (format #f "~a:~a" (basename (car where)) (cadr where)))))

\include "predefined-guitar-fretboards.ly"
\include "predefined-ukulele-fretboards.ly"
\include "predefined-mandolin-fretboards.ly"

#(define (pitch->string p)
   (format #f "~a,~a,~a"
           (ly:pitch-notename p)
           (* 2 (ly:pitch-alteration p))
           (ly:pitch-octave p)))

#(call-with-output-file "predefined-fretboards.txt"
   (lambda (port)
     (hash-for-each
      (lambda (key value)
        (format port "~a | ~a | ~a | ~s\n"
                (string-join (map (lambda (p) (number->string (ly:pitch-semitones p)))
                                  (car key)) " ")
                (string-join (map pitch->string (cdr key)) " ")
                (hash-ref lys-origins key "?")
                value))
      default-fret-table)))
