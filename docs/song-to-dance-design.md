# From a Song to a Dance

The third step of the way from a song to a dance (Shosa's `docs/ROADMAP.md` §2 and its `docs/HANDOFF.md`,
decided with the owner 2026-11-26): **song → singing → dance → video**. Step 1 is Shosa's Add a Dance…,
step 2 Lily#'s Write a Song over These Chords… (`docs/song-over-chords-design.md`). This step joins them
up: the steps as a walkthrough, and the next step said where a score gets its song. Started 2026-11-26
(session 1397) on the owner's word "start step 3".

## The walkthrough (`package.json` `walkthroughs` → `songToDance`)

| Step | Buttons | Checked off by |
|---|---|---|
| Write a song | Compose with AI… · Write a Song over These Chords… | either command run |
| Have a voice sing it | Set Up a Singing Voice (the singing walkthrough) | it, or Cantoria's Check Setup |
| Put a dance on it | Add a Dance… (`lilysharp.danceWithShosa`) · Install Shosa | Shosa's `shosa.addDance` run |
| Make a video | Export MP4 Video… (Shosa's `shosa.exportVideo`) | it run |

- Its page is `walkthrough/song-to-dance.md`: the four steps, each a program of its own.
- It is hidden until it is asked for (`when: lilysharp.danceGuide`), as the singing walkthrough is
  (`lilysharp.singingGuide`): installing Lily# does not open a page about dancing. **Lily#: From a Song
  to a Dance (Steps)** sets the context and opens it; where VS Code cannot, its page is shown.
- `lilysharp.danceWithShosa` runs Shosa's Add a Dance… on the score given, else Shosa takes the `.lys`
  in the editor or asks. Shosa not installed, it offers to install it (`yotsuda.shosa`; not reachable,
  the Extensions view searches for it).
- Write a Song over These Chords… run with no `.lys` in the editor (the walkthrough's button, the
  palette) now asks for the score in an open dialog. It used to say "open one" and stop.

## The next step (`songToDanceCore.ts` `gaveASong`, called in `proposeScore`)

When the musician accepts a chat's proposal (`lilysharp_proposeScore`, and so `lilysharp_writePart`)
that gives the score a **singer line where it had none**, a message says: "NAME.lys has a song now.
Next, a character can sing it and dance to it". Its buttons are **Add a Dance…** (or **Install Shosa**)
and **Show the Steps**.

- So it comes after Write a Song over These Chords…, and after Compose with AI… for a new piece with
  words (its "before" is empty). A score that sang already is not told again when its notes change.
- It is not awaited: the chat is answered at once, and the message stays until it is closed.
- Shosa's Add a Dance… saves an unsaved `.lys` itself (the dance reads the song from the disk).

## Checked

- `test/songToDanceCore.test.ts`: the singer line (not in a comment, not `singers`), the rule, the
  buttons, the call in `proposeScore`, the walkthrough's steps, buttons and page.
- A real VS Code 1.147 (`--extensionTestsPath`, Lab `sessions/p1397/vscode-e2e`): the steps open as the
  walkthrough (not the fallback page); with no Shosa, Add a Dance… offers it and installs
  `yotsuda.shosa`; Write a Song with no score open asks for one, opens it and hands the chat the request.

## Not done yet

- The owner's check in a real VS Code (`editors/vscode/MANUAL-CHECKS.md`, "From a Song to a Dance"):
  the walkthrough's look, and the message after a song is accepted in the chat.
- Shosa is not yet in the Marketplace (0.3.0 is its first release), so Install Shosa ends in the
  Extensions view's search until it is.
- Step 4 (Lily#): Arrange an Accompaniment…: done 2026-11-26, `docs/accompaniment-design.md`.
