# Demo clips for the site — shot list

The pages carry a hidden placeholder for each clip below:

```html
<figure class="demo-video" data-pending>
  <video src="videos/NAME.mp4" …></video>
  <figcaption>…</figcaption>
</figure>
```

A figure with `data-pending` is not shown (`display:none`), and `build-site.ps1` does not look
for its file. **To publish a clip:** save it here as `site/videos/NAME.mp4`, then delete
`data-pending` from its figure in the page's source (`site/editor-body.html` or
`site/chords-body.html`). The build then refuses to go out if the file is missing, and copies it
into `_site/videos/`. This file is not published (only files a page references are).

Clips with sound use `controls` (no autoplay — a browser will not autoplay sound); silent clips
are `muted loop playsinline autoplay`. The HTML comment above each placeholder repeats its shot.

## Recording spec

- MP4, H.264 (a `.webm` beside it is fine; add a second `<source>` then), about 1280×720,
  30 fps, **5 MB or less**. 5–20 s each; trim the dead time at both ends.
- VS Code with the preview docked to the right (`Ctrl+K V`), a light theme (the pages show the
  printed page in light), editor zoom so the source reads at video size (Ctrl+= once or twice,
  or `"editor.fontSize": 18`). Hide the minimap and the side bar.
- Show keystrokes on screen for the key-driven clips (e.g. the *Screencast Mode* of VS Code:
  *Developer: Toggle Screencast Mode*).
- Every snippet below was checked with `lysc check` (Lily# 0.9.0 + Unreleased); the clips
  marked "warns" are supposed to show that warning.

## The clips

| # | File | Page — section | Sound | Length |
|---|---|---|---|---|
| 1 | `editor-octave-mark.mp4` | editor — Typing aids | no | ~10 s |
| 2 | `editor-duration-overwrite.mp4` | editor — Typing aids | no | ~10 s |
| 3 | `editor-step-octave.mp4` | editor — Stepping | **yes** | ~15 s |
| 4 | `editor-audition.mp4` | editor — Audition | **yes** | ~10 s |
| 5 | `editor-preview.mp4` | editor — The preview | no | ~15 s |
| 6 | `editor-quickfix.mp4` | editor — Completion, hover, fixes | no | ~10 s |
| 7 | `editor-split-sections.mp4` | editor — Commands | no | ~20 s |
| 8 | `chords-shape-step.mp4` | chords — Let the editor write the shapes | **yes** | ~15 s |
| 9 | `ai-transform.mp4` | editor — AI in the editor | no | ~20 s |

Start clips 1–5 from this file:

```
part m { clef treble }
section A { m { c4 cis4. d8 e4 | c4 d4 e4 f4 | c4 e g <c e g>4 | g2 g | } }
form { ~A }
score { staff m }
```

### 1. `editor-octave-mark` — an octave mark lands after the pitch
1. Click between the `c` and the `is` of `cis4.` (bar 1).
2. Type `'` twice: `cis''4.` — the marks go after `cis`, before `4.`; the caret does not move and
   the note climbs in the preview.
3. Type `,` once: one `'` is cancelled (`cis'4.`).
4. Put the caret after `e4` (end of bar 1) and type `'`: `e'4`.

### 2. `editor-duration-overwrite` — a digit replaces the duration
1. Caret right after `d4` in bar 2; type `8` → `d8` (not `d48`).
2. Caret after `e4`; type `2` → `e2`.
3. Caret between `f` and `4`; type `1` → `f1`. The bar re-spaces (it now warns that the bar is
   too long — fine, or undo afterwards).
4. Optional: after `cis4.` type `8` → `cis8.` (the dot is kept).

### 3. `editor-step-octave` — Ctrl+Shift+Up/Down (with sound)
1. Caret on the `c4` that opens bar 3. `Ctrl+Shift+Up` twice (`c''4`), `Ctrl+Shift+Down` once
   (`c'4`) — each result sounds.
2. Caret on the `e` inside `<c e g>`: `Ctrl+Shift+Up` — only that member moves (`<c e' g>`).
3. Caret right after the `>`: `Ctrl+Shift+Up` — the whole chord moves (`<c e' g>'4`), and it
   sounds.

### 4. `editor-audition` — notes sound as you move and type (with sound)
1. Caret at the start of bar 4 (`g2 g`); move right with the arrow keys across bars 3–4 — each
   note sounds once as the caret lands on it.
2. At the end of bar 4 type ` a4 |` — the `a` sounds as it is typed.

### 5. `editor-preview` — the preview follows the text
1. At the end of the music type ` a4 b c' d' |` — the score grows with each keystroke.
2. Move the caret along bar 1 — the caret's note lights in the score.
3. Select bar 2 — all its notes light.
4. Click a note in the preview — the editor jumps to it.

### 6. `editor-quickfix` — a quick fix from the lightbulb (warns)
```
part melody { clef treble }
part bass { clef bass octave 3 }
section A {
  melody { c4 d e f | g1 | }
  bass { c1 | }
}
form { A }
score { staff melody  staff bass }
```
1. Hover the squiggle on `bass` — *Section 'A' is not the same length everywhere it is written:
   2 bar(s) in part 'melody'; 1 bar(s) in part 'bass'…*
2. `Ctrl+.` → *Add 1 bar line to bass (|)*. The warning goes; the preview shows the filled bar.

(The owner's suggestion was `@upbow` → `@upBow`. There is no quick fix for a wrong-case name —
the warning only says *Names are case-sensitive: write '@upBow'* — so this clip uses the
section-length fix instead.)

### 7. `editor-split-sections` — Split Sections to Match a Part (warns)
```
part vn1 {
  clef treble
  section A { c'4 d e f | g1 | }
  section B { a4 b c d | e1 | }
}
part vn2 {
  clef treble
  section A { e'4 f g a | b1 | c4 d e f | g1 | }
}
form { A }
score { staff vn1  staff vn2 }
```
1. Hover the warning on `A` (*4 bar(s) in part 'vn2'; 2 bar(s) in part 'vn1'*).
2. `Ctrl+.` → *Split section A in the other parts to match vn1 (…)…*.
3. Pause on the confirmation (the plan: split A in vn2 after bar 2 → A, B; form main: A → A B),
   click **Apply**.
4. vn2 now writes `section A` and `section B`, the form reads `A B`, the warning is gone.

### 8. `chords-shape-step` — stepping a chord's shape (with sound)
```
part m { clef treble }
section A { m { c'1@chord(Cm7) | f1@chord(F7) | } }
form { ~A }
score { staff m }
```
1. Caret inside `Cm7`. `Ctrl+Shift+Up`: `@chord(Cm7 x35343)` — the diagram appears, the chord
   sounds; the status bar says `Cm7: shape 1 of …`.
2. `Ctrl+Shift+Up` three more times — the shape and the diagram change, each one sounds.
3. `Ctrl+Shift+Down` back to `x35343`, then once more: the shape is removed and the diagram goes.
4. Optional: hover `F7` — *Ctrl+Shift+↑ adds a chord diagram (guitar: …)*.

### 9. `ai-transform` — Transform Selection with AI
Needs a signed-in GitHub Copilot (or another model provider) in VS Code.
```
part m { clef treble }
section A { m { e'4 d c d | e e e2 | d4 d e d | c1 | } }
form { ~A }
score { staff m }
```
1. Select bars 1–2. `Ctrl+I`, type *add a harmony line a third below*, Enter.
2. Wait for the engraved candidate beside the original; toggle After/Before once.
3. Accept — the file changes; `Ctrl+Z` once restores it (optional).
Cut the waiting time if it runs long.
