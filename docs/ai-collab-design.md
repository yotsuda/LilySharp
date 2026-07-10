# Lily# × AI collaborative editing

Select text (or, later, notes on the score) and prompt in natural language —
"3度でハモらせて", "transpose up a fourth", "add a crescendo" — and the model
rewrites just that fragment. Two Lily#-specific moves make this something WYSIWYG
AI cannot do:

- **Validate-and-self-repair before showing.** Every candidate is compiled by the
  in-process compiler (milliseconds). A broken candidate is repaired (its
  diagnostics fed back to the model) and is *never shown to the user*.
- **Decide on the score.** Accept / iterate / reject is judged on the rendered
  notation, not a text diff.

Everything is non-destructive until accept: candidates are checked and rendered
offscreen; the file is untouched until a single `WorkspaceEdit` is applied (one
Ctrl+Z undoes it).

## Status

| Milestone | Scope | State |
|---|---|---|
| **M1** | `Ctrl+I` inline transform: context (selection + grammar + resolved facts) → `vscode.lm` → validate/self-repair → candidate score preview → version/range-guarded apply | **Implemented** |
| M2 | Richer resolved facts (time/key at point, surrounding `$phrase`); already injects resolved absolute pitches | Partial (pitches done) |
| **M3** | Score-side selection (click a note, shift-click to extend → same prompt) via the `data-pos` bridge | **Implemented** |
| **M4** | Ghost-text "next measure" completion (`InlineCompletionItemProvider`, validated) | **Implemented** (opt-in) |
| **M5** | Before/after score diff, BYO-key, iteration, soft-lock, quality log | **Implemented** |

## Wire protocol (custom LSP requests)

Added to `LilySharp.Lsp` (`LspProtocolDtos.cs` + `LilySharpLanguageServer.cs`).
All are read-only / non-destructive — none mutate the open document.

| Request | Params | Returns | Purpose |
|---|---|---|---|
| `lilysharp/checkCandidate` | `{ Text }` | `{ HasErrors, Diagnostics[] }` (line/char + absolute offset/length) | Validate an arbitrary candidate string (parser + full semantic registry, the same set `check` runs). Drives self-repair. |
| `lilysharp/renderText` | `{ Text, RenderName? }` | `SvgResponse` | Render a candidate string to preview SVG without touching document state (offscreen compile). |
| `lilysharp/factsForRange` | `{ TextDocument, Start, End }` (offsets) | `{ Pitches[] }` (written → resolved) | Resolved absolute pitches inside a selection — mirrors `check --pitches` via `MeasureCollector.PitchTrace`. |

Tests: `LilySharp.Tests/Lsp/AiTransformRequestTests.cs`.

## Client components

The controller lives in `editors/vscode/src/aiTransform.ts` (registered from
`extension.ts`; the LSP client stays global there and is passed in via `AiTransformDeps`).

- **Snapshot / EditApplier** — freezes `{version, range, text}` at prompt time;
  on accept, applies at the recorded offsets if the doc is unchanged, re-anchors if
  the selected text merely moved, and confirms if it can't be located (§7).
- **ContextBuilder** — selection + the Lily# grammar canon + resolved pitch facts.
  The grammar is `docs/GRAMMAR_FOR_LLM.md`, copied into `out/` by `esbuild.js` at
  build time so it ships in the VSIX and stays in sync (compact fallback if absent).
- **ModelClient** — `vscode.lm.selectChatModels()` (prefers the user's Copilot
  models). BYO-key is M5.
- **CandidateValidator** — `checkCandidate` loop; a candidate is "broken" only if it
  *introduces* errors (more than the untouched doc, or an error inside the replaced
  span), so pre-existing errors elsewhere don't block it. Up to 2 repair rounds.
- **PreviewRenderer** — `renderText` → a dedicated "Lily# — AI candidate" webview
  with Accept / Iterate / Reject (Enter / Esc), reusing the Emmentaler font assets.
- Soft-lock decoration marks the range while the model is thinking (§7).

`Ctrl+I` (`cmd+i` on macOS) when a `.lys` editor has focus; also on the editor
context menu and command palette. Requires VS Code ≥ 1.90 (the `vscode.lm` API).

## Score-side selection (M3)

In the preview webview: **plain-click** a note to anchor (still jumps the editor),
**shift-click** another to select the range. A floating "✨ Transform with AI" action
appears (or `Ctrl+I` in the webview); it posts the selected notes' source offsets to
the extension, which maps them to a text `Range`, sets that as the editor selection,
and runs the same `lilysharp.aiTransform` command — one loop, whether the selection
started in text or on the score (§6). Offset→range mapping lives in
`aiTransformFromScore` / `noteSelectionEnd` in `extension.ts`; the end offset extends
over the last note's token (chords `[...]`, duration, trailing `@annotations`).
Known limitation: exotic last-note shapes beyond a simple note/chord may under- or
over-extend the fragment end — the user still reviews on the score before applying.

## Ghost-text completion (M4)

`src/aiComplete.ts` registers an `InlineCompletionItemProvider` for `.lys`. When the
cursor sits at end-of-line just after a barline (the "what's next" moment), it
debounces (~300 ms), asks the language model for exactly one next measure, then
**compiles the doc with that measure spliced in (`checkCandidate`) and only offers it
if it introduces no error** — a broken bar is never shown. Tab accepts, like any
inline suggestion. Requests are coalesced/cached per document state so VS Code's
repeated calls don't each hit the model.

Opt-in and **off by default** (it calls the model as you type): enable
`lilysharp.ai.ghostCompletion`. Reuses `loadGrammar` / `cleanCandidate` from
`aiTransform.ts` and the same `lilysharp/checkCandidate` request.

## Refinements (M5)

- **Before/after score diff.** The candidate webview renders both the untouched score
  and the candidate, with an After/Before toggle (Tab in the panel). The original is
  rendered once per transaction (`lilysharp/renderText` on the snapshot) and reused
  across iterations.
- **BYO-key.** `src/modelClient.ts` abstracts the model behind a `ChatClient` with
  three backends — vscode.lm (Copilot), Anthropic, and OpenAI (direct `fetch`). The
  provider is chosen by `lilysharp.ai.provider` (`auto` prefers Copilot, else a stored
  key); the key is entered via the "Lily#: Set AI API Key…" command and kept in
  SecretStorage (never in settings). `lilysharp.ai.model` overrides the BYO model id.
  Both the transform and the ghost completion use this resolver; iteration and
  self-repair are provider-agnostic.
- **Iteration & soft-lock** were delivered in M1 (the Iterate button continues the
  conversation; a decoration marks the range while the model thinks).
- **Quality log (no telemetry).** Each transform logs its outcome — model label,
  self-repair rounds, accepted / rejected / iterated / failed — to the "Lily#
  Extension" output channel only. Nothing leaves the machine.

## Manual verification (not automatable headlessly)

The server requests and the client build/type-check are covered by tests and CI.
The live loop — pressing `Ctrl+I`, a real Copilot model answering, and applying the
edit — needs a running VS Code with a signed-in model and must be exercised by hand:

1. Select a few bars in a `.lys` melody, `Ctrl+I`, "3度でハモらせて".
2. Confirm no broken candidate appears; the candidate score renders in the side panel.
3. Accept, then `Ctrl+Z` — the change applies and undoes as one step.
