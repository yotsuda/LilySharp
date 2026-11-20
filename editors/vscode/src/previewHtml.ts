// Lily# - Music notation compiler
// Copyright (C) 2025-2026 Yoshifumi Tsuda
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// The preview's page: the HTML the host hands the webview, with the page's whole script in it.
// Until session 1295 this was a template literal in the middle of extension.ts (6,573 of its
// 12,453 lines). Moved as it stood - nothing here knows a VS Code, so the text can be built and
// read under plain node.

import { PLAIN_TONES_JS } from './plainTones';
import { FOLLOW_SCROLL_CSS, FOLLOW_SCROLL_JS } from './followScroll';

// How far past an item, in characters of the source, the caret may stand and still light it.
const HIGHLIGHT_DISTANCE_THRESHOLD = 50;

/** The URLs of the preview's SoundFont synth, as the webview reaches them (synthUrls). */
export interface SynthUrls { script: string; processor: string; soundFont: string; stretch: string }

// The CSP: the page's own scripts by nonce; the synth bundle, its AudioWorklet
// processor (fetched by audioWorklet.addModule — a worklet module answers to
// script-src) and the fonts from the extension's resources; the processor's Vorbis
// decoder is WebAssembly ('wasm-unsafe-eval'), and previewSynth.ts re-adds the processor
// from a blob when the host will not hand a worklet a cross-origin module; the sound
// font is fetch()ed (connect-src).
export function getPreviewHtml(fontUri: string, braceFontUri: string, cspSource: string, nonce: string,
                        theme: 'auto' | 'light' | 'dark', textFontCss: string,
                        synth: SynthUrls = { script: '', processor: '', soundFont: '', stretch: '' },
                        midiOutput: string = ''): string {
    return `<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; font-src ${cspSource}; script-src 'nonce-${nonce}' ${cspSource} 'wasm-unsafe-eval' blob:; connect-src ${cspSource};">
    <script nonce="${nonce}">
        // The color scheme is a class on <html>, set HERE — before the body parses —
        // so a dark preview never flashes white on open. 'auto' follows VS Code's
        // theme through prefers-color-scheme (the webview mirrors the editor theme);
        // the main script below re-applies it when the setting or the theme changes.
        (function () {
            const mode = '${theme}';
            const dark = mode === 'dark'
                || (mode === 'auto' && window.matchMedia('(prefers-color-scheme: dark)').matches);
            document.documentElement.classList.toggle('theme-dark', dark);
        })();
        // The SoundFont synth's files (docs/playback-design.md): the worklet processor
        // and the sound font ('' = none, the page then plays its oscillator patches); and
        // the MIDI output port's name ('' = the synth here), lilysharp.playback.midiOutput.
        window.lysSynthConfig = { processor: '${synth.processor}', soundFont: '${synth.soundFont}', stretch: '${synth.stretch}', midiOutput: ${JSON.stringify(midiOutput)} };
    </script>
    <script nonce="${nonce}" src="${synth.script}"></script>
    <style>
        @font-face {
            font-family: 'Emmentaler';
            src: url('${fontUri}') format('woff2');
        }
        @font-face {
            font-family: 'Emmentaler-Brace';
            src: url('${braceFontUri}') format('woff2');
        }
        /* The score's text faces — the ones the layout measured (scoreFonts.ts). */
        ${textFontCss}
        body {
            margin: 0;
            padding: 0;
            background: white;
            display: flex;
            flex-direction: column;
            /* The score is not selectable text. Without this, clicking a note
               starts a text selection of the nearest SVG <text> glyph (a note-
               head / accidental), which Chromium paints as a grey rectangle over
               the neighbouring note. */
            user-select: none;
            -webkit-user-select: none;
            /* HEIGHT, not min-height: min lets the body grow with the sheet,
               so the WEBVIEW scrolls and .main-content never overflows —
               scrollTop stays 0 and the page-nav buttons do nothing. Capping
               at the viewport makes .main-content the (only) scroller. */
            height: 100vh;
            overflow: hidden;
        }
        .toolbar {
            padding: 8px 12px;
            background: #f0f0f0;
            border-bottom: 1px solid #ddd;
            display: flex;
            align-items: center;
            gap: 8px;
            flex-shrink: 0;
            /* A narrow preview wraps the header onto a second row; its buttons keep their size
               (owner's request 2026-10-25: they were squeezed). */
            flex-wrap: wrap;
            row-gap: 6px;
            /* Stay pinned to the top while the sheet scrolls underneath. */
            position: sticky;
            top: 0;
            z-index: 10;
        }
        .toolbar > * { flex-shrink: 0; }
        .toolbar .group { display: flex; align-items: center; gap: 8px; }
        .toolbar label {
            font-family: system-ui, sans-serif;
            font-size: 13px;
            color: #333;
        }
        .toolbar select {
            padding: 4px 8px;
            font-size: 13px;
            border: 1px solid #ccc;
            border-radius: 4px;
            background: white;
            /* No min-width: the width is measured from the selected score name
               and set inline (fitSelectToSelection). A floor here would win over
               that inline width and put the empty gap back. */
        }
        .toolbar button {
            padding: 4px 12px;
            font-size: 13px;
            font-family: system-ui, sans-serif;
            border: 1px solid #ccc;
            border-radius: 4px;
            background: white;
            color: #333;
            cursor: pointer;
        }
        .toolbar button:hover {
            background: #e8e8e8;
        }
        .toolbar button:disabled {
            opacity: 0.5;
            cursor: default;
        }
        .toolbar .sep {
            width: 1px;
            height: 20px;
            background: #ccc;
        }
        #pageInput {
            /* No width here: updatePageInfo sizes it to the page count's digits. */
            padding: 3px 1px 3px 4px;
            font-family: system-ui, sans-serif;
            font-size: 13px;
            text-align: right;
            border: 1px solid #ccc;
            border-radius: 4px;
            background: white;
            color: #333;
        }
        #pageInput:disabled {
            opacity: 0.5;
        }
        #pageCount {
            font-family: system-ui, sans-serif;
            font-size: 13px;
            color: #333;
            white-space: nowrap;
        }
        /* Dark scheme: :root.theme-dark, not a prefers-color-scheme media query — the
           class is the lilysharp.preview.theme setting resolved (see the head script),
           which is how "always light" / "always dark" can override the editor theme. */
        :root.theme-dark .toolbar .sep { background: #555; }
        :root.theme-dark #pageCount { color: #ccc; }
        :root.theme-dark #pageInput {
            background: #3c3c3c;
            color: #ccc;
            border-color: #555;
        }
        .main-content {
            flex: 1;
            /* A flex item defaults to min-height:auto, which refuses to shrink
               below its content and pushes the whole page (and the toolbar) into a
               window-level scroll. min-height:0 lets THIS pane own the scroll. */
            min-height: 0;
            padding: 20px;
            display: flex;
            justify-content: center;
            align-items: flex-start;
            overflow: auto;
            /* The sheet is dragged to scroll (installDragScroll): an open hand over it, a closed
               one while it moves, and the pointer over what a click jumps to. */
            cursor: grab;
        }
        /* The pointer over a note is a class the mouse's own handler sets (over-note, installBarHover),
           NOT a rule on [data-pos]: a selector that names the attribute has the browser restyle every
           element whose data-pos is written, and a keystroke that makes the text longer writes it on
           every page after the caret's (shiftPage). MEASURED in a real VS Code (session 1302, a
           1000-bar book, 8 of 16 pages shifted): the layout after the swap 18.1 ms with the rule,
           5.0 without. The closed hand comes after the pointer, so a drag wins over both. */
        .main-content.over-note,
        .main-content.over-bar {
            cursor: pointer;
        }
        .main-content.dragging {
            cursor: grabbing;
        }
        /* The bar a click would go to (barStaffAt), its staff tinted in the colour the scan view
           lights a bar in (omrScan.ts rect.light; the owner's request 2026-10-14: the two alike). */
        #svgContainer rect.bar-staff.hover,
        #svgContainer rect.bar-staff.peer,
        #svgContainer rect.bar-staff.caret,
        #svgContainer rect.bar-staff.playing {
            fill: rgba(0, 120, 215, 0.14);
        }
        /* The bars a score selection covers (owner's request 2026-10-25: the bars lit, not the
           notes — the editor's text is not selected, so lit notes would read as if it were). */
        #svgContainer rect.bar-staff.selected {
            fill: rgba(0, 120, 215, 0.22);
        }
        .container {
            max-width: 100%;
        }
        #svgContainer {
            transform-origin: top left;
            transition: transform 0.1s ease-out;
        }
        /* The A-B repeat's flags and the bars between them (docs/ab-repeat-design.md): drawn in the
           page's own units beside the bar boxes, so they scroll and zoom with the score. */
        #svgContainer .ab-shade { fill: rgba(232, 113, 10, 0.10); pointer-events: none; }
        #svgContainer .ab-flag { cursor: grab; pointer-events: all; }
        #svgContainer .ab-flag .ab-hit { fill: transparent; }
        #svgContainer .ab-flag .ab-pole { stroke: #e8710a; stroke-width: 0.25; }
        #svgContainer .ab-flag .ab-pennant { fill: #e8710a; transform-box: fill-box; transform-origin: center; transition: transform 0.08s; }
        #svgContainer .ab-flag text { fill: #fff; font: bold 1.15px sans-serif; pointer-events: none; }
        #svgContainer .ab-flag.grabbed .ab-pole { stroke-width: 0.5; }
        #svgContainer .ab-flag.grabbed .ab-pennant { transform: scale(1.3); stroke: #7a3a00; stroke-width: 0.15; }
        #svgContainer .ab-flag.ab-landed .ab-pole { animation: ab-flash 0.35s ease-out; }
        @keyframes ab-flash { from { stroke-width: 1.1; stroke: #ffb066; } }
        body.ab-grabbing, body.ab-grabbing * { cursor: grabbing !important; }
        #abGhost { position: fixed; pointer-events: none; z-index: 1001; opacity: 0.55; background: #e8710a; color: #fff;
            font: bold 11px sans-serif; padding: 1px 5px; border-radius: 2px; transform: translate(6px, -18px); }
        #abRubber { position: fixed; left: 0; top: 0; width: 100vw; height: 100vh; pointer-events: none; z-index: 1000; }
        #abRubber line { stroke: #e8710a; stroke-width: 1.5; stroke-dasharray: 4 3; }
        #svgContainer svg {
            display: block;
        }
        /* The pages as sheets (owner's request 2026-10-25): the server leaves a gap of nothing
           between two pages (SvgDocumentContext.InteractivePageGap), and the pane shows dark
           through it and around the sheet — once a score is shown (a "Loading" line keeps the
           plain background it is readable on). */
        .main-content:has(#svgContainer > svg) {
            background: #2b2b2b;
        }
        /* Dark: the SVG paints its page white and the scheme inverts it (filter below), so the
           sheet is black — black around it would not show; mid grey does. */
        :root.theme-dark .main-content:has(#svgContainer > svg) {
            background: #3c3c3c;
        }
        .zoom-info {
            position: fixed;
            bottom: 10px;
            right: 10px;
            background: rgba(0,0,0,0.7);
            color: white;
            padding: 4px 8px;
            border-radius: 4px;
            font-family: monospace;
            font-size: 12px;
            opacity: 0;
            transition: opacity 0.3s;
        }
        .zoom-info.visible {
            opacity: 1;
        }
        .highlight {
            filter: drop-shadow(0 0 4px #ff6600);
        }
        /* A head or rest marked @todo wears a ring (the SVG's ellipse.todo-ring, drawn by
           the interactive SVG alone), and a memo stands in a callout box with a leader to
           the ring — both stamped data-todo, both painted BY THE SVG ITSELF (fill="none"
           and a stroke on the element): the caret highlight (highlightPositions) reads a
           shape's attributes to decide whether to recolour its fill or its stroke, and a
           ring styled only from here had no stroke attribute, so the highlight filled it
           into a disc (owner's report 2026-10-10). No paint here, then. The preview always
           shows them (owner's decision 2026-10-25: the setting that hid them is gone).
           Until 2026-10-10 the head itself was filled red (owner: "赤くつぶれる").
           The dark scheme's invert + hue-rotate(180deg) brings the red back to red. */
        /* A staff's bar an OMR reader flagged (the scan view's B4), framed as on the scan: the
           frame applyBarFlags draws round it, two staff spaces taller above and below. */
        #svgContainer path.flag-frame {
            stroke: #e53935;
            stroke-width: 2;
            stroke-dasharray: 4 3;
            vector-effect: non-scaling-stroke;
        }
        .error {
            color: #f44336;
            background: #ffebee;
            padding: 16px;
            border-radius: 4px;
            white-space: pre-wrap;
            font-family: monospace;
        }
        .loading {
            color: #666;
            font-family: system-ui, sans-serif;
        }
        /* Non-destructive error surface: an overlay pinned to the BOTTOM of the
           preview so a transient syntax error / warning while typing neither blanks
           the score nor reflows it. (As an in-flow banner above .main-content it
           shrank the scroller on every appear/disappear, jittering the sheet up and
           down.) position:fixed takes it out of flow → the sheet never moves. */
        .error-banner {
            display: none;
            position: fixed;
            left: 0;
            right: 0;
            bottom: 0;
            z-index: 20;
            color: #f44336;
            background: #ffebee;
            border-top: 1px solid rgba(244, 67, 54, 0.5);
            padding: 6px 12px;
            white-space: pre-wrap;
            font-family: monospace;
            font-size: 12px;
            max-height: 6em;
            overflow-y: auto;
        }
        .error-banner.visible {
            display: block;
        }
        /* The last good preview stays but dims while the current source is invalid. */
        #svgContainer.stale {
            opacity: 0.45;
            transition: opacity 0.15s;
        }
        :root.theme-dark body {
            background: #1e1e1e;
        }
        :root.theme-dark .toolbar {
            background: #2d2d2d;
            border-bottom-color: #444;
        }
        :root.theme-dark .toolbar label {
            color: #ccc;
        }
        :root.theme-dark .toolbar select {
            background: #3c3c3c;
            color: #ccc;
            border-color: #555;
        }
        :root.theme-dark #svgContainer svg {
            filter: invert(1) hue-rotate(180deg);
        }
        :root.theme-dark .highlight {
            filter: drop-shadow(0 0 4px #00ccff);
        }
        :root.theme-dark .error {
            background: #4a1515;
            color: #ff8a80;
        }
        :root.theme-dark .error-banner {
            background: #4a1515;
            color: #ff8a80;
            border-top-color: rgba(255, 138, 128, 0.4);
        }
        :root.theme-dark .loading {
            color: #aaa;
        }
        /* M3: floating "transform with AI" action, shown while a score note range
           is selected. */
        /* The header's AI door (owner's request 2026-10-25: always there; it names what it hands
           the AI — the whole score, or the bars selected). */
        .toolbar button.ai {
            font-weight: 600;
            background: var(--vscode-button-background);
            color: var(--vscode-button-foreground);
        }
        .toolbar button.ai:hover { background: var(--vscode-button-hoverBackground); }
        /* Pressed, it sinks (owner's request 2026-10-29: a click on ✨ showed nothing — the chat
           opens beside, out of the eye's way): shaded in, nudged down, darker. */
        .toolbar button.ai:active, .toolbar button.ai.pressed {
            box-shadow: inset 0 2px 4px rgba(0, 0, 0, 0.45);
            transform: translateY(1px);
            filter: brightness(0.85);
        }
        /* A toggle that is on looks pressed in (owner's request 2026-10-25: Range read as a plain
           button): sunk, blue-edged, tinted. */
        .toolbar button.toggle[aria-pressed="true"] {
            background: #d6e6f8; border-color: #0078d7; color: #0b4a8b;
            box-shadow: inset 0 1px 3px rgba(0, 0, 0, 0.3);
        }
        .toolbar button.toggle[aria-pressed="true"]:hover { background: #c8ddf5; }
        /* Play lit while it plays, as a tape deck's (owner's choice 2026-11-17: Play stays pressable
           while playing — it plays again from A — so nothing said it was playing). Paused, Pause is
           the one lit (its aria-pressed); stopped, neither. */
        .toolbar button#playBtn.playing {
            background: #d6e6f8; border-color: #0078d7; color: #0b4a8b;
            box-shadow: inset 0 1px 3px rgba(0, 0, 0, 0.3);
        }
        .toolbar button#playBtn.playing:hover { background: #c8ddf5; }
        /* …and an amber edge while what sounds is the last version of the text that could be read
           (the text as it is now cannot be: a note half typed). */
        .toolbar button#playBtn.playing.stale {
            border-color: #d19a00;
            box-shadow: inset 0 0 0 1px #d19a00, inset 0 1px 3px rgba(0, 0, 0, 0.3);
        }
        /* The practice menu open looks as the Range panel open does (owner's request 2026-11-02). */
        .toolbar button.practice[aria-expanded="true"] {
            background: #d6e6f8; border-color: #0078d7; color: #0b4a8b;
            box-shadow: inset 0 1px 3px rgba(0, 0, 0, 0.3);
        }
        .toolbar button.practice[aria-expanded="true"]:hover { background: #c8ddf5; }
        /* An icon button (Range: a double-headed arrow just left of an eighth note — owner's
           request 2026-10-25: stemless heads read as no note, and a quarter as a tempo) is as
           big as the emoji one (✨). */
        /* Both are one height and hold their glyph in the middle (owner's report 2026-10-25: the
           icon sat off centre). */
        .toolbar button.icon {
            display: inline-flex; align-items: center; justify-content: center;
            height: 28px; width: 42px; padding: 0; line-height: 1;
        }
        .toolbar button.icon svg { display: block; width: 15px; height: 15px; }
        /* The voice badge: a small microphone over the bottom-right corner of ▶ — no border, no
           background, not a button. Grey and pulsing while the engine sings, blue when every voice
           is ready, the error colour when one will not be sung. */
        .toolbar .play-wrap { position: relative; display: inline-flex; }
        .voice-badge {
            position: absolute; right: -5px; bottom: -5px; width: 15px; height: 15px; z-index: 1;
            display: inline-flex; align-items: center; justify-content: center;
            border-radius: 50%; background: white; padding: 1px; box-sizing: border-box; color: #888;
            cursor: pointer; line-height: 0;
        }
        /* The display rule above beats the hidden attribute's own display:none (the range panel and
           the tip say this too): without this line the badge never hid, and kept the look of its
           last state - the pulsing placeholder - on a file with no lyrics (owner's reports, sessions
           1160-1162; the host and the page had the state right all along). */
        .voice-badge[hidden] { display: none; }
        .voice-badge svg { width: 12px; height: 12px; display: block; }
        /* The voices' progress (owner's requests 2026-11-09, sessions 1178 and 1179): a thin bar under
           Play while the engine sings, running in under the microphone, its goal. Counted on what
           the engine says it has to sing anew (voiceBar), an estimate where it says nothing. Its
           right end stops a little short of the microphone's (owner's request 2026-11-11: 6px was
           a touch long). */
        .toolbar .voice-bar {
            position: absolute; left: 0; right: 8px; bottom: -5px; height: 3px;
            border-radius: 2px; background: rgba(128, 128, 128, 0.3); overflow: hidden; pointer-events: none;
        }
        .toolbar .voice-bar[hidden] { display: none; }
        .toolbar .voice-bar > span { display: block; height: 100%; width: 0; background: #0078d7; transition: width 0.15s linear; }
        :root.theme-dark .toolbar .voice-bar > span { background: #4fa3ff; }
        /* The same bar on a voice's line in the practice menu, running in under its microphone. */
        .voice-row-bar {
            position: absolute; left: 2px; right: 10px; bottom: 1px; height: 2px;
            border-radius: 1px; background: rgba(128, 128, 128, 0.3); overflow: hidden; pointer-events: none;
        }
        .voice-row-bar[hidden] { display: none; }
        .voice-row-bar > span { display: block; height: 100%; width: 0; background: #0078d7; transition: width 0.15s linear; }
        :root.theme-dark .voice-row-bar > span { background: #4fa3ff; }
        .voice-badge[data-state="waiting"] { color: #888; }
        .voice-badge[data-state="preparing"] { color: #888; animation: voice-pulse 1.2s ease-in-out infinite; }
        .voice-badge[data-state="ready"] { color: #0078d7; }
        .voice-badge[data-state="failed"] { color: var(--vscode-errorForeground, #b00020); }
        :root.theme-dark .voice-badge { background: #2d2d2d; }
        :root.theme-dark .voice-badge[data-state="ready"] { color: #4fa3ff; }
        @keyframes voice-pulse { 0%, 100% { opacity: 0.3; } 50% { opacity: 1; } }
        /* How long the score plays (owner's request 2026-10-25), beside the transport. */
        .toolbar .length {
            font-family: system-ui, sans-serif; font-size: 12px; color: #555;
            font-variant-numeric: tabular-nums;
        }
        /* "0:00 / 4:38": the position and the length read as one, a space apart — not the
           group's 8px between two buttons (owner's report 2026-11-16) — and over them the bar,
           "Bar 12 / 118" (owner's request 2026-11-19): two small lines in the header's one. */
        /* The two lines' "/" stand one over the other (owner's request 2026-11-19): a grid of three
           columns, what is before the "/" set right, what is after it set left. */
        .toolbar .where { display: inline-grid; grid-template-columns: auto auto auto; column-gap: 3px; align-content: center; line-height: 1.15; }
        .toolbar .where .length { font-size: 11px; white-space: nowrap; }
        .toolbar .where #barNow { grid-area: 1 / 1; justify-self: end; }
        .toolbar .where #barSlash { grid-area: 1 / 2; }
        .toolbar .where #barLast { grid-area: 1 / 3; }
        .toolbar .where #posInfo { grid-area: 2 / 1; justify-self: end; }
        .toolbar .where #posSlash { grid-area: 2 / 2; }
        .toolbar .where #lengthInfo { grid-area: 2 / 3; }
        /* The button that brings a Play's view back to the notes sounding (followScroll.ts). */
        ${FOLLOW_SCROLL_CSS}
        /* The Range panel (owner's request 2026-10-25): each part's lowest and highest note over
           the score, top right under the toolbar; a click on one shows it on the score. */
        .range-panel {
            /* Under the toolbar's 10: a panel dragged up goes behind the header, which stays whole
               (owner's request 2026-11-02). */
            position: fixed; right: 12px; z-index: 5;
            /* Its own width wherever it stands: left to the browser, a panel dragged towards the
               right edge narrowed to what was left of the window and its contents wrapped
               (owner's report 2026-11-10). */
            width: max-content; max-width: calc(100vw - 12px); box-sizing: border-box;
            background: var(--vscode-editorWidget-background, #f3f3f3);
            color: var(--vscode-editorWidget-foreground, inherit);
            border: 1px solid var(--vscode-editorWidget-border, #c8c8c8);
            border-radius: 4px; box-shadow: 0 2px 8px rgba(0, 0, 0, 0.25);
            padding: 6px 8px; font-size: 12px; max-height: 60vh; overflow: auto;
        }
        .range-panel[hidden] { display: none; }
        /* Dragged anywhere but its inputs (owner's request 2026-10-29), its buttons and cells
           still clicked; nothing selects while it moves. */
        .range-panel { cursor: move; }
        .range-panel button, .range-panel input, .range-panel td { cursor: pointer; }
        .range-panel.moving, .range-panel.moving * { user-select: none; cursor: move; }
        /* The × at the top right (owner's request 2026-10-29), kept there as the panel scrolls. */
        .range-panel .panel-close {
            float: right; position: sticky; top: 0; z-index: 1; margin: -4px -6px 0 8px;
            width: 20px; height: 20px; padding: 0; line-height: 18px; text-align: center;
            font-size: 15px; border: none; border-radius: 3px; background: transparent; color: inherit; opacity: 0.7;
        }
        .range-panel .panel-close:hover { opacity: 1; background: rgba(128, 128, 128, 0.25); }
        /* The page's tooltip, in VS Code's hover look (the browser's title tip is too small). */
        .tip {
            position: fixed; z-index: 100; max-width: 320px; pointer-events: none;
            padding: 4px 8px; border-radius: 3px;
            font-family: var(--vscode-font-family, system-ui, sans-serif);
            font-size: var(--vscode-font-size, 13px); line-height: 1.4;
            background: var(--vscode-editorHoverWidget-background, #f3f3f3);
            color: var(--vscode-editorHoverWidget-foreground, #333);
            border: 1px solid var(--vscode-editorHoverWidget-border, #c8c8c8);
            box-shadow: 0 2px 8px rgba(0, 0, 0, 0.18);
            white-space: pre-line; overflow-wrap: anywhere;
        }
        .tip[hidden] { display: none; }
        /* The practice menu (owner's request 2026-10-25): the button opens it under itself; its
           rows are the tempo, the metronome and the parts. */
        /* An icon button as Range is (owner's request 2026-11-02), wider only while it shows the
           tempo it plays at. */
        .toolbar button.practice {
            width: auto; min-width: 42px; padding: 0 8px; gap: 3px;
            font-variant-numeric: tabular-nums; font-size: 12px;
        }
        .toolbar button.practice #practiceSpeed:empty { display: none; }
        /* Taller than 60vh it scrolls: the scroll bar's room kept in its width, or the bar ate into
           the rows and a sideways bar came too (the recording's parts, §8, made it that tall). */
        .practice-panel { scrollbar-gutter: stable; }
        .practice-panel .prow { display: flex; align-items: flex-start; gap: 10px; padding: 4px 2px; }
        .practice-panel .plbl { width: 72px; flex-shrink: 0; opacity: 0.75; font-weight: 600; padding-top: 3px; }
        .practice-panel button {
            font: inherit; padding: 2px 8px; border: 1px solid #c8c8c8; background: #fff; color: #333;
            cursor: pointer; border-radius: 3px;
        }
        .practice-panel .tempo { display: inline-flex; align-items: center; gap: 8px; }
        .practice-panel .tempo input[type="range"] { width: 160px; margin: 0; accent-color: #0078d7; }
        .practice-panel button:disabled { opacity: 0.45; cursor: default; }
        .practice-panel .tval { width: 3.2em; text-align: right; font-variant-numeric: tabular-nums; }
        .practice-panel button[aria-pressed="true"] {
            background: #d6e6f8; border-color: #0078d7; color: #0b4a8b;
        }
        /* A part sounds while its button is pressed in; one let out is silent, its name grey (no strike-through:
           the line sat off the letters' middle in VS Code's font — owner's report 2026-10-25). */
        .practice-panel .parts { display: flex; flex-direction: column; align-items: stretch; gap: 4px; }
        /* One line a part: the toggle, then its fader (owner's request 2026-11-09). */
        .practice-panel .parts .pline { display: flex; align-items: center; gap: 8px; }
        .practice-panel .parts .pline button { flex: 1 1 auto; min-width: 0; }
        /* The recording's parts under its line (docs/recording-design.md §8). */
        .practice-panel .rec-part-rows { display: flex; flex-direction: column; align-items: stretch; gap: 4px; }
        .practice-panel .rec-part-rows:empty { display: none; }
        /* The mixer (docs/transport-mixer-design.md §1 1-2): two groups, each with its head (its name, all
           on or off, its fader), then a line a part: the name, M, S, the fader. A line not heard (muted, or
           others soloed) is dimmed. */
        .mixer-panel .mgroup { padding: 4px 2px 8px; clear: both; }   /* (under the floated ×, not around it) */
        .mixer-panel .mgroup + .mgroup { border-top: 1px solid rgba(128, 128, 128, 0.35); padding-top: 8px; }
        .mixer-panel { min-width: min(400px, calc(100vw - 16px)); }
        .mixer-panel .mhead { display: flex; align-items: center; gap: 8px; margin-bottom: 6px; }
        .mixer-panel .mtitle { width: 6.5em; flex: 0 0 auto; }
        .mixer-panel .mhead > button.toggle:not(.msmall) { flex: 1 1 auto; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; text-align: left; }
        /* (the group's All, beside its on/off: as wide as its word) */
        .mixer-panel .mhead > button.toggle.mall { flex: 0 0 auto; }
        .mixer-panel .mhead .fader { width: 160px; flex: 0 0 auto; margin: 0; accent-color: #0078d7; cursor: pointer; }
        .mixer-panel .mhead .flevel { width: 2.8em; flex: 0 0 auto; text-align: right; font-size: 11px; opacity: 0.8; font-variant-numeric: tabular-nums; }
        .mixer-panel .parts { display: flex; flex-direction: column; align-items: stretch; gap: 4px; padding-left: 1.5em; }
        .mixer-panel .parts .pline button.pname { flex: 1 1 auto; min-width: 5em; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; text-align: left; }
        .mixer-panel .parts .pline button.msmall { flex: 0 0 auto; }
        .practice-panel button.msmall { padding: 2px 6px; min-width: 24px; font-weight: 600; }
        .practice-panel button.msolo[aria-pressed="true"] { background: #fff4c2; border-color: #c9a400; color: #6b5600; }
        .mixer-panel .pline.unheard button.pname { opacity: 0.5; }
        /* The recording's octave, under its group's head (folded too): the number between its − and +. */
        .mixer-panel .moctrow { padding-left: 1.5em; align-items: center; }
        .mixer-panel .moctrow .tempo { gap: 4px; }
        .mixer-panel #recOctValue { width: auto; min-width: 1.6em; text-align: center; }
        .mixer-panel #recPartsRow { margin-bottom: 4px; }
        .mixer-panel .mbody > .presets { padding-left: 1.5em; margin-top: 6px; }
        .mixer-panel .mbody > .presets .plbl { display: none; }
        /* Each group folded by its name (§6): the head stays, the body goes. */
        .mixer-panel .mgroup.folded > .mbody { display: none; }
        .mixer-panel .mgroup.folded > .mhead { margin-bottom: 0; }
        .mixer-panel button.mfold { border: none; background: transparent; color: inherit; padding: 2px 0; text-align: left; cursor: pointer; font-weight: 600; opacity: 0.85; }
        .mixer-panel button.mfold:hover { opacity: 1; }
        .mixer-panel button.mfold::before { content: '\\25BE\\00A0'; display: inline-block; width: 1em; }
        .mixer-panel .mgroup.folded button.mfold::before { content: '\\25B8\\00A0'; }
        .mixer-panel .mbody > .prow { padding-left: 1.5em; }
        /* (the rows' sliders take the room there is, so the panel needs no sideways scroll) */
        .mixer-panel .prow .tempo { flex: 1 1 auto; min-width: 0; }
        .mixer-panel .prow .tempo input[type="range"] { width: auto; flex: 1 1 100px; min-width: 70px; max-width: 160px; }
        /* Tempo's one line: its name (as a group's, with no fold), the slider in the room there is, the
           two toggles as wide as their words. */
        .mixer-panel .mlabel { font-weight: 600; opacity: 0.85; padding: 2px 0 2px 1em; box-sizing: border-box; }
        .mixer-panel .mhead.mtempo { margin-bottom: 0; flex-wrap: wrap; row-gap: 4px; }
        .mixer-panel .mhead.mtempo .fader { width: auto; flex: 1 1 70px; min-width: 70px; }
        .mixer-panel .mhead.mtempo > button.toggle { flex: 0 0 auto; white-space: nowrap; }
        /* Faders 100%: at the right of the row it stands in (placeFadersReset). */
        .mixer-panel #fadersReset { margin-left: auto; white-space: nowrap; }
        /* As long as the tempo's slider (owner's request 2026-11-09, session 1159: longer, the 200% end further right). */
        .practice-panel .parts input.fader { width: 160px; flex: 0 0 auto; margin: 0; accent-color: #0078d7; cursor: pointer; }
        .practice-panel .parts .flevel { width: 2.8em; flex: 0 0 auto; text-align: right; font-size: 11px; opacity: 0.8; font-variant-numeric: tabular-nums; }
        .practice-panel .presets { padding-top: 0; }
        .practice-panel .parts button { text-align: left; }
        .practice-panel .parts button[aria-pressed="false"] { color: #888; }
        /* A part the score does not sound (off unless pressed), and a sung voice. */
        .practice-panel .parts button.extra { font-style: italic; }
        /* Each voice's own microphone, the Play button's (owner's request 2026-11-10): its shape and its
           colours are the same rules, on the toggle's lower right. None prepared: grey, as not sung yet. */
        .practice-panel .parts button.voice { position: relative; padding-right: 16px; }
        .practice-panel .parts .voice-badge { right: -4px; bottom: -4px; }
        /* The recording's panel (docs/recording-design.md): its file name and what it is doing. */
        .practice-panel .rec-name { max-width: 220px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .practice-panel .rec-status { font-size: 11px; opacity: .8; padding-top: 4px; max-width: 300px; }
        .practice-panel .rec-status:empty { display: none; }
        .practice-panel input[type="range"]:disabled { opacity: .4; }
        .range-panel table { border-collapse: collapse; }
        .range-panel th { text-align: left; font-weight: 600; opacity: 0.75; padding: 2px 8px; }
        .range-panel td { padding: 2px 8px; white-space: nowrap; }
        .range-panel td.end { cursor: pointer; border-radius: 3px; }
        .range-panel td.end:hover { background: var(--vscode-list-hoverBackground, rgba(128, 128, 128, 0.2)); }
        .range-panel td.end.cur { background: var(--vscode-list-activeSelectionBackground, rgba(0, 120, 215, 0.25)); }
        .range-panel .pitch { font-weight: 600; }
        .range-panel .where { opacity: 0.7; }
        .range-panel .note { opacity: 0.7; padding: 4px 8px 0; }
        .range-panel td.tess { vertical-align: top; }
        .range-panel .tess-chart { display: block; margin-top: 2px; }
        .range-panel .tess-bar { fill: var(--vscode-charts-blue, #1a73c8); opacity: 0.35; }
        .range-panel .tess-bar.in { opacity: 1; }
        .range-panel .tess-line { fill: var(--vscode-charts-blue, #1a73c8); }
    </style>
</head>
<!-- The preview is a rendered score, not an editable text buffer, so VS Code's
     default webview context menu (Copy / Cut / Paste) has nothing to act on:
     Cut and Paste can never apply, and Copy only ever picked up stray SVG text.
     preventDefaultContextMenuItems drops those built-in entries, leaving only
     what this extension contributes under menus/webview/context. lysPlaying is
     one of the when-clause keys those items gate on; the contextmenu handler
     below refreshes them for each click. -->
<body data-vscode-context='{"preventDefaultContextMenuItems": true, "lysPlaying": false}'>
    <div class="toolbar">
        <!-- Each group wraps as one when the preview is narrow (owner's request 2026-10-25). -->
        <span class="group">
        <label for="renderSelect">Score:</label>
        <select id="renderSelect">
            <option value="">(Default)</option>
        </select>
        <button id="exportBtn" class="icon" type="button" aria-label="Export" title="Export…"><svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M2.5 9.5v4h11v-4"/><path d="M8 2v8.5"/><path d="M4.8 5.2 8 2l3.2 3.2"/></svg></button>
        </span>
        <span class="sep"></span>
        <span class="group">
        <button id="aiTransformBtn" class="ai icon" type="button" title="Ask AI in Chat (Ctrl+I)">✨</button>
        <button id="rangeBtn" class="toggle icon" type="button" aria-pressed="false" aria-label="Range" title="Each part's range"><svg viewBox="0 -.25 16.5 16.5" fill="currentColor" aria-hidden="true"><path d="M3 1.5 5.95 5.3H3.8v6.6h2.15L3 15.7.05 11.9H2.2V5.3H.05z"/><g transform="translate(.8 0)"><ellipse cx="9.4" cy="12.4" rx="2.8" ry="2" transform="rotate(-22 9.4 12.4)"/><rect x="11.35" y="1.5" width="1.05" height="10.8"/><path d="M12.4 1.5c.3 2 1.6 2.8 2.4 3.9.9 1.5 1.1 3.6.3 5.5.1-2.1-.9-3.6-2.7-4.2z"/></g></svg></button>
        </span>
        <span class="sep"></span>
        <span class="group">
        <!-- The voice badge (owner's request 2026-11-08): a small microphone over the corner of ▶ —
             hidden until the score has a sung voice; then preparing (grey, pulsing), ready, or
             failed, its title naming the parts. Not a button (the owner's first look at a ♪ button
             beside ⏹: "looks like a button but is not one; a note does not say singing, the
             instruments are notes too"): a status over the Play it speaks of, drawn as what sings.
             A click still opens the practice menu, where each voice has its own toggle and sign. -->
        <!-- Rewind (owner's request 2026-11-17): the A flag back to the top. -->
        <button id="rewindBtn" type="button" title="The A flag back to the top (playing: plays from there)">⏮</button>
        <span class="play-wrap">
        <button id="playBtn" type="button" title="Play from the A flag (Space while stopped)">▶</button>
        <span id="voiceBadge" class="voice-badge" role="status" hidden aria-label="Voice"><svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="6" y="1.2" width="4" height="7.6" rx="2"/><path d="M3.6 7.4a4.4 4.4 0 0 0 8.8 0"/><path d="M8 11.8v2.6M5.6 14.4h4.8"/></svg></span>
        <span id="voiceBar" class="voice-bar" hidden aria-hidden="true"><span></span></span>
        </span>
        <!-- Pause (docs/transport-mixer-design.md §1 3): where it plays, held; pressed again, on from there. -->
        <button id="pauseBtn" class="toggle icon" type="button" aria-pressed="false" title="Pause — pressed again, go on from there (Space)" aria-label="Pause" disabled><svg viewBox="0 0 16 16" fill="currentColor" aria-hidden="true"><rect x="3" y="2.5" width="3.6" height="11" rx=".6"/><rect x="9.4" y="2.5" width="3.6" height="11" rx=".6"/></svg></button>
        <button id="stopBtn" type="button" title="Stop, back to the A flag" disabled>⏹</button>
        <!-- Repeat (owner's decision 2026-11-19: here, the practice panel's Loop group gone). -->
        <button id="loopBtn" class="toggle icon" type="button" aria-pressed="false" aria-label="Repeat" title="Repeat: play round and round — the bars A to B when A-B is on, else the whole score as it plays"><svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M2.8 7.2V6.4a2.2 2.2 0 0 1 2.2-2.2h7.6"/><path d="M10.4 2l2.2 2.2-2.2 2.2"/><path d="M13.2 8.8v.8a2.2 2.2 0 0 1-2.2 2.2H3.4"/><path d="M5.6 14l-2.2-2.2 2.2-2.2"/></svg></button>
        <button id="practiceBtn" class="toggle icon practice" type="button" aria-pressed="false" aria-haspopup="true" aria-expanded="false" aria-label="Practice and mixer" title="Practice and mixer: tempo, the parts, the recording"><svg viewBox="0 0 16 16" fill="currentColor" aria-hidden="true"><path fill-rule="evenodd" d="M3.6 14.8 6.3 1.4h3.4l2.7 13.4zM7.3 3 5.9 10.2h4.2L8.7 3z"/><path d="M7.7 11.2 12.4 3.9l1 .65-4.7 7.3z"/></svg><span id="practiceSpeed"></span></button>
        <!-- The bar over the time (owner's request 2026-11-19): "Bar 12 / 118" over "0:21 / 4:38". -->
        <span class="where"><span id="barNow" class="length" title="The bar playing — stopped, the A flag's bar, where Play starts" hidden></span><span id="barSlash" class="length" hidden>/</span><span id="barLast" class="length" title="The last bar printed" hidden></span><span id="posInfo" class="length" title="Where the playback stands, in the score's time" hidden></span><span id="posSlash" class="length" hidden>/</span><span id="lengthInfo" class="length" title="Length, as played"></span></span>
        </span>
        <span class="sep"></span>
        <span class="group">
        <button id="firstPageBtn" type="button" title="First page">⏮</button>
        <!-- No min/max: the spinner is inverted in script (down = next page), so
             the native step must be free to go past either end (see pageInput). -->
        <input id="pageInput" type="number" value="1" title="Page" style="width: calc(1ch + 22px)">
        <span id="pageCount">/ 1</span>
        <button id="lastPageBtn" type="button" title="Last page">⏭</button>
        </span>
    </div>
    <div id="errorBanner" class="error-banner" role="alert"></div>
    <button id="followBtn" class="follow-btn" type="button" title="Scroll back to the notes sounding, and follow them again" hidden>↓ Follow</button>
    <div id="rangePanel" class="range-panel" hidden><button type="button" id="rangeClose" class="panel-close" title="Close" aria-label="Close">×</button><div id="rangeBody"></div></div>
    <!-- Practice and the mixer in one panel (docs/transport-mixer-design.md §6; owner's decision 2026-11-17): a group a
         heading; Score and Recording folded or opened by their names (remembered), their heads — the on/off,
         All, the fader — usable folded. -->
    <div id="practicePanel" class="range-panel practice-panel mixer-panel" hidden>
        <button type="button" id="practiceClose" class="panel-close" title="Close" aria-label="Close">×</button>
        <!-- Tempo: one line, never folded (owner's decision 2026-11-19: folded, the line showed all of it). -->
        <div class="mgroup">
            <div class="mhead mtempo"><span class="mtitle mlabel">Tempo</span><input type="range" id="tempoSlider" class="fader" min="50" max="150" step="5" value="100" aria-label="Tempo"><span id="tempoValue" class="flevel">100%</span><button type="button" id="clickBtn" class="toggle" aria-pressed="false" title="A click on every beat, the bar's first louder">Metronome</button><button type="button" id="countInBtn" class="toggle" aria-pressed="false" title="One bar of clicks before the music starts">Count-in</button></div>
        </div>
        <div class="mgroup" data-fold="score">
            <div class="mhead"><button type="button" class="mfold mtitle" aria-expanded="true">Score</button><button type="button" id="scoreOnBtn" class="toggle" aria-pressed="true" title="The score sounds: the parts turned on below — press to silence it, each part's own on or off kept">Play</button><button type="button" id="scoreAllBtn" class="toggle mall" aria-pressed="true" title="Every part of the score on, as the score has them — or every one off">All</button><input type="range" id="scoreLevelSlider" class="fader" min="0" max="200" step="1" value="100" aria-label="Score volume"><span id="scoreLevelValue" class="flevel"></span></div>
            <div class="mbody">
                <span id="partsList" class="parts"></span>
                <div class="prow presets"><span class="plbl"></span><span class="tempo"><button type="button" id="partsReset" title="The parts the selected score plays">As scored</button><button type="button" id="fadersReset" title="Every fader to 100%: the score's and the recording's">Faders 100%</button></span></div>
            </div>
        </div>
        <div class="mgroup" id="mixRecGroup" data-fold="rec" hidden>
            <div class="mhead"><button type="button" class="mfold mtitle" aria-expanded="true">Recording</button><button type="button" id="recOnBtn" class="toggle rec-name" aria-pressed="false"><span id="recName"></span></button><button type="button" id="recAllBtn" class="toggle mall" aria-pressed="true" title="Every part of the recording on — or every one off" hidden>All</button><input type="range" id="recLevelSlider" class="fader" min="0" max="200" step="1" value="100"><span id="recLevelValue" class="flevel"></span></div>
            <div class="mbody">
                <div id="recPartsRow" hidden><span id="recPartsRows" class="parts rec-part-rows"></span></div>
            </div>
            <!-- (outside the fold: Recording folded hides its parts; its octave and its status stay — owner's
                 request 2026-11-17. The octave alone is left of Speed and pitch — owner's decision 2026-11-19:
                 the tempo is the Tempo group's, and the file's own tuning is not used.) -->
            <div class="prow moctrow" id="recOctRow"><span class="plbl">Octave</span><span class="tempo"><button type="button" id="recOctDown" title="An octave down">−</button><span id="recOctValue" class="tval">0</span><button type="button" id="recOctUp" title="An octave up">+</button></span></div>
            <div id="recStatus" class="rec-status" role="status"></div>
        </div>
    </div>
    <div class="main-content">
        <div class="container">
            <div id="svgContainer">
                <div class="loading">Loading preview...</div>
            </div>
        </div>
    </div>
    <div class="zoom-info" id="zoomInfo">100%</div>
    <script nonce="${nonce}">
        const vscode = acquireVsCodeApi();
        // Boot beacon + error relay: the FIRST statements, so the extension's
        // output channel shows whether this script ran at all, and any later
        // runtime error lands in the same log instead of a hidden devtools.
        vscode.postMessage({ type: 'webviewBoot' });
        window.addEventListener('error', (e) => {
            vscode.postMessage({ type: 'webviewError', message: String(e.message), line: e.lineno });
        });
        const HIGHLIGHT_THRESHOLD = ${HIGHLIGHT_DISTANCE_THRESHOLD};

        let scale = 1;
        // Each file's zoom and scroll while the follower shows another (keyed by URI), and
        // the one to put back when the file it turned to draws.
        const viewByFile = new Map();
        let pendingView = null;
        const minScale = 0.25;
        const maxScale = 4;
        const scaleStep = 0.1;

        const svgContainer = document.getElementById('svgContainer');
        const errorBanner = document.getElementById('errorBanner');
        const zoomInfo = document.getElementById('zoomInfo');
        const renderSelect = document.getElementById('renderSelect');

        // --- M3: select a note range on the score to drive the AI transform. A plain
        // click sets the anchor (and still jumps the editor); shift-click extends the
        // selection from the anchor. The "Transform with AI" action then maps the
        // selected source offsets to a text range in the editor (§6). ---
        const aiTransformBtn = document.getElementById('aiTransformBtn');
        let aiRangeLo = -1, aiRangeHi = -1;   // current score selection (source offsets)
        function aiClearSelection() {
            aiRangeLo = -1; aiRangeHi = -1;
            lightSelectedBars();
            askStoppedPosition();
        }
        function aiSetSelection(a, b) {
            aiRangeLo = Math.min(a, b);
            aiRangeHi = Math.max(a, b);
            // The selection lights its BARS, not its notes (owner's request 2026-10-25): the
            // editor's text is not selected, so lit notes would read as a selection there.
            clearHighlights();
            lastHighlightRanges = null;
            lastHighlightPos = -1;
            lightSelectedBars();
            askStoppedPosition();   // the header's position: the selection's start while stopped
        }
        // Every bar whose written stretch (data-span) meets the selection [lo, hi] is lit; none
        // when there is no selection. Re-run after a render replaces the boxes.
        function lightSelectedBars() {
            for (const r of svgContainer.querySelectorAll('rect.bar-staff.selected')) r.classList.remove('selected');
            if (aiRangeLo >= 0) {
                for (const r of svgContainer.querySelectorAll('rect.bar-staff[data-span]')) {
                    const span = r.getAttribute('data-span').split(' ').map(Number);
                    if (span.length === 2 && span[0] <= aiRangeHi && span[1] > aiRangeLo) r.classList.add('selected');
                }
            }
            updateAiButton();
        }
        // What the selection is, for the head of the AI's input box (owner's request 2026-10-25:
        // "N bars selected", as "Nothing is selected" heads it without one). Bars are counted by
        // their box (rect.bar-box, one per bar of a system), so a bar of several staves counts once.
        function selectedLabel() {
            const boxes = Array.from(svgContainer.querySelectorAll('rect.bar-box'));
            const bars = new Set();
            for (const r of svgContainer.querySelectorAll('rect.bar-staff.selected')) {
                const b = r.getBoundingClientRect();
                const cx = (b.left + b.right) / 2, cy = (b.top + b.bottom) / 2;
                const box = boxes.find(x => {
                    const c = x.getBoundingClientRect();
                    return cx >= c.left && cx <= c.right && cy >= c.top && cy <= c.bottom;
                });
                bars.add(box ? box : r);
            }
            const n = bars.size;
            return n === 0 ? 'The selection' : n === 1 ? '1 bar selected' : n + ' bars selected';
        }
        // The header's AI button is its emoji alone, "✨" (owner's request 2026-10-25); its tooltip
        // names it in a few words, and with bars selected says they go with the request. It opens
        // Copilot's chat, as Ctrl+I does (owner's requests 2026-10-29).
        function updateAiButton() {
            aiTransformBtn.title = aiRangeLo < 0
                ? 'Ask AI in Chat (Ctrl+I)'
                : 'Ask AI in Chat, about the selected bars (Ctrl+I)';
        }
        function askInChat() {
            // Held sunk a moment, so a quick click is seen to land.
            aiTransformBtn.classList.add('pressed');
            setTimeout(() => aiTransformBtn.classList.remove('pressed'), 180);
            vscode.postMessage({ type: 'askAiInChat', startPos: aiRangeLo, endPos: aiRangeHi });
        }
        aiTransformBtn.addEventListener('click', askInChat);

        function showErrorBanner(text) {
            errorBanner.textContent = text;
            errorBanner.classList.add('visible');
        }
        function hideErrorBanner() {
            errorBanner.classList.remove('visible');
            errorBanner.textContent = '';
        }
        let hideTimeout;
        let lastHighlightPos = -1;
        let lastHighlightTokenStart = -1; // start of the caret's token, so re-highlight after a render keeps the containment guard
        let lastHighlightRanges = null;   // set while a selection (not a bare cursor) is highlighted

        // Pages of the current SVG (single-page SVGs have no g.page wrappers).
        let pages = [{ top: 0, height: 0 }];
        let svgWidthPx = 0;   // the SVG's natural width in px (width attribute)
        let pxPerSpace = 10;  // px per staff-space (width / viewBox width)
        // 'width' = keep the score fitted to the pane; null = manual zoom.
        // Fit-to-width is the state a freshly opened preview starts in, so the
        // first render (and every resize after it) fits without being asked.
        let fitMode = 'width';
        const mainContent = document.querySelector('.main-content');
        const pageInput = document.getElementById('pageInput');
        const pageCount = document.getElementById('pageCount');
        // The page number last put in the box: the base the inverted spinner
        // steps from (the box's own value has already moved by then).
        let shownPageNumber = 1;
        function showPageNumber(n) {
            shownPageNumber = n;
            pageInput.value = String(n);
        }

        // Zoom by resizing the SVG element (not CSS transform): real layout
        // size keeps scrollbars, centering and page-scroll math correct.
        function updateZoom() {
            const svg = svgContainer.querySelector('svg');
            if (svg && svgWidthPx > 0) {
                svg.style.width = (svgWidthPx * scale) + 'px';
                svg.style.height = 'auto';
            }
            zoomInfo.textContent = Math.round(scale * 100) + '%';
            zoomInfo.classList.add('visible');
            clearTimeout(hideTimeout);
            hideTimeout = setTimeout(() => {
                zoomInfo.classList.remove('visible');
            }, 1500);
            updatePageInfo();
        }

        function collectPages() {
            const svg = svgContainer.querySelector('svg');
            pages = [{ top: 0, height: 0 }];
            svgWidthPx = 0;
            if (!svg) { updatePageInfo(); return; }
            const vb = (svg.getAttribute('viewBox') || '0 0 1 1').split(/ +/).map(Number);
            const vbW = vb[2] || 1, vbH = vb[3] || 1;
            svgWidthPx = parseFloat(svg.getAttribute('width')) || svg.clientWidth || 1;
            pxPerSpace = svgWidthPx / vbW;
            const gs = svg.querySelectorAll(':scope > g.page');
            pages = gs.length > 0
                ? Array.from(gs).map(g => ({
                    top: parseFloat(g.getAttribute('data-page-top')) || 0,
                    height: parseFloat(g.getAttribute('data-page-height')) || vbH
                }))
                : [{ top: 0, height: vbH }];
            updatePageInfo();
        }

        function currentPageIndex() {
            const st = mainContent.scrollTop - 10;
            let idx = 0;
            for (let i = 0; i < pages.length; i++) {
                if (pages[i].top * pxPerSpace * scale <= st + 1) idx = i;
            }
            return idx;
        }

        function updatePageInfo() {
            const n = pages.length;
            pageCount.textContent = '/ ' + n;
            // As narrow as the widest page number allows: its digits, the
            // padding and border (7px), and Chromium's spinner (about 15px).
            pageInput.style.width = 'calc(' + String(n).length + 'ch + 22px)';
            // Scrolling reports the page, but not over a number the user is
            // still typing: a focused box keeps its text until change/blur.
            if (document.activeElement !== pageInput) {
                showPageNumber(currentPageIndex() + 1);
            }
            const single = n < 2;
            pageInput.disabled = single;
            for (const id of ['firstPageBtn', 'lastPageBtn']) {
                document.getElementById(id).disabled = single;
            }
        }

        function gotoPage(i) {
            i = Math.max(0, Math.min(pages.length - 1, i));
            mainContent.scrollTop = i === 0 ? 0 : pages[i].top * pxPerSpace * scale + 20;
            updatePageInfo();
        }

        function availSize() {
            return { w: mainContent.clientWidth - 44, h: mainContent.clientHeight - 44 };
        }

        function fitWidth() {
            fitMode = 'width';
            const a = availSize();
            if (svgWidthPx > 0) scale = a.w / svgWidthPx;
            updateZoom();
        }

        function isDarkTheme() {
            return document.documentElement.classList.contains('theme-dark');
        }

        function getHighlightColor() {
            return isDarkTheme() ? '#00ccff' : '#ff6600';
        }

        // ---- color scheme ---------------------------------------------------
        // lilysharp.preview.theme, resolved onto <html class="theme-dark"> (the head
        // script set it once before paint; this keeps it current). 'auto' follows the
        // editor theme, which the webview reports through prefers-color-scheme.
        let themeMode = '${theme}';
        const darkScheme = window.matchMedia('(prefers-color-scheme: dark)');
        function applyTheme(mode) {
            if (mode === 'light' || mode === 'dark' || mode === 'auto') themeMode = mode;
            const dark = themeMode === 'dark' || (themeMode === 'auto' && darkScheme.matches);
            document.documentElement.classList.toggle('theme-dark', dark);
        }
        darkScheme.addEventListener('change', () => applyTheme(themeMode));

        // Put an attribute back to the value the SVG shipped with: re-apply the
        // saved string, or drop the attribute entirely if it never had one.
        // Removing it blindly is wrong — many grobs carry an explicit paint
        // (the section mark's box is fill="#FFFFFF", its label fill="#000000"),
        // and dropping it falls back to SVG's default black, which the dark-mode
        // invert() filter then renders as a solid white blob that never reverts.
        function restoreAttr(el, name, orig) {
            if (orig === null || orig === undefined) {
                el.removeAttribute(name);
            } else {
                el.setAttribute(name, orig);
            }
        }

        // ---- page-level update -----------------------------------------------
        // The score arrives as ONE SVG string on every render, and replacing the
        // whole of it (innerHTML) parses, lays out and paints every page in the
        // editor's own window. Measured on a five-page song: 150 ms to parse,
        // 230 ms to lay out, 80 ms to paint — half a second between a keystroke
        // and the caret whenever the preview was open (2026-09-04, owner report).
        // A keystroke changes one page, so the pages are compared as STRINGS and
        // only the changed ones are parsed again (37 ms for one page of that
        // song). A page whose drawing is the same but whose source offsets moved
        // — every element after the edit carries a new data-pos — keeps its DOM
        // and has the offsets re-stamped from the new markup, in document order
        // (8 ms). Anything this cannot account for — a different page count, a
        // changed frame around the pages, an element count that does not match
        // — falls back to the whole replacement, so the picture can never differ
        // from what innerHTML would have shown.
        const PAGE_OPEN = '<g class="page" data-page-top=';
        // A source attribute with its VALUE blanked and its NAME kept: two pages
        // that agree on this carry the attributes on the same elements in the
        // same order, which is what lets the values be re-stamped by index.
        // ⚠️ This script is the body of a TypeScript template literal, so a regex
        // backslash has to be written DOUBLED here to survive into the page: a
        // single one is the template's own escape and vanishes (a \\s written
        // singly became an s, and a \\/ a /, which ended the regex early and took
        // the whole script down — the preview stayed on "Loading", 2026-09-04).
        const SOURCE_ATTRS = /\\s(data-pos|data-alt|data-start|data-span)="[^"]*"/g;
        // The elements that carry one: a grob's data-pos, a barline's data-alt list, and a bar's
        // written place (rect.bar-staff's data-start, which a click on the bar goes to).
        // A bar's written stretch (data-span, a pair: the caret in it lights the bar) rides with it.
        const SOURCE_SELECTOR = '[data-pos], [data-alt], [data-start], [data-span]';
        let shownPages = null;      // per page: { markup, blank } of what is on screen
        let shownFrame = null;      // the markup around the pages (svg tag, style, closing)

        function forgetShownPages() { shownPages = null; shownFrame = null; }

        // The SVG string as [head, page, page, …, tail]: each page runs from its
        // opening tag to the next page's (or to </svg>), so the closing </g> and
        // the whitespace after it belong to it. null when the shape is not that.
        function splitPages(svg) {
            const close = svg.lastIndexOf('</svg>');
            const first = svg.indexOf(PAGE_OPEN);
            if (first < 0 || close < 0 || first > close) return null;
            const pages = [];
            for (let at = first; at >= 0 && at < close;) {
                const next = svg.indexOf(PAGE_OPEN, at + PAGE_OPEN.length);
                const end = next < 0 || next > close ? close : next;
                pages.push(svg.slice(at, end));
                at = next;
            }
            return { head: svg.slice(0, first), pages, tail: svg.slice(close) };
        }

        // Re-stamps one page's source offsets from the markup, whose blanked form
        // the caller has found equal to the page's: the source attributes then sit
        // on the same elements, in the same order, under the same names, so one
        // pass over the markup's attributes and one over the elements that carry
        // any line up one to one. false when they do not — a count that differs —
        // and the caller replaces the page instead.
        function restampPage(page, markup) {
            const els = page.querySelectorAll(SOURCE_SELECTOR);
            const attrs = /\\sdata-(pos|alt|start|span)="([^"]*)"/g;
            for (let k = 0; k < els.length; k++) {
                const el = els[k];
                const carried = (el.hasAttribute('data-pos') ? 1 : 0) + (el.hasAttribute('data-alt') ? 1 : 0)
                    + (el.hasAttribute('data-start') ? 1 : 0) + (el.hasAttribute('data-span') ? 1 : 0);
                for (let j = 0; j < carried; j++) {
                    const m = attrs.exec(markup);
                    if (!m) return false;
                    el.setAttribute('data-' + m[1], m[2]);
                }
            }
            return attrs.exec(markup) === null;
        }

        // A page's source offsets as drawn, in document order, against the markup the page
        // was just brought up to — what the group reconcile promises (a kept or carried group
        // re-stamped, a parsed one fresh). Reading them back costs one pass over the page's
        // offset-carrying elements, far less than the parse the reconcile saves, and it is
        // the one place a reconcile that left an offset stale can be seen: the caret and the
        // selection light whatever note carries the offset, so a stale one lights the wrong
        // note (owner report 2026-10-11: selecting a d lit the e three notes on, until the
        // preview was reopened — the server's pages were right, measured edit by edit).
        // null when they agree, else what differed first, for the output channel.
        function offsetsDisagree(page, markup) {
            const drawn = [];
            for (const el of page.querySelectorAll(SOURCE_SELECTOR)) {
                for (const a of el.attributes) {
                    if (a.name === 'data-pos' || a.name === 'data-alt' || a.name === 'data-start' || a.name === 'data-span') drawn.push(a.name + '=' + a.value);
                }
            }
            const attrs = /\\sdata-(pos|alt|start|span)="([^"]*)"/g;
            let k = 0;
            for (let m; (m = attrs.exec(markup)); k++) {
                const want = 'data-' + m[1] + '=' + m[2];
                if (drawn[k] !== want) return 'offset #' + k + ' drawn ' + (drawn[k] || 'none') + ', markup ' + want;
            }
            return k === drawn.length ? null : drawn.length + ' offsets drawn, ' + k + ' in the markup';
        }

        // After a reconcile: every changed page's offsets read back against its markup, and
        // a page that disagrees replaced from its markup — said loudly in the account, so the
        // reconcile's slip is found from the output channel the next time it happens.
        function recheckOffsets(live, livePages, markups, changed) {
            let note = '';
            for (const i of changed) {
                const why = offsetsDisagree(livePages[i], markups[i]);
                if (!why) continue;
                const fresh = parseOne(markups[i]);
                if (!fresh) return note + '; STALE OFFSETS on page ' + i + ' (' + why + '), and it did not parse alone';
                live.replaceChild(fresh, livePages[i]);
                shownPages[i] = { markup: markups[i], blank: markups[i].replace(SOURCE_ATTRS, ' $1') };
                note += '; STALE OFFSETS on page ' + i + ' after the reconcile (' + why + ') - page replaced';
            }
            return note;
        }

        // Inside a page the renderer puts each SYSTEM, and then the page's overlays
        // (slurs, lyrics, dynamics, marks — drawn after every system), in a labeled
        // group of its own (SharedRenderer, BeginLabeledGroup — interactive SVG
        // only). A page whose drawing changed is therefore compared group by
        // group, and only the group the edit touched is parsed again: a system
        // is a few kilobytes where the page is a hundred.
        const CHUNK_OPEN = /<g class="(system|overlay)">/g;
        const G_TAGS = /<g[\\s>]|<\\/g>/g;
        // A system's FRAME: the renderer draws each system in its own frame and puts it
        // where it sits with the first tag inside the group, <g transform="translate(0,Y)
        // scale(1,1)"> (IDocumentContext.SystemLocalFrames). A system that moved down
        // the page is therefore the same text under another transform, and the preview
        // sets the attribute instead of parsing the system again. The frame tag is
        // matched right after the group's open tag only — nested groups (an ossia's
        // scale) carry transforms of their own that must not be touched.
        const FRAME_TAG = /^(<g class="system">\\s*<g)( transform="[^"]*")?(>)/;
        // The chunk with its frame transform blanked, and the transform itself.
        function unframe(markup) {
            const m = FRAME_TAG.exec(markup);
            if (!m) return { text: markup, transform: null };
            return {
                text: markup.slice(0, m[1].length) + '>' + markup.slice(m[0].length),
                transform: m[2] ? m[2].slice(' transform="'.length, -1) : ''
            };
        }
        function setFrameTransform(group, transform) {
            const frame = group.firstElementChild;
            if (!frame || frame.tagName.toLowerCase() !== 'g') return false;
            if (transform) frame.setAttribute('transform', transform);
            else frame.removeAttribute('transform');
            return true;
        }

        // A page's markup as its labeled groups and the FRAME around and between
        // them — the page's own tag, the header texts, the margin group's tags,
        // the whitespace. null when a group has no close, which no page of the
        // renderer's has.
        function splitChunks(markup) {
            const chunks = [];
            let frame = '';
            let head = null, gapsBlank = true;
            let at = 0;
            CHUNK_OPEN.lastIndex = 0;
            for (let open; (open = CHUNK_OPEN.exec(markup));) {
                G_TAGS.lastIndex = open.index;
                let depth = 0, end = -1;
                for (let tag; (tag = G_TAGS.exec(markup));) {
                    depth += tag[0] === '</g>' ? -1 : 1;
                    if (depth === 0) { end = tag.index + tag[0].length; break; }
                }
                if (end < 0) return null;
                const gap = markup.slice(at, open.index);
                if (head === null) head = gap; else if (gap.trim() !== '') gapsBlank = false;
                frame += gap;
                chunks.push({ label: open[1], markup: markup.slice(open.index, end) });
                at = end;
                CHUNK_OPEN.lastIndex = end;
            }
            const tail = markup.slice(at);
            frame += tail;
            // head/tail: the frame BEFORE the first group and AFTER the last one, for a
            // comparison that does not count the groups (reflowPages); gapsBlank says the
            // text between groups is whitespace only, so head + groups + tail is the page.
            return { frame, chunks, head: head === null ? frame : head, tail, gapsBlank };
        }

        // A fresh element parsed from one group's or one page's markup, or null
        // when the markup is not exactly one element.
        function parseOne(markup) {
            const holder = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            holder.innerHTML = markup;
            const fresh = holder.firstElementChild;
            return fresh && fresh === holder.lastElementChild ? fresh : null;
        }

        // Brings every page whose drawing changed up to the markup, group by group and
        // ACROSS the pages: the live groups of all those pages go into one pool keyed
        // by content (frame transform and source offsets blanked), and each group of
        // the new markup takes its match from the pool — the same page's same slot
        // when that is a match, else wherever it is — moved into place with the
        // transform set and the offsets re-stamped. Only a group with no match (the
        // edited system, a page's overlays) is parsed.
        // ⚠️ ONE POOL FOR ALL CHANGED PAGES, not one per page: when the page breaker
        // re-cuts the pages, a page that handed one system on and took one in keeps
        // its group COUNT, and a per-page, slot-by-slot comparison of it parsed every
        // one of its systems while the system it handed on was never in any pool —
        // the editor's own line said "carried 0, parsed 4" for exactly that keystroke
        // (2026-09-04; no backticks in this script — it is a template literal). The
        // result names what it did; a "fail" says why the pages
        // cannot be accounted for (a frame that differs beyond its offsets, a group
        // whose class does not match), and the caller then replaces those pages
        // whole, which is safe after a partial reconcile because only they took part.
        function reflowPages(livePages, split, failed) {
            const pool = new Map(); // blank -> [{ node, prev }] of the failed pages' live groups
            for (const i of failed) {
                const shown = shownPages[i];
                const before = shown.split || (shown.split = splitChunks(shown.markup));
                if (!before) return { fail: 'page ' + i + ' has no group split' };
                const nodes = livePages[i].querySelectorAll('g.system, g.overlay');
                if (nodes.length !== before.chunks.length)
                    return { fail: 'page ' + i + ' shows ' + nodes.length + ' groups, the record says ' + before.chunks.length };
                for (let k = 0; k < nodes.length; k++) {
                    const was = before.chunks[k];
                    const prev = was.unframed || (was.unframed = unframe(was.markup));
                    const blank = was.blank || (was.blank = prev.text.replace(SOURCE_ATTRS, ' $1'));
                    let list = pool.get(blank);
                    if (!list) pool.set(blank, list = []);
                    // stale: the page's offsets were shifted in place (applyPages) since
                    // this record was made, so its text no longer says which offsets
                    // the live group carries — a carried group from it is always
                    // re-stamped. Its blank form is unaffected (offsets blanked).
                    list.push({ node: nodes[k], prev, label: was.label, page: i, index: k, blank, stale: !!shown.stale });
                }
            }
            let carried = 0, parsed = 0, moved = 0, kept = 0;
            let why = '';
            // When a SYSTEM finds no match, say what the nearest unclaimed system in the
            // pool differs by — the first differing character with its neighbourhood —
            // so a mismatch is diagnosed from the output channel, not guessed at.
            let explained = 0;
            function explain(i, k, blank) {
                if (explained >= 3) return;
                let best = null, bestAt = -1;
                for (const list of pool.values())
                    for (const e of list) {
                        if (e.label !== 'system') continue;
                        let n = 0;
                        const lim = Math.min(e.blank.length, blank.length);
                        while (n < lim && e.blank[n] === blank[n]) n++;
                        if (n > bestAt) { bestAt = n; best = e; }
                    }
                if (!best) { explained++; why += (why ? ' | ' : '') + 'no unclaimed system in the pool'; return; }
                // Its own old self in the same slot is the EDITED system — expected to
                // differ, not worth a line. Anything else is a system that should have
                // matched somewhere and did not.
                if (best.page === i && best.index === k) return;
                explained++;
                const ctx = s => JSON.stringify(s.slice(Math.max(0, bestAt - 30), bestAt + 40));
                why += (why ? ' | ' : '') + 'a system on page ' + i + ' differs from page ' + best.page
                    + ' group ' + best.index + ' at ' + bestAt + '/' + blank.length + ' vs ' + best.blank.length
                    + ': new ' + ctx(blank) + ' old ' + ctx(best.blank);
            }
            for (const i of failed) {
                const markup = split.pages[i];
                const pieces = splitChunks(markup);
                const before = shownPages[i].split;
                if (!pieces) return { fail: 'page ' + i + ' does not split' };
                // The frame without its groups: the group COUNT is what changed here, so
                // the frame as a whole differs by the whitespace between groups; what must
                // agree is the text before the first group and after the last, offsets
                // blanked, with nothing but whitespace between the groups on either side.
                if (!pieces.gapsBlank || !before.gapsBlank
                    || pieces.head.replace(SOURCE_ATTRS, ' $1') !== before.head.replace(SOURCE_ATTRS, ' $1')
                    || pieces.tail !== before.tail)
                    return { fail: 'page ' + i + ' frame differs beyond its offsets' };
                const page = livePages[i];
                const existing = Array.from(page.querySelectorAll('g.system, g.overlay'));
                // The groups' parent: the margin group when there is one, else the page.
                const parent = existing.length ? existing[0].parentNode
                    : (page.querySelector(':scope > g:not(.system):not(.overlay)') || page);
                const anchor = existing.length ? existing[existing.length - 1].nextSibling : null;
                const desired = [];
                for (let k = 0; k < pieces.chunks.length; k++) {
                    const chunk = pieces.chunks[k];
                    const next = unframe(chunk.markup);
                    const blank = next.text.replace(SOURCE_ATTRS, ' $1');
                    chunk.blank = blank; chunk.unframed = next;
                    const list = pool.get(blank);
                    let node = null;
                    if (list && list.length) {
                        // The same page's same slot first — the common case is a page
                        // whose systems all stayed where they were — else any match.
                        let at = list.findIndex(e => e.page === i && e.index === k);
                        if (at < 0) at = 0;
                        const hit = list.splice(at, 1)[0];
                        if (hit.label !== chunk.label) return { fail: 'page ' + i + ': a ' + hit.label + ' matched a ' + chunk.label };
                        node = hit.node;
                        const transformed = next.transform !== hit.prev.transform;
                        if (transformed && !setFrameTransform(node, next.transform))
                            return { fail: 'page ' + i + ': a carried system has no frame' };
                        if ((hit.stale || next.text !== hit.prev.text) && !restampPage(node, chunk.markup))
                            return { fail: 'page ' + i + ': a carried group could not be re-stamped' };
                        if (node.parentNode !== parent) carried++;
                        else if (transformed) moved++;
                        else kept++;
                    } else {
                        if (chunk.label === 'system') explain(i, k, blank);
                        node = parseOne(chunk.markup);
                        if (!node) return { fail: 'page ' + i + ': a group did not parse' };
                        parsed++;
                    }
                    desired.push(node);
                }
                // Put the page's groups in the markup's order (inserting each before the
                // same anchor keeps them in sequence, and moves a node that is already
                // there), then drop the live groups the markup no longer has — they are
                // either carried elsewhere already or gone.
                const keep = new Set(desired);
                for (const node of desired) parent.insertBefore(node, anchor);
                for (const node of existing) if (!keep.has(node) && node.parentNode === parent) parent.removeChild(node);
                // The frame's own offsets (the first page's header): a whole-page
                // re-stamp is exact now that the DOM order is the markup's.
                if ((shownPages[i].stale || pieces.head !== before.head) && !restampPage(page, markup))
                    return { fail: 'page ' + i + ': the frame could not be re-stamped' };
                shownPages[i] = { markup, blank: markup.replace(SOURCE_ATTRS, ' $1'), split: pieces };
            }
            return { carried, parsed, moved, kept, why };
        }

        // Shows the svg: the pages that changed are replaced — group by group
        // where the page's groups line up — the ones that only moved in the
        // source are re-stamped, the rest are left as they are.
        // Returns a one-line account of what it did, for the output channel.
        function applySvg(svg) {
            const split = splitPages(svg);
            const live = svgContainer.querySelector('svg');
            const livePages = live ? live.querySelectorAll(':scope > g.page') : [];
            const frame = split ? split.head + split.tail : null;
            const whole = () => {
                svgContainer.innerHTML = svg;
                shownFrame = frame;
                shownPages = split
                    ? split.pages.map(markup => ({ markup, blank: markup.replace(SOURCE_ATTRS, ' $1') }))
                    : null;
            };
            if (!split || !shownPages || !live
                || livePages.length !== split.pages.length || frame !== shownFrame) {
                whole();
                return 'whole document (' + (split ? split.pages.length + ' pages' : 'no page groups') + ')';
            }
            let pagesKept = 0, pagesRestamped = 0, pagesReplaced = 0;
            const changed = []; // pages whose drawing changed — reconciled together below
            for (let i = 0; i < split.pages.length; i++) {
                const markup = split.pages[i];
                const shown = shownPages[i];
                if (markup === shown.markup) { pagesKept++; continue; }
                const blank = markup.replace(SOURCE_ATTRS, ' $1');
                // The drawing is the same: only offsets moved.
                if (blank === shown.blank && restampPage(livePages[i], markup)) {
                    shownPages[i] = { markup, blank }; pagesRestamped++; continue;
                }
                changed.push(i);
            }
            let account = '';
            if (changed.length) {
                const r = reflowPages(livePages, split, changed);
                if (r.fail) {
                    // A partial reconcile may have moved groups between the changed pages;
                    // replacing each of them from its own markup rebuilds exactly those
                    // pages, so it is safe (only they took part).
                    for (const i of changed) {
                        const fresh = parseOne(split.pages[i]);
                        if (!fresh) { whole(); return 'whole document (a page did not parse alone)'; }
                        live.replaceChild(fresh, livePages[i]);
                        const markup = split.pages[i];
                        shownPages[i] = { markup, blank: markup.replace(SOURCE_ATTRS, ' $1') };
                        pagesReplaced++;
                    }
                    account = ', replaced ' + pagesReplaced + ' (reconcile gave up: ' + r.fail + ')';
                } else {
                    account = ', reconciled ' + changed.length + ' (groups kept ' + r.kept + ', moved ' + r.moved
                        + ', carried ' + r.carried + ', parsed ' + r.parsed + (r.why ? '; ' + r.why : '') + ')'
                        + recheckOffsets(live, livePages, split.pages, changed);
                }
            }
            return 'pages kept ' + pagesKept + ', re-stamped ' + pagesRestamped + account;
        }

        // ---- page-wise answers --------------------------------------------------
        // The server can send the picture as PAGES (SvgPages): with a base version, a
        // DELTA against the picture on screen — which pages are the same, which only
        // moved their source offsets, and the markup of the ones that changed — so the
        // whole document no longer travels on every keystroke (2026-09-18: on a 1000-bar
        // book 3–12 MB of markup per keystroke, of which one page had changed). A 'same'
        // page is left alone. A 'shifted' page has every data-pos / data-alt mapped
        // through the answer's window, the arithmetic the server verified the new page
        // against (below the prefix unchanged, at or after the suffix start moved by the
        // delta); its stored markup then no longer says the right numbers, which the
        // reconcile allows for (stale). A 'changed' page goes through the same group
        // reconcile a changed page of a one-string answer does. Whatever this cannot
        // apply — no picture on screen, another page count, another frame, a shift the
        // window declines — asks the host for the whole picture (requestFull) and
        // returns null; the answer to that carries every page and replaces the document.
        function requestFull(why) {
            vscode.postMessage({ type: 'requestFull', why });
            return null;
        }
        function pagesChars(ps) {
            let n = ps.Head.length + ps.Tail.length;
            for (const item of ps.Items) n += item.Markup ? item.Markup.length : 0;
            return n;
        }
        // Maps one offset through the window; NaN when the window has no image for it.
        function shiftOffset(n, w) {
            return n < w.Prefix ? n : n >= w.SuffixStart ? n + w.Delta : NaN;
        }
        // Re-stamps every source offset the page carries through the window. false when
        // an offset lies inside the window — the server never calls such a page shifted,
        // so this is the picture and the answer disagreeing, and the caller starts over.
        function shiftPage(page, w) {
            if (!w) return false;
            for (const el of page.querySelectorAll(SOURCE_SELECTOR)) {
                for (const name of ['data-pos', 'data-start']) {
                    const pos = el.getAttribute(name);
                    if (pos === null) continue;
                    const m = shiftOffset(parseInt(pos, 10), w);
                    if (!Number.isFinite(m)) return false;
                    // (written only where it moved: what is written before the edit — another
                    // part's bars on this page — keeps its offset, and an attribute not written
                    // is an element the browser does not look at again)
                    const moved = String(m);
                    if (moved !== pos) el.setAttribute(name, moved);
                }
                for (const name of ['data-alt', 'data-span']) {
                    const list = el.getAttribute(name);
                    if (!list) continue;
                    const parts = list.split(' ').map(a => shiftOffset(parseInt(a, 10), w));
                    if (parts.some(m => !Number.isFinite(m))) return false;
                    const moved = parts.join(' ');
                    if (moved !== list) el.setAttribute(name, moved);
                }
            }
            return true;
        }
        function applyPages(ps) {
            const items = ps.Items;
            const frame = ps.Head + ps.Tail;
            const live = svgContainer.querySelector('svg');
            const livePages = live ? live.querySelectorAll(':scope > g.page') : [];
            const allMarkup = items.every(it => typeof it.Markup === 'string');
            if (!shownPages || !live || livePages.length !== items.length || frame !== shownFrame) {
                if (!allMarkup) {
                    return requestFull(!live ? 'no picture on screen'
                        : !shownPages ? 'no record of the pages shown'
                        : livePages.length !== items.length ? 'page count ' + livePages.length + ' -> ' + items.length
                        : 'the frame around the pages changed');
                }
                svgContainer.innerHTML = ps.Head + items.map(it => it.Markup).join('') + ps.Tail;
                shownFrame = frame;
                shownPages = items.map(it => ({ markup: it.Markup, blank: it.Markup.replace(SOURCE_ATTRS, ' $1') }));
                return 'whole document (' + items.length + ' pages)';
            }
            let pagesKept = 0, pagesShifted = 0, pagesReplaced = 0;
            const changed = [];
            const markups = new Array(items.length);
            for (let i = 0; i < items.length; i++) {
                const item = items[i];
                if (item.Change === 'same') { pagesKept++; continue; }
                if (item.Change === 'shifted') {
                    if (!shiftPage(livePages[i], ps.Window)) {
                        return requestFull('page ' + i + ' holds an offset the window has no image for');
                    }
                    shownPages[i].stale = true;
                    shiftPageFacts(livePages[i], ps.Window);
                    pagesShifted++;
                    continue;
                }
                if (typeof item.Markup !== 'string') {
                    return requestFull('page ' + i + ' is changed but carries no markup');
                }
                markups[i] = item.Markup;
                changed.push(i);
            }
            let account = '';
            if (changed.length) {
                const r = reflowPages(livePages, { pages: markups }, changed);
                if (r.fail) {
                    for (const i of changed) {
                        const fresh = parseOne(markups[i]);
                        if (!fresh) {
                            return requestFull('page ' + i + ' did not parse alone');
                        }
                        live.replaceChild(fresh, livePages[i]);
                        const markup = markups[i];
                        shownPages[i] = { markup, blank: markup.replace(SOURCE_ATTRS, ' $1') };
                        pagesReplaced++;
                    }
                    account = ', replaced ' + pagesReplaced + ' (reconcile gave up: ' + r.fail + ')';
                } else {
                    account = ', reconciled ' + changed.length + ' (groups kept ' + r.kept + ', moved ' + r.moved
                        + ', carried ' + r.carried + ', parsed ' + r.parsed + (r.why ? '; ' + r.why : '') + ')'
                        + recheckOffsets(live, livePages, markups, changed);
                }
            }
            return 'pages kept ' + pagesKept + ', shifted ' + pagesShifted + account;
        }

        // ---- source-position index -------------------------------------------
        // Every source offset the rendered score carries, sorted. Built once per
        // SVG and reused by the caret handlers, which run on every cursor
        // movement: reading data-pos off each drawn element there cost the whole
        // document per keystroke, and with the preview open a long score visibly
        // lagged behind the caret. A barline can also carry a data-alt list (the
        // other written bars that collapse onto it), and those offsets count too.
        let posSorted = [];        // ascending, unique
        let posElements = new Map(); // offset -> the drawn elements that carry it
        let posIndexBuilt = false;

        function invalidateSourcePositions() {
            posIndexBuilt = false;
            posSorted = [];
            posElements = new Map();
        }

        // What ONE page carries, read off its elements once and kept with the page (its
        // g.page element). The index used to be read off every element of the picture after
        // every update, the pages that update left alone included: MEASURED in a real VS Code
        // (session 1302, a 1000-bar book of 16 pages, one page changed by the keystroke) the
        // read was 10 ms of the page's 22 after a swap of 3.7. A page's facts stand for as long
        // as the page shows the markup they were read from — shownPages[i], the very object: a
        // changed or re-stamped page is given a new one, a replaced page is another element, a
        // whole new picture is all new elements — and a page the server calls shifted has them
        // mapped through the window its attributes are mapped through (shiftPageFacts). With no
        // record of the pages shown nothing is kept and every page is read, as before.
        const pageFactsOf = new WeakMap();
        // pos: [offset, element, offset, element, …] in the page's order — the element null
        // where it carries the offset and is never painted (sourcePositions says which).
        // spans: [rect, from, to, …] — each staff's bar that says where it is written
        // (rect.bar-staff's data-span), for the caret's bar. bars: the bar boxes that say their
        // measure ({ m, bar, rect } off rect.bar-box's data-m and data-bar), for the A-B flags.
        function readPageFacts(root) {
            const pos = [], spans = [], bars = [];
            for (const el of root.querySelectorAll('[data-pos], [data-alt], rect.bar-staff[data-span], rect.bar-box[data-m]')) {
                if (el.classList.contains('bar-box')) {
                    const m = parseInt(el.getAttribute('data-m'), 10);
                    if (Number.isInteger(m)) bars.push({ m, bar: parseInt(el.getAttribute('data-bar'), 10), rect: el });
                    continue;
                }
                if (el.classList.contains('bar-staff')) {
                    const span = (el.getAttribute('data-span') || '').split(' ');
                    if (span.length === 2) spans.push(el, parseInt(span[0], 10), parseInt(span[1], 10));
                    continue;
                }
                const paintable = !el.classList.contains('nh-hit')
                    && !(el.tagName.toLowerCase() === 'rect'
                        && !el.getAttribute('stroke') && el.getAttribute('fill'));
                const keep = paintable ? el : null;
                const n = parseInt(el.getAttribute('data-pos'), 10);
                if (Number.isFinite(n)) pos.push(n, keep);
                const alt = el.getAttribute('data-alt');
                if (alt) {
                    for (const a of alt.split(' ')) {
                        const m = parseInt(a, 10);
                        if (Number.isFinite(m)) pos.push(m, keep);
                    }
                }
            }
            return { pos, spans, bars, shown: null };
        }
        // Every page's facts, in the pages' order (a picture with no page groups is one page,
        // read each time; none with no picture).
        function livePageFacts() {
            const svg = svgContainer.querySelector('svg');
            if (!svg) return [];
            const groups = svg.querySelectorAll(':scope > g.page');
            if (groups.length === 0) return [readPageFacts(svg)];
            const all = [];
            for (let i = 0; i < groups.length; i++) all.push(pageFacts(groups[i], i));
            return all;
        }
        function pageFacts(page, i) {
            const shown = shownPages ? shownPages[i] : null;
            const kept = pageFactsOf.get(page);
            if (kept && shown && kept.shown === shown) return kept;
            const facts = readPageFacts(page);
            facts.shown = shown;
            pageFactsOf.set(page, facts);
            return facts;
        }
        // A shifted page's facts, through the window shiftPage has just taken its attributes
        // through (the same arithmetic, so the two agree offset for offset).
        function shiftPageFacts(page, w) {
            const facts = pageFactsOf.get(page);
            if (!facts) return;
            for (let k = 0; k < facts.pos.length; k += 2) {
                const m = shiftOffset(facts.pos[k], w);
                if (!Number.isFinite(m)) { pageFactsOf.delete(page); return; }
                facts.pos[k] = m;
            }
            for (let k = 0; k < facts.spans.length; k += 3) {
                const from = shiftOffset(facts.spans[k + 1], w), to = shiftOffset(facts.spans[k + 2], w);
                if (!Number.isFinite(from) || !Number.isFinite(to)) { pageFactsOf.delete(page); return; }
                facts.spans[k + 1] = from;
                facts.spans[k + 2] = to;
            }
        }

        // The elements sharing one data-pos are indexed here as well, so a caret
        // movement paints its note from a Map lookup: the attribute selector it
        // used to run ('[data-pos="N"], [data-alt~="N"]') walks EVERY element of
        // the SVG, and the preview's frame runs in the editor's window — on a
        // long score each arrow key spent that walk before the caret could move
        // (owner report 2026-09-04). Two kinds of element that share a note's
        // offset are left out of the index because they must never be
        // recolored: the transparent 'nh-hit' notehead click target (a filled box
        // would show), and the white OCCLUDER behind a tab fret digit (it hides
        // the string line). A genuine boxed label (section/rehearsal) is STROKED;
        // the occluder is fill-only. It is told by its EXPLICIT fill: a barline is
        // a strokeless rect too, but drawn in the black default (no fill
        // attribute), so an unstroked rect WITHOUT a fill attribute is real ink
        // and must still highlight — only an unstroked rect that carries a fill
        // (the white mask) is the occluder. Leaving the mask in made hasBox true,
        // which coloured the box and suppressed the notehead's own highlight.
        function sourcePositions() {
            if (posIndexBuilt) return posSorted;
            const seen = new Set();
            const byPos = new Map();
            const add = (n, el) => {
                if (!Number.isFinite(n)) return;
                seen.add(n);
                if (!el) return;
                const list = byPos.get(n);
                if (list) list.push(el); else byPos.set(n, [el]);
            };
            const svg = svgContainer.querySelector('svg');
            if (svg) {
                // Page by page, each page's own offsets read once (pageFacts); a picture with
                // no page groups is read whole, as it always was.
                const groups = svg.querySelectorAll(':scope > g.page');
                const each = facts => {
                    for (let k = 0; k < facts.pos.length; k += 2) add(facts.pos[k], facts.pos[k + 1]);
                };
                if (groups.length === 0) each(readPageFacts(svg));
                else for (let i = 0; i < groups.length; i++) each(pageFacts(groups[i], i));
            }
            posSorted = Array.from(seen).sort((a, b) => a - b);
            posElements = byPos;
            posIndexBuilt = true;
            return posSorted;
        }

        // The elements currently painted, so clearing them costs those elements
        // and not a '.highlight' query over the whole SVG.
        let highlighted = [];

        function clearHighlights() {
            const els = highlighted;
            highlighted = [];
            els.forEach(el => {
                el.classList.remove('highlight');
                // Only restore what we actually recolored (origStroke/origFill set);
                // a boxed label's text is left untouched on highlight, so leave its
                // fill alone here too — undefined means "never recolored".
                if (el.__origStroke !== undefined) {
                    restoreAttr(el, 'stroke', el.__origStroke);
                    el.__origStroke = undefined;
                }
                if (el.__origFill !== undefined) {
                    restoreAttr(el, 'fill', el.__origFill);
                    el.__origFill = undefined;
                }
                // Restore the original z-order (DOM position) that was changed
                // when this element was raised on highlight.
                if (el.__origParent) {
                    el.__origParent.insertBefore(el, el.__origNextSibling);
                    el.__origParent = null;
                    el.__origNextSibling = null;
                }
            });
        }

        // A caret highlight is painted once per frame, from the LAST message: a
        // held arrow key sends a caret position per repeat, and painting each
        // one — recolor, raise, restore — costs a style and layout pass of the
        // whole SVG in the editor's own window. Only the position the caret has
        // now is worth a frame.
        let pendingHighlight = null;
        let highlightFrame = 0;
        function scheduleHighlight(paint) {
            pendingHighlight = paint;
            if (highlightFrame) return;
            highlightFrame = requestAnimationFrame(() => {
                highlightFrame = 0;
                const paintNow = pendingHighlight;
                pendingHighlight = null;
                if (paintNow) paintNow();
            });
        }

        // The editor's caret as the extension last said it (highlightPosition / highlightRange).
        var editorCaret = null;
        /** Where a Play pressed while playing starts: the note the editor's caret is on (the
         *  rule highlightNearestElement lights it by), else the caret's own place (the next note
         *  written after it is found when the notes come), else a selection's start; -1 none. */
        function caretStartPos() {
            const c = editorCaret;
            if (!c) return -1;
            if (c.ranges && c.ranges.length > 0) return Math.min.apply(null, c.ranges.map(r => r[0]));
            if (!(c.pos >= 0)) return typeof c.caret === 'number' ? c.caret : -1;
            sourcePositions();
            const floor = typeof c.tokenStart === 'number' && c.tokenStart >= 0 ? c.tokenStart : 0;
            let lo = 0, hi = posSorted.length - 1, nearest = -1;
            while (lo <= hi) {
                const mid = (lo + hi) >> 1;
                if (posSorted[mid] <= c.pos) { nearest = posSorted[mid]; lo = mid + 1; }
                else { hi = mid - 1; }
            }
            return nearest >= floor && c.pos - nearest < HIGHLIGHT_THRESHOLD ? nearest : c.pos;
        }

        function highlightNearestElement(cursorPos, tokenStart) {
            // Clear previous highlights
            clearHighlights();
            if (cursorPos < 0) return;

            // The note the caret is on = the nearest one AT or before the caret that also
            // lies within the caret's own token (data-pos >= tokenStart). The tokenStart
            // guard is what rejects the nearest PRECEDING note when the caret sits on a
            // barline / keyword / gap — that token holds no note, so nothing highlights.
            //
            // Answered from the source-position index, not by reading the attributes
            // of every drawn element: this runs on EVERY cursor movement, and the
            // scan is what made a long score feel stuck whenever the preview was open.
            sourcePositions();
            const floor = (typeof tokenStart === 'number' && tokenStart >= 0) ? tokenStart : 0;
            let lo = 0, hi = posSorted.length - 1, nearestPos = -1;
            while (lo <= hi) {
                const mid = (lo + hi) >> 1;
                if (posSorted[mid] <= cursorPos) { nearestPos = posSorted[mid]; lo = mid + 1; }
                else { hi = mid - 1; }
            }
            // The greatest position at or before the caret is the nearest one; if even
            // that lies before the caret's own token, no note belongs to this caret.
            if (nearestPos < floor) { nearestPos = -1; }
            const nearestDist = nearestPos >= 0 ? cursorPos - nearestPos : Infinity;

            // Highlight ALL elements with that data-pos
            if (nearestPos >= 0 && nearestDist < HIGHLIGHT_THRESHOLD) {
                highlightPositions([nearestPos]);
            }
            askStoppedPosition();   // the header's position follows the caret while stopped
        }

        // Highlight every note whose source position falls inside a selected range
        // (an editor selection, possibly several ranges for a multi-cursor select).
        function highlightRange(ranges) {
            clearHighlights();
            // Over the index's distinct positions rather than every drawn element:
            // a chord's heads, dots and accidentals all carry the same offset, so
            // the element list is several times longer than the answer needs.
            sourcePositions();
            const positions = [];
            for (const pos of posSorted) {
                for (let i = 0; i < ranges.length; i++) {
                    if (pos >= ranges[i][0] && pos < ranges[i][1]) {
                        positions.push(pos);
                        break;
                    }
                }
            }
            if (positions.length > 0) {
                highlightPositions(positions);
            }
            askStoppedPosition();
        }

        // The marks the scan view's slider hides (*Marks below*), hidden here too (owner's
        // request 2026-10-12): every piece of a mark carries its key in data-todo, so one
        // stylesheet hides the ring, the memo and its leader of each key, and keeps doing so
        // across re-renders. The music glyph a mark rings (text.music) is never hidden.
        function hideTodos(keys) {
            let style = document.getElementById('hiddenTodos');
            if (!style) {
                style = document.createElement('style');
                style.id = 'hiddenTodos';
                document.head.appendChild(style);
            }
            style.textContent = hiddenTodoCss(keys);
        }
        function hiddenTodoCss(keys) {
            const rules = [];
            for (const key of keys) {
                const k = '[data-todo="' + String(key).replace(/["\\\\]/g, '') + '"]';
                rules.push('#svgContainer path' + k, '#svgContainer ellipse' + k, '#svgContainer rect' + k,
                    '#svgContainer line' + k, '#svgContainer text' + k + ':not(.music)');
            }
            return rules.length === 0 ? '' : rules.join(', ') + ' { display: none; }';
        }

        // Drag the sheet to scroll it (owner request 2026-10-12). A press becomes a drag only
        // once the mouse has moved SLOP pixels, so a click on a note still jumps to the editor
        // (or seeks, or extends the selection), and the click a drag ends with is swallowed
        // here, in the capture phase, before the document's click handler sees it. Only the
        // left button of a mouse with no modifier: shift-click selects, a touch or pen
        // scrolls natively, and a press on a scroll bar or a control is left alone.
        function installDragScroll(scroller, doc) {
            const SLOP = 4;
            let press = null;
            let dragged = false;
            scroller.addEventListener('pointerdown', (e) => {
                // Every click starts with a press, so a flag a drag left behind (its click
                // never came) cannot swallow the next real click.
                dragged = false;
                press = null;
                if (e.pointerType !== 'mouse' || e.button !== 0
                    || e.shiftKey || e.ctrlKey || e.metaKey || e.altKey) return;
                const r = scroller.getBoundingClientRect();
                if (e.clientX - r.left >= scroller.clientLeft + scroller.clientWidth
                    || e.clientY - r.top >= scroller.clientTop + scroller.clientHeight) return;
                if (e.target && e.target.closest && e.target.closest('button, input, select, textarea, a')) return;
                press = { id: e.pointerId, x: e.clientX, y: e.clientY, left: scroller.scrollLeft, top: scroller.scrollTop };
            });
            scroller.addEventListener('pointermove', (e) => {
                if (press === null || e.pointerId !== press.id) return;
                const dx = e.clientX - press.x, dy = e.clientY - press.y;
                if (!dragged) {
                    if (Math.abs(dx) < SLOP && Math.abs(dy) < SLOP) return;
                    dragged = true;
                    scroller.setPointerCapture(e.pointerId);
                    scroller.classList.add('dragging');
                }
                scroller.scrollLeft = press.left - dx;
                scroller.scrollTop = press.top - dy;
            });
            const release = (e) => {
                if (press === null || e.pointerId !== press.id) return;
                press = null;
                scroller.classList.remove('dragging');
            };
            scroller.addEventListener('pointerup', release);
            scroller.addEventListener('pointercancel', (e) => { release(e); dragged = false; });
            doc.addEventListener('click', (e) => {
                if (!dragged) return;
                dragged = false;
                e.stopImmediatePropagation();
                e.preventDefault();
            }, true);
        }

        // A bar of a staff is a click target too (owner request 2026-10-14, after the OMR scan
        // view's bars): a click that lands on no data-pos element but inside a bar goes to where
        // that bar is written — the server's rect.bar-staff boxes carry it (data-start). They
        // take no pointer events, so a note keeps its own click and the sheet its drag; the box
        // is found here by its geometry. A click beside a staff (a lyric line, a chord name, the
        // gap to the next staff) takes the nearest staff of the same bar, up to two staff
        // heights away.
        // A bar's notes as a selection: the first and last data-pos inside its written stretch
        // (data-span), or its start when it holds none.
        function barNoteRange(r) {
            const span = (r.getAttribute('data-span') || '').split(' ').map(Number);
            const start = parseInt(r.getAttribute('data-start'), 10);
            let lo = -1, hi = -1;
            if (span.length === 2 && !isNaN(span[0]) && !isNaN(span[1])) {
                for (const n of svgContainer.querySelectorAll('[data-pos]')) {
                    const p = parseInt(n.getAttribute('data-pos'), 10);
                    if (p >= span[0] && p < span[1]) {
                        if (lo < 0 || p < lo) lo = p;
                        if (p > hi) hi = p;
                    }
                }
            }
            return lo >= 0 ? [lo, hi] : (isNaN(start) ? null : [start, start]);
        }
        function barStaffAt(clientX, clientY) {
            let best = null, bestDist = Infinity;
            for (const r of svgContainer.querySelectorAll('rect.bar-staff')) {
                const b = r.getBoundingClientRect();
                if (clientX < b.left || clientX > b.right) continue;
                const d = clientY < b.top ? b.top - clientY : clientY > b.bottom ? clientY - b.bottom : 0;
                if (d <= 2 * b.height && d < bestDist) { best = r; bestDist = d; }
            }
            return best;
        }
        // The bar playing now, tinted on its STAVES as a hover tints a bar (owner's request
        // 2026-11-16: the chord names are small, the eye loses them) — the staves' boxes alone, not a
        // chords or lyrics row's (data-row), whose band would overlap a one-line staff's. The bar is
        // the one the first lit element stands in (its copy, as the follow-along lit it); a moment
        // with nothing lit keeps the bar there. null takes it away (the playback stopped).
        /** The box (client rect) of the bar an element stands in — of all its system's staves — or null. */
        function barBoxOf(el) {
            const box = barBoxElOf(el);
            return box ? box.getBoundingClientRect() : null;
        }
        /** …and the box itself (rect.bar-box), which says its measure (data-m). */
        function barBoxElOf(el) {
            const b = el.getBoundingClientRect();
            const cx = (b.left + b.right) / 2, cy = (b.top + b.bottom) / 2;
            // The bars of the system the element is DRAWN in (g.system), where it is in one: a note
            // high over its staff stands as near the system above as its own, and by the distance
            // alone the bar lit jumped a line up for that note (owner's report 2026-11-19: a string
            // quartet's bar 55, its first violin on ledger lines, lit bar 49 over it). Every bar of
            // the page for what is drawn outside the systems (the page's overlay group).
            const system = el.closest('g.system');
            let box = null, bestGap = Infinity;
            for (const r of (system || svgContainer).querySelectorAll('rect.bar-box')) {
                const k = r.getBoundingClientRect();
                if (cx < k.left || cx > k.right) continue;
                const gap = cy < k.top ? k.top - cy : cy > k.bottom ? cy - k.bottom : 0;
                if (gap < bestGap) { box = r; bestGap = gap; }
            }
            return box;
        }
        // The bar playing, as its measure index (the header's "Bar 12 / 118", showBarInfo): litBar while
        // a bar is lit, lastLitBar the one lit when the playing stopped (a pause shows it still).
        var litBar = -1, lastLitBar = -1;
        function lightPlayingBar(painted) {
            for (const r of svgContainer.querySelectorAll('rect.bar-staff.playing')) r.classList.remove('playing');
            if (!painted || painted.length === 0) {
                if (!painted) { if (litBar >= 0) lastLitBar = litBar; litBar = -1; showBarInfo(); }
                return;
            }
            const box = barBoxElOf(painted[0]);
            const column = box ? box.getBoundingClientRect() : null;
            if (!column) return;
            const m = parseInt(box.getAttribute('data-m'), 10);
            if (Number.isInteger(m) && m !== litBar) { litBar = m; showBarInfo(); }
            for (const r of svgContainer.querySelectorAll('rect.bar-staff:not([data-row])')) {
                const k = r.getBoundingClientRect();
                const mx = (k.left + k.right) / 2, my = (k.top + k.bottom) / 2;
                if (mx >= column.left && mx <= column.right && my >= column.top - 1 && my <= column.bottom + 1) r.classList.add('playing');
            }
        }
        let hoveredBar = null;
        function hoverBar(r) {
            if (r === hoveredBar) return;
            if (hoveredBar) hoveredBar.classList.remove('hover');
            hoveredBar = r;
            if (r) r.classList.add('hover');
            mainContent.classList.toggle('over-bar', r !== null);
            // …and a scan view of the score lights the same bar (omrScan previewHoversBar).
            vscode.postMessage({ type: 'hoverBar', start: r ? parseInt(r.getAttribute('data-start'), 10) : null });
        }
        // The bar the editor's caret is in stays lit (owner's request 2026-10-14, as the scan view
        // lights it): every bar whose written stretch (data-span: its first note to the bar line
        // closing it) holds the caret — just after that bar line included — or the start of the
        // token under it; each printed copy of it. Kept across renders (the caret is re-applied
        // with the bar flags), and moved by an edit's caret movement. A caret before a bar's first
        // note on the line that bar starts on — on 'voice {', 'tuplet 3/2 {', 'break', the
        // indentation — lights that bar too (owner's report 2026-10-23: a bar that opens with
        // 'voice {' lit only once the caret reached its first note): when no bar holds the caret,
        // the first bar to start after it before the line ends (lineEnd, sent with the caret).
        let caretAt = -1, caretToken = -1, caretLineEnd = -1;
        function lightCaretBar(caret, tokenStart, lineEnd) {
            if (Number.isInteger(caret)) {
                caretAt = caret;
                caretToken = Number.isInteger(tokenStart) ? tokenStart : -1;
                caretLineEnd = Number.isInteger(lineEnd) ? lineEnd : -1;
            }
            for (const r of svgContainer.querySelectorAll('rect.bar-staff.caret')) r.classList.remove('caret');
            if (caretAt < 0) return;
            let lit = false, next = Infinity;
            const spans = [];
            // (the bars' stretches off the pages' facts: read off the page once, not every caret)
            for (const facts of livePageFacts()) {
                const s = facts.spans;
                for (let k = 0; k < s.length; k += 3) {
                    const r = s[k], from = s[k + 1], to = s[k + 2];
                    spans.push([r, from]);
                    if ((caretAt >= from && caretAt <= to + 1) || (caretToken >= from && caretToken <= to)) { r.classList.add('caret'); lit = true; }
                    else if (from > caretAt && from <= caretLineEnd && from < next) next = from;
                }
            }
            if (!lit && next !== Infinity) {
                for (const [r, from] of spans) if (from === next) r.classList.add('caret');
            }
        }
        // The scan view's bar under the mouse, lit here: every bar whose first note is written
        // on the line the reader wrote it on, [from, to] (omrScan lineOfScanBar); none without.
        function lightPeerBars(from, to) {
            for (const r of svgContainer.querySelectorAll('rect.bar-staff.peer')) r.classList.remove('peer');
            if (!Number.isInteger(from) || !Number.isInteger(to)) return;
            for (const r of svgContainer.querySelectorAll('rect.bar-staff')) {
                const s = parseInt(r.getAttribute('data-start'), 10);
                if (s >= from && s <= to) r.classList.add('peer');
            }
        }
        function installBarHover() {
            let pending = null;
            svgContainer.addEventListener('mousemove', (e) => {
                pending = e;
                requestAnimationFrame(() => {
                    if (pending === null) return;
                    const ev = pending;
                    pending = null;
                    const t = ev.target;
                    const onNote = t && t.hasAttribute && t.hasAttribute('data-pos');
                    // (the pointer over what a click jumps to — the style's over-note; a glyph's
                    // own child counts as the glyph, as it did when the rule was on [data-pos])
                    mainContent.classList.toggle('over-note', !!(t && t.closest && t.closest('[data-pos]')));
                    hoverBar(onNote || ev.buttons !== 0 || ev.shiftKey || mainContent.classList.contains('dragging')
                        ? null : barStaffAt(ev.clientX, ev.clientY));
                });
            });
            svgContainer.addEventListener('mouseleave', () => {
                pending = null;
                mainContent.classList.remove('over-note');
                hoverBar(null);
            });
        }

        // Groups all elements sharing one data-pos into printed instances
        // (phrase copies): elements of ONE instance sit within a chord's
        // footprint; other copies live in other measures/systems. Bands by
        // y-gap (systems are far apart), then splits by x-gap. Returned in
        // reading order = chronological within the part's staff.
        function clusterInstances(matches) {
            // DOM order IS drawing order (measure by measure, system by
            // system) - i.e. chronological within the part's staff. Keep it,
            // and only SPLIT when the next element sits in another bar, or far
            // from the previous one in its bar (a phrase written twice in one
            // bar). The earlier y-band sort could merge two systems through
            // ledger-note ys and then x-sorting interleaved their copies out of
            // time order.
            // ⚠️ WHERE IT IS DRAWN ON THE PAGE, NOT ITS NEIGHBOUR IN THE DOM, makes
            // a copy (owner's reports 2026-10-29: in "form { A B A }" playing A lit
            // both copies, and a song's playback shook the view between pages).
            // getBBox is in the element's own frame and every system has its own,
            // so copies on two systems at one x stood 0 apart and were one copy;
            // and a page draws some heads again in its overlay group after its
            // systems (a hi-hat's), so the DOM is not in time order. A copy is the
            // elements in one bar-box (in time order, the DOM's order of the bars)
            // within 6 staff spaces of each other across; the old gaps stay for a
            // page with no bar-boxes.
            const bars = Array.from(svgContainer.querySelectorAll('rect.bar-box'));
            if (bars.length === 0) return clusterByGaps(matches);
            const boxes = bars.map(r => r.getBoundingClientRect());
            const systems = bars.map(r => r.closest('g.system'));
            let unit = 1;
            try { const b0 = bars[0].getBBox(); if (b0.width > 0) unit = boxes[0].width / b0.width; } catch (e) { /* non-SVG */ }
            const byBar = new Map();
            const loose = [];
            let last = null;
            for (const el of matches) {
                const b = el.getBoundingClientRect();
                const x = b.left + 0.5, y = (b.top + b.bottom) / 2;
                // The bar whose column holds it, the nearest of them up or down: a box
                // spans the staff lines only, and a note under the staff is outside it.
                // …of ITS OWN system's, where it is drawn in one (see barBoxElOf): a note high
                // over its staff is as near the system above.
                const own = el.closest('g.system');
                let bar = -1, nearest = Infinity;
                for (let i = 0; i < boxes.length; i++) {
                    if (own && systems[i] !== own) continue;
                    const r = boxes[i];
                    if (x < r.left || x > r.right) continue;
                    const d = y < r.top ? r.top - y : y > r.bottom ? y - r.bottom : 0;
                    if (d < nearest) { nearest = d; bar = i; }
                }
                // In no bar's column (a mark beyond the staves): with the element before it.
                if (bar < 0) { if (last) last.els.push(el); else loose.push(el); continue; }
                let list = byBar.get(bar);
                if (!list) byBar.set(bar, list = []);
                let inst = list.find(c => Math.abs(c.x - b.left) <= 6 * unit);
                if (!inst) { inst = { x: b.left, els: [] }; list.push(inst); }
                inst.els.push(el);
                last = inst;
            }
            const instances = [];
            for (const bar of Array.from(byBar.keys()).sort((p, q) => p - q)) {
                for (const c of byBar.get(bar).sort((p, q) => p.x - q.x)) instances.push(c.els);
            }
            if (loose.length > 0) {
                if (instances.length > 0) instances[0].unshift.apply(instances[0], loose);
                else instances.push(loose);
            }
            return instances;
        }
        // The copies told apart by gaps in DOM order: a page drawn without bar-boxes.
        function clusterByGaps(matches) {
            const instances = [];
            let inst = null;
            let prev = null;
            for (const el of matches) {
                let x = 0, y = 0;
                try { const b = el.getBBox(); x = b.x; y = b.y; } catch (e) { /* non-SVG */ }
                if (!inst || Math.abs(x - prev.x) > 6 || Math.abs(y - prev.y) > 12) {
                    inst = [];
                    instances.push(inst);
                }
                inst.push(el);
                prev = { x: x, y: y };
            }
            return instances;
        }

        // Paints every element of every given data-pos (playback lights the
        // WHOLE onset: rh and lh notes striking together are different
        // source positions). Does NOT clear existing highlights.
        // Entries may be numbers (paint ALL copies - editor click sync) or
        // { pos, occ }: a phrase's every expansion shares ONE source
        // position, so copy #occ in document order (= chronological within
        // the part's staff) is the note actually sounding now.
        /** The drawn elements of one entry of highlightPositions (a data-pos, or { pos, occ }),
         *  painted or not — the follow-along reads the notes about to sound by it (followAlong). */
        function elementsAt(entry) {
            const pos = typeof entry === 'object' ? entry.pos : entry;
            const occ = typeof entry === 'object' ? entry.occ : -1;
            sourcePositions();
            let matches = (posElements.get(pos) || []).slice();
            if (occ >= 0 && matches.length > 1) {
                const marks = matches.filter(el => el.hasAttribute('data-todo'));
                const music = marks.length > 0 && marks.length < matches.length
                    ? matches.filter(el => !el.hasAttribute('data-todo')) : matches;
                const instances = clusterInstances(music);
                const picked = instances[Math.min(occ, instances.length - 1)] || music;
                matches = music !== matches && instances.length === 1 ? picked.concat(marks) : picked;
            }
            return matches;
        }
        function highlightPositions(positions) {
            const color = getHighlightColor();
            const painted = [];
            for (const entry of positions) {
                const pos = typeof entry === 'object' ? entry.pos : entry;
                const occ = typeof entry === 'object' ? entry.occ : -1;
                // Every drawn element carrying this offset — as its data-pos OR as
                // a data-alt member (a barline that several written bars collapse
                // onto lights from any of their offsets) — from the index, which
                // already leaves out the click targets and occluders that must
                // never be recolored (see sourcePositions).
                sourcePositions();
                let matches = (posElements.get(pos) || []).slice();
                if (occ >= 0 && matches.length > 1) {
                    // Pick the occ-th printed INSTANCE — a chord's every head
                    // (plus dots/accidentals) shares one data-pos, so slicing
                    // by raw element index lit a single head and left the
                    // rest of the chord dark. Cluster by geometry instead.
                    // An @todo mark's pieces (data-todo: its ring, and a memo that may
                    // stand bars away) share the data-pos too but are no copy of the
                    // music: counted, a memo moved over another bar was "copy 0" and lit
                    // alone, the chord name or head left dark (owner's report 2026-11-16).
                    // So the copies are counted from the music alone; the mark lights
                    // with it where there is one copy only.
                    const marks = matches.filter(el => el.hasAttribute('data-todo'));
                    const music = marks.length > 0 && marks.length < matches.length
                        ? matches.filter(el => !el.hasAttribute('data-todo')) : matches;
                    const instances = clusterInstances(music);
                    const picked = instances[Math.min(occ, instances.length - 1)] || music;
                    matches = music !== matches && instances.length === 1 ? picked.concat(marks) : picked;
                }
                painted.push.apply(painted, matches);
                highlighted.push.apply(highlighted, matches);
                // A boxed mark (section/rehearsal) is a <rect> with a <text> label on
                // top. Recoloring both to the highlight color hides the label, so when
                // the group has a box we recolor the box only and leave its text its
                // own color (still raised above the box, so it stays readable).
                const hasBox = matches.some(el => el.tagName.toLowerCase() === 'rect');
                matches.forEach(el => {
                    el.classList.add('highlight');
                    const tag = el.tagName.toLowerCase();
                    // Save the shipped paint once (guard against re-highlight
                    // clobbering it with the highlight color) so clear can restore
                    // the real value, not SVG's default.
                    if (tag === 'line' || (el.getAttribute('fill') === 'none' && el.getAttribute('stroke'))) {
                        // A line, or an outline-only shape (a chord diagram's open-string
                        // ring): recolor the stroke — filling a ring would turn it into a dot.
                        if (el.__origStroke === undefined) el.__origStroke = el.getAttribute('stroke');
                        el.setAttribute('stroke', color);
                    } else if (tag === 'text' && hasBox) {
                        // leave the label text's fill untouched — readable on the box
                    } else {
                        if (el.__origFill === undefined) el.__origFill = el.getAttribute('fill');
                        el.setAttribute('fill', color);
                    }
                    // SVG has no z-index — z-order is document order. The stem
                    // (and beam) are drawn after the notehead, so a recolored
                    // head would be partly covered by the black stem. Raise the
                    // highlighted element to the end of its group so it paints
                    // on top; remember its slot to restore on clear.
                    if (!el.__origParent && el.parentNode) {
                        el.__origParent = el.parentNode;
                        el.__origNextSibling = el.nextSibling;
                        el.parentNode.appendChild(el);
                    }
                });
            }
            return painted;
        }

        function escapeHtml(text) {
            return text
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;');
        }

        function updateRenderSelect(renders, selectedRender) {
            // One entry per score. Display the LABEL (the score's name, or (Default) for the
            // unnamed one). The VALUE is the output name / preview selector: empty for the
            // unnamed score, which exports to the source .lys filename.
            renderSelect.innerHTML = '';
            const scoreRenders = (renders || []).filter(r => r.Type === 'score');
            scoreRenders.forEach(render => {
                const option = document.createElement('option');
                option.value = render.Filename;
                option.textContent = render.Name;
                if (option.value === (selectedRender || '')) {
                    option.selected = true;
                }
                renderSelect.appendChild(option);
            });
            fitSelectToSelection();
        }

        // The select is sized to its CONTENT. Left to itself a native <select>
        // reserves room for its widest option, so one long score name leaves a
        // wide empty gap next to every short one.
        //
        // The width is measured by an off-screen twin rather than computed from
        // the text: a select's box is text + padding + border + the dropdown
        // arrow Chromium draws itself, and only Chromium knows what that arrow
        // costs. The twin carries the same class (so the same font and padding)
        // and is left at its auto width, which IS the width that fits its
        // options exactly. Absolutely positioned, so it never disturbs the
        // toolbar it has to live in to inherit those styles.
        const selectSizer = document.createElement('select');
        selectSizer.setAttribute('aria-hidden', 'true');
        selectSizer.tabIndex = -1;
        selectSizer.style.cssText =
            'position:absolute;visibility:hidden;pointer-events:none;top:0;left:0;width:auto;';
        document.querySelector('.toolbar').appendChild(selectSizer);

        // Width the twin settles on once it holds exactly these option labels.
        function widthFor(labels) {
            selectSizer.innerHTML = '';
            for (const label of labels) {
                const o = document.createElement('option');
                o.textContent = label;
                selectSizer.appendChild(o);
            }
            return selectSizer.offsetWidth;
        }

        function fitSelectToSelection() {
            const opt = renderSelect.options[renderSelect.selectedIndex];
            renderSelect.style.width = widthFor([opt ? opt.textContent : '']) + 'px';
        }

        // The native popup takes its width from the element, so the list would be
        // clipped to the (short) closed width. Widening just before it opens —
        // mousedown and the keyboard open keys both precede the popup — gives the
        // list room for the longest name; it shrinks back on close.
        function fitSelectToWidest() {
            const labels = Array.from(renderSelect.options).map(o => o.textContent);
            renderSelect.style.width = widthFor(labels) + 'px';
        }

        renderSelect.addEventListener('mousedown', fitSelectToWidest);
        renderSelect.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') {
                fitSelectToSelection();   // closed without choosing
            } else if (e.key === 'Enter' || e.key === ' ' || e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                fitSelectToWidest();
            }
        });
        // change = a choice was made, blur = the popup went away with the focus.
        renderSelect.addEventListener('change', fitSelectToSelection);
        renderSelect.addEventListener('blur', fitSelectToSelection);
        // Fit the placeholder too: the first score list only arrives with the
        // first render, and until then the box would sit at its auto width.
        fitSelectToSelection();

        renderSelect.addEventListener('change', () => {
            vscode.postMessage({ type: 'selectRender', renderName: renderSelect.value });
        });

        // A narrow preview wraps the header's groups onto rows: a separator whose two groups
        // stand on different rows would dangle at a row's end, so it hides.
        (() => {
            const bar = document.querySelector('.toolbar');
            const tidy = () => {
                // One at a row's start leaves the layout (the row above had no room for it, so
                // it has none for the group either); one at a row's end only goes unseen.
                for (const sep of bar.querySelectorAll(':scope > .sep')) {
                    sep.style.display = '';
                    sep.style.visibility = '';
                }
                for (const sep of bar.querySelectorAll(':scope > .sep')) {
                    const a = sep.previousElementSibling, b = sep.nextElementSibling;
                    if (!a || !b || Math.abs(a.offsetTop - b.offsetTop) <= 4) continue;
                    if (Math.abs(sep.offsetTop - b.offsetTop) <= 4) sep.style.display = 'none';
                    else sep.style.visibility = 'hidden';
                }
            };
            if (window.ResizeObserver) new ResizeObserver(tidy).observe(bar);
            tidy();
        })();
        // The page's own tooltip (owner's report 2026-10-25: the browser's, which a title
        // shows, was too small to read and no CSS reaches it): the same words, in VS Code's
        // hover font and colours. A title — an attribute, or an SVG <title> child (the
        // tessitura chart's) — moves to data-tip when the pointer first comes over it, so the
        // browser's never shows; one set again later (updateAiButton) moves again. A red microphone
        // says what went wrong at once, without the wait (owner's request 2026-11-09, session 1191).
        let refreshTip = () => {};
        (() => {
            const tip = document.createElement('div');
            tip.className = 'tip';
            tip.hidden = true;
            document.body.appendChild(tip);
            let timer = null, shownFor = null;
            const tipOf = (el) => {
                for (let e = el; e && e !== document.body; e = e.parentNode) {
                    if (!e.getAttribute) continue;
                    if (e.hasAttribute('title')) {
                        e.setAttribute('data-tip', e.getAttribute('title'));
                        e.removeAttribute('title');
                    }
                    // Not the score's own: a drawing's <title> names the drawing, not a control.
                    const t = !svgContainer.contains(e) && e.querySelector && e.querySelector(':scope > title');
                    if (t) { e.setAttribute('data-tip', t.textContent); t.remove(); }
                    if (e.hasAttribute('data-tip') && e.getAttribute('data-tip')) return e;
                }
                return null;
            };
            const hide = () => { clearTimeout(timer); timer = null; tip.hidden = true; shownFor = null; };
            const show = (el) => {
                tip.textContent = el.getAttribute('data-tip');
                tip.hidden = false;
                const r = el.getBoundingClientRect();
                const w = tip.offsetWidth, h = tip.offsetHeight;
                let x = Math.min(Math.max(4, r.left + r.width / 2 - w / 2), window.innerWidth - w - 4);
                let y = r.bottom + 6;
                if (y + h > window.innerHeight - 4) y = Math.max(4, r.top - h - 6);
                tip.style.left = x + 'px';
                tip.style.top = y + 'px';
                shownFor = el;
            };
            const atOnce = (el) => el.dataset && el.dataset.state === 'failed' && el.classList.contains('voice-badge');
            document.addEventListener('pointerover', (ev) => {
                const el = tipOf(ev.target);
                if (el === shownFor) return;
                hide();
                if (!el) return;
                if (atOnce(el)) { show(el); return; }
                timer = setTimeout(() => show(el), 500);
            });
            // The element's words changed under a resting pointer, which sends no pointerover.
            refreshTip = (el) => {
                const showing = shownFor === el;
                const over = !el.hidden && el.matches(':hover') && (showing || atOnce(el));
                if (showing || over) hide();
                if (over && tipOf(el) === el) show(el);
            };
            document.addEventListener('pointerdown', hide, true);
            document.addEventListener('keydown', hide, true);
            document.addEventListener('scroll', hide, true);
            window.addEventListener('blur', hide);
            document.documentElement.addEventListener('pointerleave', hide);
        })();
        const exportBtn = document.getElementById('exportBtn');
        exportBtn.addEventListener('click', () => {
            vscode.postMessage({ type: 'export', renderName: renderSelect.value });
        });

        // ---- Playback and audition ----
        // The sound is the SoundFont synth (out/synth/preview-synth.js = window.LysSynth,
        // spessasynth_lib's worklet; docs/playback-design.md), fed the server's note list a
        // few hundred milliseconds ahead of the clock; the oscillator patches further down
        // are the fallback while no sound font is loaded. ONE AudioContext for the page,
        // made at boot and never closed. The browser's autoplay policy lets it run only
        // once the user has clicked or typed in THIS document (the editor's keys do not
        // count): until then it stays 'suspended' — so it is resumed from the first gesture
        // here and asked to resume at every strike, and if it is still not running shortly
        // after, the host is told once (audioBlocked) and the status bar says to click the
        // preview. The Play button is a click, so it always gets through.
        let audioCtx = null;
        let audioBlockedTold = false;
        function audioContext() {
            if (!audioCtx || audioCtx.state === 'closed') { audioCtx = new AudioContext(); }
            return audioCtx;
        }
        function audio() {
            const ctx = audioContext();
            if (ctx.state !== 'running') {
                ctx.resume().catch(() => { /* reported below */ });
                setTimeout(() => {
                    if (audioCtx && audioCtx.state !== 'running' && !audioBlockedTold) {
                        audioBlockedTold = true;
                        vscode.postMessage({ type: 'audioBlocked', state: audioCtx.state });
                    }
                }, 300);
            }
            return ctx;
        }
        function resumeAudioOnGesture() {
            if (audioCtx && audioCtx.state === 'suspended') {
                audioCtx.resume().then(() => { audioBlockedTold = false; }).catch(() => { /* noop */ });
            }
        }
        window.addEventListener('pointerdown', resumeAudioOnGesture, true);
        window.addEventListener('keydown', resumeAudioOnGesture, true);

        function flashInfo(text, ms) {
            zoomInfo.textContent = text;
            zoomInfo.classList.add('visible');
            setTimeout(() => zoomInfo.classList.remove('visible'), ms || 2500);
        }

        // The SoundFont synth, created once at boot so the font is parsed before the first
        // Play (a suspended context loads it fine). window.lysSynthConfig carries the URLs
        // the extension gave this page (the head script); no bundle or no processor URL
        // means no synth, and the page plays its oscillator patches.
        let sfSynth = null;
        let noSynthTold = false;
        function soundFontSynth() {
            if (sfSynth) { return sfSynth; }
            const cfg = window.lysSynthConfig;
            if (!window.LysSynth || !cfg || !cfg.processor) {
                if (!noSynthTold) {
                    noSynthTold = true;
                    vscode.postMessage({ type: 'synthStatus', state: 'failed', detail: !window.LysSynth
                        ? 'the synth bundle did not load (window.LysSynth is undefined)' : 'no processor URL' });
                }
                return null;
            }
            sfSynth = window.LysSynth.create(audioContext(), cfg.processor, cfg.soundFont || '', (status) => {
                vscode.postMessage({ type: 'synthStatus', state: status.state, detail: status.detail || '' });
                if (status.state === 'failed') {
                    flashInfo('Sound: ' + (status.detail || 'the sound font did not load'), 4000);
                }
            });
            return sfSynth;
        }
        setTimeout(() => {
            try { soundFontSynth(); }
            catch (e) { vscode.postMessage({ type: 'synthStatus', state: 'failed', detail: String(e) }); }
        }, 0);
        // lilysharp.playback.soundFont changed: the extension sends the new URL ('' = none).
        function setSoundFont(uri) {
            if (!window.lysSynthConfig) { window.lysSynthConfig = {}; }
            window.lysSynthConfig.soundFont = uri || '';
            const sf = soundFontSynth();
            if (sf) { sf.setSoundFont(uri || ''); }
        }
        // MIDI out (docs/playback-design.md §6): the name of the port the sounds go to —
        // lilysharp.playback.midiOutput — or '' for the synth here. The page still decides WHAT
        // plays (the note list, the offset, an audition's pitches); the server is the port
        // and answers with the wall-clock time of the playback's zero, which the highlight
        // follows (Date.now() is the same clock). A changed setting stops what plays.
        let midiOutput = (window.lysSynthConfig && window.lysSynthConfig.midiOutput) || '';
        function setMidiOutput(name) {
            stopPlayback();
            stopHeldNote();
            midiOutput = name || '';
        }

        // ---- The singing voice (docs/singing-design.md §5): the host renders each sung part's
        // words through the singing engine and hands the WAV here (the 'voice' message); a Play
        // starts the buffer at the same clock as the synth's notes, through a limiter of its own,
        // IN PLACE OF the part's instrument (owner's decision 2026-11-08: one sound a part — a
        // part a lyrics row sings is a singer, and its instrument is the stand-in while there is
        // no voice: no engine, kana missing, not rendered yet, or the menu's ♪ switched to the
        // instrument). A voice is for one practice speed; at another the instrument plays until
        // the host has sung it anew. A stretch's voice stops where the stretch ends and a loop
        // sings it round after round (LysSynth.voiceCues); a candidate's notes play without the
        // voice (voicePlan null).
        const voices = new Map();           // part -> { buffer, speed, key }
        let voiceSources = [];
        // Where the voice stands for the played list: null = no voice; { pieces: null } the whole
        // piece from its 0; { pieces, rounds } a stretch — the score's times it plays, in order
        // (spanOf) — laid out rounds times (the loop's, looped).
        let voicePlan = { pieces: null, rounds: 1 };
        let voiceOut = null;
        function voiceOutput(ctx) {
            if (!voiceOut) {
                const gain = ctx.createGain();
                gain.gain.value = 0.9;
                const limiter = ctx.createDynamicsCompressor();
                limiter.threshold.value = -10; limiter.knee.value = 12; limiter.ratio.value = 12;
                limiter.attack.value = 0.002; limiter.release.value = 0.12;
                gain.connect(limiter);
                limiter.connect(ctx.destination);
                voiceOut = gain;
            }
            return voiceOut;
        }
        // A chosen voice's short phrase: the next one cuts it off (session 1170).
        let voiceSampleSource = null;
        function playVoiceSample(message) {
            const got = Date.now();
            const bytes = message.bytes;
            if (!bytes || !bytes.buffer) { return; }
            const copy = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
            const ctx = audio();
            ctx.decodeAudioData(copy).then(buffer => {
                const decoded = Date.now();
                if (voiceSampleSource) { try { voiceSampleSource.stop(); } catch (e) { /* ended */ } }
                voiceSampleSource = null;
                // Not while the page's audio may not run (owner's report 2026-11-11: a voice taken
                // before the preview had been clicked said nothing, and its phrase came out over
                // the song at the next Play). A source started on a suspended context is not
                // dropped: its start is the context's frozen time, so it sounds the moment a
                // click — the Play button's — lets the context run. The phrase is for the moment
                // the voice is chosen; later it is noise. audio() has asked the context to resume
                // and tells the host when it will not (the status bar says to click the preview).
                // Nor over a Play that began while the phrase was on its way.
                if (ctx.state !== 'running' || playClock !== null || midiPlaying) {
                    vscode.postMessage({ type: 'webviewPerf', text: 'voice sample ' + message.id + ': not said ('
                        + (ctx.state !== 'running' ? 'AudioContext ' + ctx.state : 'the score is playing') + ')' });
                    return;
                }
                const src = ctx.createBufferSource();
                src.buffer = buffer;
                src.connect(voiceOutput(ctx));
                src.start();
                voiceSampleSource = src;
                const started = Date.now();
                vscode.postMessage({ type: 'webviewPerf', text: 'voice sample ' + message.id
                    + ': delivery ' + (got - message.sentAt) + ' ms, decode ' + (decoded - got) + ' ms, start ' + (started - decoded) + ' ms, '
                    + (started - message.pressedAt) + ' ms from the key; AudioContext ' + ctx.state
                    + ', output latency ' + Math.round(((ctx.baseLatency || 0) + (ctx.outputLatency || 0)) * 1000) + ' ms' });
            }, e => {
                vscode.postMessage({ type: 'webviewPerf', text: 'voice sample could not be decoded: ' + e });
            });
        }
        function setVoice(message) {
            const bytes = message.bytes;
            if (!bytes || !bytes.buffer) { return; }
            if (!sungNowHas(message.part)) { return; }  // a voice of a part this score does not sing (session 1160)
            const copy = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
            audioContext().decodeAudioData(copy).then(buffer => {
                // The buffer begins lead seconds before the score's 0 (VOICE_LEAD_S), with the
                // first syllable's consonant in that lead: pre is how much of it sounds before the
                // beat — what a Play from the top lets the instruments wait for (startPlayback).
                // (No backticks in this script: it lives in a template literal.)
                const lead = message.lead || 0;
                const s = lead > 0 && window.LysSynth && window.LysSynth.soundStart
                    ? window.LysSynth.soundStart(buffer.getChannelData(0), buffer.sampleRate, lead) : null;
                const sungBefore = sungTracks();
                voices.set(message.part, { buffer, speed: message.speed || 1, key: message.key, lead, pre: s === null ? 0 : Math.max(0, lead - s) });
                setVoiceState(message.part, 'ready');
                joinVoice(message.part, message.entries, sungBefore);
                joinLoop(message.part);
            }, e => {
                vscode.postMessage({ type: 'webviewPerf', text: 'voice: ' + message.part + ' could not be decoded: ' + e });
                setVoiceState(message.part, 'failed', 'the WAV could not be decoded');
            });
        }
        // pending: another document's voices are on their way (the host asked for them) — the
        // microphone stays grey as "preparing" until 'voiceParts' names them (or names none).
        // (No backticks in this script: it lives in a template literal.)
        // The placeholder lives until the host names the parts (voiceParts) or says it cannot
        // (voiceSettled) - and at most PLACEHOLDER_MS, whatever happened to that answer (owner's
        // report 2026-11-09, session 1161: a file with no lyrics opened with the microphone
        // pulsing for ever; the server answers in milliseconds, so a placeholder older than this
        // is a lost answer, not a slow one).
        const PLACEHOLDER_MS = 10000;
        let placeholderTimer = null;
        function clearVoices(pending) {
            // Voices taken away while they sing (the engine changed, the singers went): the list
            // sounding was laid out without their parts' instruments, so the parts fell silent to the
            // list's end - a loop's ten minutes (measured 2026-11-19 in a real VS Code: the voice
            // stopped, and no strike of its part went to the synth in the rounds after). What plays
            // is laid out again from here, the instruments standing in.
            const sang = voiceSources.length > 0;
            stopVoices(); voices.clear(); voiceStates.clear();
            sungNow = null;
            if (sang && playingNow()) resumeHere();
            if (placeholderTimer) { clearTimeout(placeholderTimer); placeholderTimer = null; }
            if (pending) {
                voiceStates.set('', { state: 'preparing' });
                placeholderTimer = setTimeout(() => {
                    placeholderTimer = null;
                    if (voiceStates.has('')) {
                        vscode.postMessage({ type: 'webviewPerf', text: 'voice: the placeholder outlived its answer (' + PLACEHOLDER_MS + ' ms) - settled' });
                        settleVoices();
                    }
                }, PLACEHOLDER_MS);
            }
            renderVoiceBadge();
        }
        // The parts the score sings now ('voiceParts', each time the host asks lilysharp/sung): a
        // voice or a state of any other part is from an earlier score or an earlier text of this
        // one, and goes — so a score with no lyrics shows no microphone. Kept (sungNow) so that a
        // voice or a state arriving late for any other part is refused too (session 1160: the
        // host's late 'preparing' after a switch to a file with no lyrics pulsed for ever).
        let sungNow = null;     // null: not named yet since the last clearVoices
        function keepVoices(parts) {
            const keep = new Set(parts || []);
            sungNow = keep;
            if (placeholderTimer) { clearTimeout(placeholderTimer); placeholderTimer = null; }
            for (const part of [...voices.keys()]) { if (!keep.has(part)) { voices.delete(part); } }
            for (const part of [...voiceStates.keys()]) { if (!keep.has(part)) { voiceStates.delete(part); } }
            renderVoiceBadge();
        }
        function sungNowHas(part) { return sungNow === null || sungNow.has(part); }
        // The pane's sign of the voices (owner's request 2026-11-08): what the host says of each
        // (preparing, failed — the 'voiceState' message) and what this page decoded (ready), shown
        // as one badge beside the transport (playbackCore.voiceBadge) and as a sign on each
        // voice's toggle in the practice menu.
        const voiceStates = new Map();      // voice key -> { state, detail }
        let menuVoiceKeys = [];             // the voices the practice menu lists (partsData)
        let menuVoiceNames = {};            // what its lines call them
        function setVoiceState(part, state, detail, estimateMs, singing, key) {
            if (!sungNowHas(part)) { return; }      // a part this score does not sing: a late word of another text
            if (state === 'ready' || state === 'failed') { voiceBarFrom = Date.now(); }     // the next one's time begins
            // A voice told waiting or preparing is of a text that is gone (the host compares the
            // keys): the buffer goes with it, so a Play meanwhile plays the instrument, not the
            // old words (session 1148).
            if (state === 'waiting' || state === 'preparing') { voices.delete(part); }
            const before = voiceStates.get(part) || {};
            // What the engine said of the render (told) stays while the word is of the same render
            // (its key): a Play pressed meanwhile repeats "preparing", and the count must not go back.
            const told = state === 'preparing' && before.told && key && before.told.key === key ? before.told : undefined;
            voiceStates.set(part, { state, detail, estimateMs: estimateMs || before.estimateMs, singing: window.LysSynth.voiceStillSinging(before.singing, state, singing), told });
            renderVoiceBadge();
        }
        // The engine's own count of a voice it is singing (the host's 'voiceProgress'; owner's
        // request 2026-11-11): the seconds to sing ANEW and how many are done. The bar is counted
        // on these, not on the score's length.
        function setVoiceTold(message) {
            const s = voiceStates.get(message.part);
            if (!s || s.state !== 'preparing' || !sungNowHas(message.part)) { return; }
            const now = Date.now();
            const same = s.told && s.told.key === message.key && message.sung > 0;
            s.told = { key: message.key, toSing: message.toSing, sung: message.sung, at: now, firstAt: same ? s.told.firstAt : now, msPerSecond: message.msPerSecond || 0 };
            s.singing = true;
            renderVoiceBadge();
        }
        // The bar under Play, for as long as the engine is singing a voice (voiceBarShown).
        let voiceBarTimer = null, voiceBarStarted = 0, voiceBarFrom = 0;
        // The voices this showing of the bar counts (voiceRound: the ones being sung now, not the
        // ones that were ready before it came up), and how far it has been drawn: it never goes back.
        let voiceBarRound = [], voiceBarDrawn = 0;
        function voiceBarParts() {
            return [...voiceStates].map(([part, s]) => ({ part, state: s.state, singing: s.singing, estimateMs: s.estimateMs, told: s.told }));
        }
        function paintVoiceBar() {
            const bar = document.getElementById('voiceBar');
            const now = Date.now();
            bar.hidden = false;
            const parts = voiceBarParts();
            voiceBarRound = window.LysSynth.voiceRound(voiceBarRound, parts, true);
            voiceBarDrawn = Math.max(voiceBarDrawn, window.LysSynth.voiceBar(parts.filter(p => voiceBarRound.includes(p.part)), now, now - voiceBarFrom));
            bar.firstElementChild.style.width = Math.round(voiceBarDrawn * 100) + '%';
            paintVoiceRows();
        }
        // Each voice's own bar in the practice menu, for as long as the engine is singing it.
        function paintVoiceRows() {
            const states = voiceBarParts();
            for (const btn of document.querySelectorAll('#partsList button.voice')) {
                const bar = btn.querySelector('.voice-row-bar');
                if (!bar) { continue; }
                const done = window.LysSynth.voiceRowProgress(states, btn.dataset.part, Date.now() - voiceBarFrom, Date.now());
                bar.hidden = done === null;
                bar.firstElementChild.style.width = done === null ? '0' : Math.round(done * 100) + '%';
            }
        }
        function renderVoiceBar(singing) {
            const bar = document.getElementById('voiceBar');
            if (!singing) {
                if (voiceBarTimer) { clearInterval(voiceBarTimer); voiceBarTimer = null; }
                voiceBarStarted = 0;
                voiceBarRound = [];
                voiceBarDrawn = 0;
                bar.hidden = true;
                bar.firstElementChild.style.width = '0';
                return;
            }
            if (!voiceBarStarted) {
                voiceBarStarted = voiceBarFrom = Date.now();
                voiceBarRound = window.LysSynth.voiceRound([], voiceBarParts(), false);
                voiceBarDrawn = 0;
            }
            if (!voiceBarTimer) { voiceBarTimer = setInterval(paintVoiceBar, 100); }
            paintVoiceBar();
        }
        // The host asked for the voices and got none to name (the text has an error, the server
        // is not up): the nameless placeholder of clearVoices goes, what is known stays.
        function settleVoices() {
            if (placeholderTimer) { clearTimeout(placeholderTimer); placeholderTimer = null; }
            voiceStates.delete('');
            renderVoiceBadge();
        }
        function renderVoiceBadge() {
            const badge = document.getElementById('voiceBadge');
            const all = [...voiceStates].map(([part, s]) => ({ part: menuVoiceNames[part] || part, state: s.state, detail: s.detail }));
            const b = window.LysSynth.voiceBadge(all);
            renderVoiceBar(window.LysSynth.voiceBarShown([...voiceStates.values()]));
            badge.hidden = !b;
            if (b) {
                badge.dataset.state = b.state;
                badge.title = b.title;
                // (The page script lives in a template literal: a backslash-n here would be a real newline.)
                badge.setAttribute('aria-label', b.title.split(String.fromCharCode(10))[0]);
            } else {
                // Nothing to say: no state left on the element either (session 1162).
                delete badge.dataset.state;
                badge.removeAttribute('title');
                badge.removeAttribute('data-tip');
                badge.setAttribute('aria-label', 'Voice');
            }
            refreshTip(badge);
            for (const btn of document.querySelectorAll('#partsList button.voice')) {
                const s = voiceStates.get(btn.dataset.part);
                const mic = btn.querySelector('.voice-badge');
                if (mic && s) { mic.dataset.state = s.state; } else if (mic) { delete mic.dataset.state; }
                const base = btn.dataset.title || '';
                btn.title = s ? base + (s.state === 'ready' ? ' — ready' : s.state === 'preparing' ? ' — being sung…' : s.state === 'waiting' ? ' — not sung yet' : ' — not sung' + (s.detail ? ': ' + s.detail : '')) : base;
            }
            paintVoiceRows();
        }
        // The badge sits on the corner of the Play button and takes the clicks that land there: they
        // are Play's (owner's report 2026-11-09, session 1149: a Play on that corner opened the
        // practice menu, which the badge used to open, instead of playing). The badge is a status,
        // not a button; its title says what it knows.
        document.getElementById('voiceBadge').addEventListener('click', () => document.getElementById('playBtn').click());
        // A part's voices, one for each singer line: the part's name for the first, part#2 and on for the rest.
        function voiceKeysOf(part) { return [...voices.keys()].filter(k => k === part || k.startsWith(part + '#')); }
        // The voices that sound now, each with its own switch and fader. A voice whose fader stands
        // at 0 is as switched off: its instrument stands in. The score's own voice sings unless
        // switched off; one the score does not sing only when the menu turned it on (voicesOn,
        // session 1163).
        function soundingVoices() {
            const keys = [];
            if (voicePlan === null || !rawParts) { return keys; }
            for (const name of rawParts) {
                for (const key of voiceKeysOf(name)) {
                    const v = voices.get(key);
                    const own = !rawSung || rawSung.includes(name);
                    const e = eff(), on = own ? !e.voicesOff.includes(key) : e.voicesOn.includes(key);
                    if (Math.abs(v.speed - practice.speed) < 1e-6 && on && levelIn(e.voiceLevels, key) > 0) { keys.push(key); }
                }
            }
            return keys;
        }
        // The tracks a voice stands in for now: a part with at least one voice sounding.
        function sungTracks() {
            const set = new Set();
            if (!rawParts) { return set; }
            const sounding = soundingVoices();
            rawParts.forEach((name, i) => { if (sounding.some(k => k === name || k.startsWith(name + '#'))) { set.add(i); } });
            return set;
        }
        // Every voice the menu lists for a part, whether its sound has come yet.
        function menuVoicesOf(part) {
            const listed = menuVoiceKeys.filter(k => k === part || k.startsWith(part + '#'));
            return listed.length > 0 ? listed : [part];
        }
        function stopVoices() {
            for (const s of voiceSources) { try { s.stop(); } catch (e) { /* ended */ } }
            voiceSources = [];
        }
        // A voice that lands while its part already plays on the instrument standing in for it
        // (owner's request 2026-11-11) comes in at the next note it sings - no rest is waited
        // for (the first build did; the owner's song had few). The instrument is struck no
        // more from there (the tracks that sounded without the voice and would not with it,
        // instrumentSounds), and the voice is cut in a consonant's worth before the beat, or
        // with what room a rest gives, fading in over a moment so that a vowel cut into does
        // not click. entries are the sung notes, in the score's seconds (voiceEntries); the
        // list's time is the score's at the voice's speed, after a count-in.
        // Left to the next Play: a stretch or a loop (their voices are cut by voiceCues), the
        // MIDI output and the plain tones (their instruments cannot be taken back), a Play with
        // no sung note left. (No backticks in this script: it lives in a template literal.)
        //
        // ANOTHER voice of the part may be singing already (owner's report 2026-11-11: the singer
        // changed while the score played - the new voice was ready and the old one sang on to
        // the end): it stops where the new one comes in, and no instrument is taken back, since
        // none stood in. The score was edited under the Play then (playEdited), so the voice is
        // the new text's and the Play the old one's: it is cut in only where it still fits -
        // every note it sings beginning with a note of its part in the list played. A singer
        // changed leaves the notes as they were; a rhythm rewritten waits for the next Play.
        let playingNotes = null;    // the list startPlayback sounds
        let playEdited = false;     // the score changed since that Play began
        function voiceFitsPlay(key, list) {
            if (!playEdited) { return true; }
            const track = rawParts ? rawParts.indexOf(key.split('#')[0]) : -1;
            const onsets = playingNotes.filter(n => n.N === track && !(n.P < 0) && !n.L && !n.Z).map(n => n.T);
            if (onsets.length === 0) { return false; }
            return list.every(e => e.at < playOffset - 1e-6 || onsets.some(t => Math.abs(t - e.at) < 0.002));
        }
        function joinVoice(key, entries, sungBefore) {
            if (playClock === null || !playingNotes || midiPlaying || loopList || !voicePlan || voicePlan.pieces
                || !audioCtx || !window.LysSynth || !window.LysSynth.nextVoiceEntry || !window.LysSynth.voiceEntryEarly) { return; }
            if (!soundingVoices().includes(key)) { return; }
            const v = voices.get(key);
            const list = (entries || []).map(e => ({ at: countInFix + e.at / v.speed, room: e.room / v.speed }));
            if (!voiceFitsPlay(key, list)) { return; }
            const replaced = voiceSources.filter(s => s.voiceKey === key);
            const sungAfter = sungTracks();
            const tracks = new Set();
            for (const n of (replaced.length > 0 ? [] : playingNotes)) {
                if (typeof n.N === 'number' && !tracks.has(n.N) && instrumentSounds(n.N, sungBefore) && !instrumentSounds(n.N, sungAfter)) { tracks.add(n.N); }
            }
            if (tracks.size > 0 && !sfPlayer) { return; }
            const now = playOffset + playClock();
            const entry = window.LysSynth.nextVoiceEntry(list, 0, now, sfPlayer ? playOffset + sfPlayer.handedOutUntil : -1);
            if (!entry) { return; }
            const lead = v.lead || 0;
            const early = window.LysSynth.voiceEntryEarly(entry, lead, entry.at - now);
            const t0 = audioCtx.currentTime - playClock();
            const when = t0 + (entry.at - playOffset) - early;
            // The menu's fader for the voice (at most 1 here, as startVoices), faded in.
            const level = Math.min(1, levelIn(eff().voiceLevels, key));
            const g = audioCtx.createGain();
            g.gain.setValueAtTime(0, when);
            g.gain.linearRampToValueAtTime(level, when + 0.03);
            g.connect(voiceOutput(audioCtx));
            const src = audioCtx.createBufferSource();
            src.buffer = v.buffer;
            src.connect(g);
            src.start(when, lead + (entry.at - countInFix) - early);
            src.voiceKey = key;
            // The voice that sang this part until now ends as the new one fades in.
            for (const old of replaced) { try { old.stop(when + 0.03); } catch (e) { /* ended */ } }
            voiceSources.push(src);
            if (tracks.size > 0) { sfPlayer.silence(playingNotes.filter(n => tracks.has(n.N) && !n.U && n.T >= entry.at - 1e-6)); }
            flashInfo('The voice of ' + (menuVoiceNames[key] || key) + ' comes in', 3000);
        }
        // A voice that lands while a LOOP goes round, which joinVoice leaves to the next Play: the
        // rounds are laid out ten minutes ahead with the part's instrument standing in, so the
        // voice was not heard until Play was pressed again (measured 2026-11-19 in a real VS Code:
        // a four-bar loop, the voice ready in its first round, no voice in the rounds after). The
        // round sounding plays to its end and the next begins with the voice - the loop laid out
        // again there, as a loop takes an edit (refreshLoop, whose switch this one gives way to: it
        // starts with the voice too). Not on the MIDI output, as joinVoice.
        function joinLoop(key) {
            if (playClock === null || !playingNotes || midiPlaying || !loopOn || !loopList || lastSpan === undefined || !(loopRound > 0)) { return; }
            if (!soundingVoices().includes(key) || loopSwitchTimer) { return; }
            const period = loopRound + loopGap, lt = Math.max(0, playOffset + playClock());
            let remain = period - (lt % period);
            if (remain < 0.25) remain += period;
            const gen = playGeneration, list = loopList;
            // (a new list starts 0.15 s after it is handed over: handed over so much before the round's end)
            loopSwitchTimer = setTimeout(() => {
                loopSwitchTimer = null;
                if (gen !== playGeneration || !playingNow() || loopList !== list) return;
                const rounds = looped(list);
                voicePlan = loopVoicePlan();
                startPlayback(rounds, 0, false, false, loopGap, spanStart);
                loopList = list;
                flashInfo('The voice of ' + (menuVoiceNames[key] || key) + ' comes in', 3000);
            }, Math.max(0, (remain - 0.15) * 1000));
        }
        // Starts every voice so that the list's time listTime sounds at the context's time when:
        // after a count-in (countInFix), each stretch of the score the list plays cut from the
        // buffer (voiceCues) — a selected stretch stops at its end, a loop's rounds each sing it again.
        // How long the instruments wait for the voices' consonants before the beat on a Play from
        // the score's 0 with no count-in (the count-in gives the room itself; a start inside the
        // score has the previous syllable before it, not a consonant in silence).
        function voicePreroll(offset, countFix) {
            if ((countFix || 0) > 0 || offset > 0 || voicePlan === null) { return 0; }
            if (voicePlan.pieces && !(voicePlan.pieces.length > 0 && voicePlan.pieces[0][0] < 1e-6)) { return 0; }
            let pre = 0;
            for (const key of soundingVoices()) { pre = Math.max(pre, voices.get(key).pre || 0); }
            return pre;
        }
        // Whether track i's instrument sounds in this Play (session 1165): a part the score only
        // sings keeps its instrument off unless the menu turned it on, standing in only while its
        // voice is not there (not sung now: off, not ready, or its fader at 0); every other part's
        // instrument sounds (the muted and the extras left off are already out of the list).
        function unsungSounds(i) {
            if (!rawParts || typeof i !== 'number' || i < 0) { return true; }
            const name = rawParts[i];
            { const e = eff(); return e.instrumentsOn.includes(name) || !menuVoicesOf(name).every(k => e.voicesOff.includes(k)); }
        }
        function instrumentSounds(i, sung) {
            if (!rawParts || typeof i !== 'number' || i < 0) { return true; }
            const name = rawParts[i];
            const sungOnly = rawSung && rawSung.includes(name) && !(rawPlayed && rawPlayed.includes(name));
            if (!sungOnly) { return true; }
            const e = eff();
            if (e.instrumentsOn.includes(name)) { return true; }
            if (menuVoicesOf(name).every(k => e.voicesOff.includes(k))) { return false; }    // both switched off: silence
            return !sung.has(i);      // the stand-in, until the voice is there
        }
        // Where the voices end in the list's time: the whole buffer after the count-in's lead, or
        // the plan's pieces laid out their rounds (for a Play with no note left to end on).
        function voicesEnd() {
            let end = 0;
            if (voicePlan === null) { return 0; }
            for (const key of soundingVoices()) {
                const v = voices.get(key);
                const seconds = v.buffer.duration - (v.lead || 0);
                if (!voicePlan.pieces) { end = Math.max(end, countInFix + seconds); }
                else {
                    const length = voicePlan.pieces.reduce((s, p) => s + (p[1] - p[0]), 0);
                    const rounds = Math.max(1, voicePlan.rounds || 1);
                    end = Math.max(end, countInFix + Math.min(seconds, length) * rounds + (voicePlan.gap || 0) * (rounds - 1));
                }
            }
            return end;
        }
        function startVoices(ctx, when, listTime, room) {
            stopVoices();
            const sounding = soundingVoices();
            if (sounding.length === 0) { return; }
            if (!window.LysSynth || !window.LysSynth.voiceCues || !window.LysSynth.withVoiceLead) {
                vscode.postMessage({ type: 'webviewPerf', text: 'voice: the synth script is an old build (no voiceCues / withVoiceLead) - reload the window' });
                return;
            }
            for (const key of sounding) {
                const v = voices.get(key);
                const lead = v.lead || 0;
                // (a first round from the A flag: nothing sung before A — voicePlan.entry)
                const from = Math.max(listTime, voicePlan.entry || 0);
                const cues = window.LysSynth.voiceCues(from, countInFix, voicePlan.pieces, voicePlan.rounds, v.buffer.duration - lead, voicePlan.gap || 0)
                    .map(c => from === listTime ? c : Object.assign({}, c, { at: c.at + from - listTime }));
                // The menu's fader for the voice: a gain of its own before the voices' output.
                const level = levelIn(eff().voiceLevels, key);
                let out = voiceOutput(ctx);
                if (level < 1) {
                    const g = ctx.createGain();
                    g.gain.value = level;
                    g.connect(out);
                    out = g;
                }
                for (const cue of window.LysSynth.withVoiceLead(cues, lead, v.pre || 0, room || 0)) {
                    const src = ctx.createBufferSource();
                    src.buffer = v.buffer;
                    src.connect(out);
                    if (cue.duration === undefined) { src.start(when + cue.at, cue.offset); }
                    else { src.start(when + cue.at, cue.offset, cue.duration); }
                    src.voiceKey = key;     // which voice sings here, for one that replaces it (joinVoice)
                    voiceSources.push(src);
                }
            }
        }

        let playingOscs = [];
        let playEndTimer = null;
        let playheadTimer = null;
        let relaying = false;       // the current playback is a candidate's, followed for its panel
        let sourceLit = false;      // the host was told what sounds, for the editor to light its text
        let tickWatch = null;       // the follow-along tick's evenness, for the log (followAlong)
        let followTick = null;      // the follow-along's tick while it runs (the host's followTick calls it too)
        let sfPlayer = null;       // the SoundFont player of the current playback
        let midiPlaying = false;    // the current playback is the server's, on the MIDI output
        let midiPlayPending = null; // a Play sent to the server, awaiting its start (generation, notes, offset)
        let playbackNotes = null;   // last received event list — enables click-to-seek
        // A Play with bars selected plays them (owner's request 2026-10-25): the selection's
        // source range, kept until the notes come. The header's loop plays the stretch (or the
        // whole score) round and round, its repeats ignored — loopList is what goes round.
        let pendingSpan = null;
        let loopOn = false;
        let loopList = null;
        let pendingStartBar = -1;   // a start point by a printed bar, the A flag's (Play; barStartIn)

        // ---- The Range panel (owner's request 2026-10-25): each part's lowest and highest note
        // (lilysharp/ranges — what Play sounds), to check a part's range and find a singer's top
        // note at once. A click on one shows it on the score and puts the editor's caret on it; a
        // pitch written in several places goes to the next on each click. Asked again after
        // every render while open.
        const rangeBtn = document.getElementById('rangeBtn');
        const rangePanel = document.getElementById('rangePanel');
        const rangeBody = document.getElementById('rangeBody');   // what the panel says, under its ×
        let rangeParts = null;      // the last answer: [{ Name, Low, High }], each end { Pitch, Name, Positions }
        let rangeError = null;
        const rangeAt = {};         // 'part|Low' / 'part|High' -> the index of the place shown
        let rangeCur = null;        // the key of the end shown last
        function refreshRanges() {
            if (!rangePanel.hidden) vscode.postMessage({ type: 'requestRanges' });
        }
        rangeBtn.addEventListener('click', () => {
            rangePanel.hidden = !rangePanel.hidden;
            rangeBtn.setAttribute('aria-pressed', String(!rangePanel.hidden));
            if (rangePanel.hidden) return;
            raisePanel(rangePanel);
            if (rangePanel.dataset.moved) clampPanel(rangePanel);
            else rangePanel.style.top = (document.querySelector('.toolbar').getBoundingClientRect().bottom + 6) + 'px';
            if (!rangeParts && !rangeError) rangeBody.textContent = 'Reading the ranges…';
            refreshRanges();
        });
        // Its × closes it, as the Range button does (owner's request 2026-10-29).
        document.getElementById('rangeClose').addEventListener('click', () => {
            rangePanel.hidden = true;
            rangeBtn.setAttribute('aria-pressed', 'false');
        });
        // The bar a note is drawn in: the bar box over its x nearest it in y (a high note stands
        // above its staff's box).
        function barOfPos(pos) {
            sourcePositions();
            const el = (posElements.get(pos) || [])[0];
            if (!el) return null;
            const b = el.getBoundingClientRect();
            const cx = (b.left + b.right) / 2, cy = (b.top + b.bottom) / 2;
            let best = null, bestGap = Infinity;
            for (const r of svgContainer.querySelectorAll('rect.bar-box[data-bar]')) {
                const k = r.getBoundingClientRect();
                if (cx < k.left || cx > k.right) continue;
                const gap = cy < k.top ? k.top - cy : cy > k.bottom ? cy - k.bottom : 0;
                if (gap < bestGap) { best = r; bestGap = gap; }
            }
            return best ? best.getAttribute('data-bar') : null;
        }
        function renderRanges() {
            rangeBody.textContent = '';
            if (rangeError) { rangeBody.textContent = 'Range: ' + rangeError; return; }
            if (!rangeParts) return;
            if (rangeParts.length === 0) { rangeBody.textContent = 'No pitched notes in this score.'; return; }
            const table = document.createElement('table');
            const head = document.createElement('tr');
            for (const h of ['Part', 'Lowest', 'Highest', 'Tessitura']) {
                const th = document.createElement('th');
                th.textContent = h;
                head.appendChild(th);
            }
            table.appendChild(head);
            for (const p of rangeParts) {
                const tr = document.createElement('tr');
                const name = document.createElement('td');
                name.textContent = p.Name;
                tr.appendChild(name);
                tr.appendChild(rangeCell(p, 'Low'));
                tr.appendChild(rangeCell(p, 'High'));
                tr.appendChild(tessituraCell(p));
                table.appendChild(tr);
            }
            rangeBody.appendChild(table);
            const note = document.createElement('div');
            note.className = 'note';
            note.textContent = 'Sounding pitches · C4 = middle C · tessitura: where four-fifths of the time is';
            rangeBody.appendChild(note);
        }
        // The tessitura (owner's request 2026-10-25): where the part spends four-fifths of its
        // sounding time, named, over a small chart of that time on each semitone from its lowest
        // note to its highest — one hue, the tessitura's bars full and underlined, the rest
        // faint. Each column says its note and share on hover.
        const PITCH_NAMES = ['C', 'C♯', 'D', 'E♭', 'E', 'F', 'F♯', 'G', 'A♭', 'A', 'B♭', 'B'];
        const pitchName = (m) => PITCH_NAMES[((m % 12) + 12) % 12] + (Math.floor(m / 12) - 1);
        function tessituraCell(p) {
            const td = document.createElement('td');
            td.className = 'tess';
            const w = p.Weights || [];
            const label = document.createElement('div');
            label.textContent = p.Tessitura || '';
            td.appendChild(label);
            if (w.length === 0) return td;
            const ns = 'http://www.w3.org/2000/svg';
            const bar = Math.max(2, Math.min(5, Math.floor(180 / w.length) - 2)), gap = w.length > 40 ? 1 : 2, h = 22;
            const max = Math.max(...w);
            const low = Math.floor(p.Low.Pitch);
            const svg = document.createElementNS(ns, 'svg');
            svg.setAttribute('width', String(w.length * (bar + gap)));
            svg.setAttribute('height', String(h + 4));
            svg.setAttribute('class', 'tess-chart');
            w.forEach((v, i) => {
                const m = low + i, x = i * (bar + gap);
                const inside = m >= p.TessituraLow && m <= p.TessituraHigh;
                const bh = v > 0 ? Math.max(2, Math.round((v / max) * h)) : 0;
                if (bh > 0) {
                    const r = document.createElementNS(ns, 'rect');
                    r.setAttribute('x', String(x)); r.setAttribute('y', String(h - bh));
                    r.setAttribute('width', String(bar)); r.setAttribute('height', String(bh));
                    r.setAttribute('rx', '1');
                    r.setAttribute('class', inside ? 'tess-bar in' : 'tess-bar');
                    svg.appendChild(r);
                }
                // The column's hover target: its whole height, wider than the bar.
                const hit = document.createElementNS(ns, 'rect');
                hit.setAttribute('x', String(x - gap / 2)); hit.setAttribute('y', '0');
                hit.setAttribute('width', String(bar + gap)); hit.setAttribute('height', String(h + 4));
                hit.setAttribute('fill', 'transparent');
                const t = document.createElementNS(ns, 'title');
                t.textContent = pitchName(m) + ': ' + Math.round(v * 100) + '% of the time';
                hit.appendChild(t);
                svg.appendChild(hit);
            });
            const a = (p.TessituraLow - low) * (bar + gap), b = (p.TessituraHigh - low) * (bar + gap) + bar;
            const line = document.createElementNS(ns, 'rect');
            line.setAttribute('x', String(a)); line.setAttribute('y', String(h + 2));
            line.setAttribute('width', String(Math.max(bar, b - a))); line.setAttribute('height', '2');
            line.setAttribute('class', 'tess-line');
            svg.appendChild(line);
            td.appendChild(svg);
            return td;
        }
        function rangeCell(p, end) {
            const e = p[end];
            const key = p.Name + '|' + end;
            const n = e.Positions.length;
            const i = Math.min(rangeAt[key] || 0, Math.max(0, n - 1));
            const td = document.createElement('td');
            td.className = 'end' + (key === rangeCur ? ' cur' : '');
            const pitch = document.createElement('span');
            pitch.className = 'pitch';
            pitch.textContent = (end === 'High' ? '▲ ' : '▼ ') + e.Name;
            const bar = n > 0 ? barOfPos(e.Positions[i]) : null;
            const where = document.createElement('span');
            where.className = 'where';
            where.textContent = (bar ? '  bar ' + bar : '') + (n > 1 ? '  ' + (i + 1) + '/' + n : '');
            td.append(pitch, where);
            td.title = n > 1 ? 'Written in ' + n + ' places — click for the next' : 'Show it on the score';
            td.addEventListener('click', () => showRangeEnd(e, key));
            return td;
        }
        function showRangeEnd(e, key) {
            const n = e.Positions.length;
            if (n === 0) return;
            // The first click shows the place shown last; the next ones go on to the next place.
            if (rangeCur === key) rangeAt[key] = ((rangeAt[key] || 0) + 1) % n;
            rangeCur = key;
            const pos = e.Positions[Math.min(rangeAt[key] || 0, n - 1)];
            clearHighlights();
            const painted = highlightPositions([pos]);
            lastHighlightPos = pos;
            if (painted[0]) painted[0].scrollIntoView({ block: 'center', inline: 'center', behavior: 'smooth' });
            vscode.postMessage({ type: 'jumpToPosition', position: pos });
            renderRanges();
        }

        // Paused (docs/transport-mixer-design.md §1 3): what played and where, to go on from —
        // { notes, at, origin, countFix, loop, seconds } of the score's list, or { recAt } of the
        // recording playing alone. Stop is live while paused; nothing sounds.
        var paused = null;
        var playOrigin = undefined;     // the origin startPlayback was last given (a stretch's start, or 0)
        /** Whether something sounds now (not paused, not stopped). */
        function playingNow() { return !document.getElementById('stopBtn').disabled && !paused; }
        var playStateSaid = false;
        // The text as it is now cannot be played (the last playback answer was an error: a note
        // half typed) — what sounds is the last version that could be read. While it plays, Play's
        // edge turns amber and its tooltip says so (owner's decision 2026-11-17).
        var scoreUnreadable = false;
        function markUnreadable(unreadable) {
            if (unreadable === scoreUnreadable) return;
            scoreUnreadable = unreadable;
            paintPlayStale(playingNow());
        }
        function paintPlayStale(playing) {
            const playBtn = document.getElementById('playBtn');
            playBtn.classList.toggle('stale', playing && scoreUnreadable);
            playBtn.title = !playing ? 'Play from the A flag (Space while stopped)'
                : scoreUnreadable ? 'Playing the last version of the text that could be read (the text as it is now cannot be) — press to play again from the A flag'
                : 'Playing — press to play again from the A flag';
        }
        function setPlayUi(playing) {
            // The host's follower waits for the music to stop before it turns to another file
            // (said as it changes — not off Stop's state, which a pause keeps live).
            if (playing !== playStateSaid) {
                playStateSaid = playing;
                vscode.postMessage({ type: 'playState', playing });
            }
            // Play stays pressable while playing: pressed then, it plays again from the A flag. It is
            // lit while it plays (owner's choice 2026-11-17), Pause while paused.
            const playBtn = document.getElementById('playBtn');
            playBtn.disabled = false;
            playBtn.classList.toggle('playing', !!playing && !paused);
            paintPlayStale(!!playing && !paused);
            document.getElementById('stopBtn').disabled = !playing && !paused;
            const pauseBtn = document.getElementById('pauseBtn');
            pauseBtn.disabled = !playing && !paused;
            pauseBtn.setAttribute('aria-pressed', String(!!paused));
            // Where the playback stands, beside the length (owner's request 2026-11-16).
            clearInterval(positionTimer);
            positionTimer = playing ? setInterval(showPosition, 200) : null;
            showPosition();
            if (!playing) askStoppedPosition();    // stopped: where the next Play starts
        }

        // ---- The playback's position in the header (owner's request 2026-11-16): just left of
        // the length, on the length's own yardstick — the score's seconds at the written tempo,
        // repeats played (the length is when the score's last note ends, lengthData). The list's
        // clock is taken back to it: less the count-in, plus the stretch's start (a selection or a
        // loop, within its round), times the speed the list was built at (the practice tempo, or
        // the recording's rate), and off the recording's seconds when the notes were moved onto
        // them (playBasis.warp). A recording playing alone shows its own second. Hidden when
        // nothing plays.
        // (var: setPlayUi, above, may run before this line has.)
        var positionTimer = null;
        function clockText(s) {
            const r = Math.max(0, Math.round(s));
            return Math.floor(r / 60) + ':' + String(r % 60).padStart(2, '0');
        }
        function playbackScoreSeconds() {
            if (playClock) {
                // (a loop's count-in before a round shows the round's start, as the first count-in does)
                const tau = lastSpan !== undefined && loopList ? placeNow().tau : Math.max(playOffset + playClock(), loopEntry) - countInFix;
                const origin = lastSpan === undefined ? 0 : spanStart;
                const basis = playBasis || { warp: null, speed: practice.speed };
                const u = (origin + tau) * basis.speed;
                return basis.warp && window.LysSynth && window.LysSynth.unwarpTime
                    ? window.LysSynth.unwarpTime(basis.warp, u) : u;
            }
            if (recPlayer && recPlayer.playing) return recPlayer.position();
            return null;
        }
        function showPosition() {
            const el = document.getElementById('posInfo');
            const s = playingNow() ? playbackScoreSeconds() : paused && typeof paused.seconds === 'number' ? paused.seconds : stoppedSeconds;
            el.hidden = typeof s !== 'number' || !isFinite(s);
            el.textContent = el.hidden ? '' : clockText(s);
            document.getElementById('posSlash').hidden = el.hidden;
            if (!playingNow()) showBarInfo();    // (playing, the lit bar says it: lightPlayingBar)
        }
        /** The bar over the time (owner's request 2026-11-19: it was the practice panel's last line,
         *  "118 bars placed"): the bar playing — paused, the one it stopped in; stopped, the A flag's,
         *  where Play starts — of the bars printed, by the numbers printed. */
        function showBarInfo() {
            const now = document.getElementById('barNow'), end = document.getElementById('barLast');
            let bars;
            try { bars = abBars(); } catch (e) { return; }     // (the page's script is still starting)
            let last = -1;
            for (const b of bars) if (b) last = b.m;
            const m = playingNow() ? (litBar >= 0 ? litBar : Math.max(0, abFrom))
                : paused && lastLitBar >= 0 ? lastLitBar : Math.max(0, abFrom);
            for (const e of [now, document.getElementById('barSlash'), end]) e.hidden = last < 0;
            if (last < 0) { now.textContent = end.textContent = ''; return; }
            const num = i => bars[i] && Number.isInteger(bars[i].bar) ? bars[i].bar : i + 1;
            now.textContent = num(Math.min(m, last));        // (the "/" between them is its own cell: barSlash)
            end.textContent = num(last);
        }
        /**
         * When printed bar m (a measure index, as the flags count) first plays, on a list's own time
         * base: its first event (a note, a rest's light) taken back to the bar line before it — a bar
         * with none, the next bar that has one. The beats are the list's (bar lines accented).
         */
        function barStartIn(list, beats, m) {
            let t = Infinity, bestM = Infinity;
            for (const n of list || []) {
                if (typeof n.M !== 'number' || n.M < m) continue;
                if (n.M < bestM || (n.M === bestM && n.T < t)) { bestM = n.M; t = n.T; }
            }
            if (t === Infinity) return 0;
            let start = 0;
            for (const b of beats || []) { if (b.T > t + 1e-6) break; if (b.A) start = b.T; }
            return start;
        }
        // …and while nothing plays, where Play starts — the A flag's bar (docs/transport-mixer-design.md
        // §1 3; until then the editor's caret, owner's decision 2026-11-17) — off the notes in hand
        // (the score's seconds as the length counts them), else asked of the extension
        // (requestPositionTime → positionTime; a newer question makes an older answer stale, seq).
        var stoppedSeconds = null, positionSeq = 0;
        function askStoppedPosition() {
            if (playingNow() || paused) return;
            const m = Math.max(0, abFrom);
            if (rawNotes) { stoppedSeconds = barStartIn(rawNotes, rawBeats, m); showPosition(); return; }
            let position = -1;
            try {
                for (const r of svgContainer.querySelectorAll('rect.bar-staff[data-start]')) {
                    if (measureOfStaffBox(r) === m) { position = parseInt(r.getAttribute('data-start'), 10); break; }
                }
            } catch (e) {
                return;     // (the page's script is still starting: its bindings are not there yet)
            }
            if (position < 0) { stoppedSeconds = m === 0 ? 0 : null; showPosition(); return; }
            vscode.postMessage({ type: 'requestPositionTime', position, exact: false, seq: ++positionSeq });
        }

        function stopPlayback() {
            playGeneration++;   // a Play waiting for the sound font is off
            stopVoices();
            if (recPlayer) recPlayer.stop();   // the recording (docs/recording-design.md) gives way too
            clearTimeout(playEndTimer);
            playEndTimer = null;
            clearInterval(playheadTimer);
            playheadTimer = null;
            follow.stop();
            followTick = null;
            playClock = null;
            playingNotes = null;
            if (tickWatch && tickWatch.ticks > 0) {
                vscode.postMessage({ type: 'webviewPerf', text: 'follow-along: ' + tickWatch.ticks + ' ticks, longest gap '
                    + Math.round(tickWatch.longest) + ' ms' + (tickWatch.hidden ? ', page hidden' : '') });
            }
            tickWatch = null;
            if (relaying) {
                relaying = false;
                vscode.postMessage({ type: 'candidateSounding', sounding: [] });
            }
            if (sourceLit) {
                sourceLit = false;
                vscode.postMessage({ type: 'soundingSource', positions: [], ended: true });
            }
            clearHighlights();
            lightPlayingBar(null);
            lastHighlightPos = -1;
            // (The next Play starts from the EDITOR's caret — caretStartPos; owner's decision
            // 2026-11-16 — until then a Play after Stop started from the top.)
            if (sfPlayer) {
                sfPlayer = null;
                if (sfSynth) { sfSynth.stop(); }
            }
            midiPlayPending = null;
            if (midiPlaying) {
                midiPlaying = false;
                vscode.postMessage({ type: 'midiStop' });
            }
            for (const o of playingOscs) {
                try { o.stop(); } catch (e) { /* already ended */ }
            }
            playingOscs = [];
            setPlayUi(false);
        }

        // A Play struck while the sound font is still loading waits for it a few
        // seconds — the click just resumed the context, so the worklet's processor and
        // the bank's parsing, which wait for the audio thread, are now under way — and
        // plays the oscillator patches only if it does not come. The generation makes a
        // Stop or another Play in the meantime cancel the wait.
        let playGeneration = 0;
        // origin: given by the score's own Play (and its changes and loop rounds) — the score's
        // played time the list's 0 is — and then the recording sounds with it (startRecordingAlong).
        function startPlayback(notes, offset, plainTones, here, countFix, origin, span) {
            offset = offset || 0;
            stopPlayback();
            // A voice's phrase still sounding gives way to the score (playVoiceSample).
            if (voiceSampleSource) { try { voiceSampleSource.stop(); } catch (e) { /* ended */ } voiceSampleSource = null; }
            playSpan = span || 0;   // (a stretch the recording stops with: Alt+M's bar)
            countInFix = countFix || 0;
            loopEntry = 0;          // (the caller that starts a round from the A flag sets it after)
            // A list with no note still plays when a voice sings (owner's report 2026-11-09,
            // session 1164: the part's button off and its voice on - the voice alone - gave
            // silence): the part's button is the instrument's, the voice is its own toggle's.
            notes = notes || [];
            // (the recording with the score sounds even with every part off: the recording alone, in time)
            const voiced = sungTracks().size > 0 || (origin !== undefined && !!(playBasis && playBasis.warp) && recordingAlong());
            if (notes.length === 0 && !voiced) { return; }
            // Seek = re-schedule from the offset; notes already past it are
            // dropped (mid-note tails too — re-striking half a note reads
            // worse than starting cleanly at the next onset).
            notes = window.LysSynth ? window.LysSynth.fromOffset(notes, offset) : notes.filter(n => n.T >= offset - 0.001);
            if (notes.length === 0 && !voiced) { return; }
            // What sounds is what the menu has on: the instruments whose buttons are on, the voices
            // whose notes are on - each on its own (session 1165; until then a voice took the
            // instrument's place). The score's intent is the DEFAULT: a part it only sings (a
            // singer's staff, no staff without words) has its instrument off unless the menu
            // turned it on (instrumentsOn) - but the instrument stands in while the voice is not
            // there yet (owner's decision 2026-11-08: a Play never waits). The highlight follows
            // the whole list.
            const sung = sungTracks();
            // …and where NONE of a part's voices sings a section (n.U, the server's mark: a
            // singer line's in-clause left it to nobody), the instrument plays it — unless the menu
            // switched the part's voices off and its instrument is not on: silence, as above.
            const played = notes.filter(n => !n.L && !n.Z && (n.U ? unsungSounds(n.N) : instrumentSounds(n.N, sung)));   // (a lit % plays nothing, nor a part turned off)
            if (midiOutput && !here && played.length > 0) {
                // The MIDI output: the server plays the list from the offset and answers
                // (midiStarted) with the clock the highlight follows; a port that is not
                // there comes back as an error, and the list plays here instead. (Nothing for
                // the port - the voices alone - plays here.)
                const gen = ++playGeneration;
                midiPlayPending = { gen, notes, offset };
                setPlayUi(true);
                vscode.postMessage({ type: 'midiPlay', port: midiOutput, notes: played, offset });
                return;
            }
            const ctx = audio();
            const sf = soundFontSynth();
            if (sf && sf.state === 'loading' && !plainTones) {
                const gen = ++playGeneration;
                setPlayUi(true);
                flashInfo('Loading the sound font…', 6000);
                sf.whenReady(8000).then(ok => {
                    if (gen !== playGeneration) { return; }
                    if (!ok) { flashInfo('The sound font is not ready: plain tones this time'); }
                    startPlayback(notes, offset, !ok, here, countFix, origin, span);
                });
                return;
            }
            // From the score's 0: the instruments wait for the voices' first consonant (voicePreroll).
            const pre = voicePreroll(offset, countFix);
            let t0 = ctx.currentTime + 0.15 + pre;
            // The whole list's end, and the voices' (a voice alone has no note to end on).
            let end = Math.max(notes.reduce((e, n) => Math.max(e, n.T + n.D), 0), voicesEnd());
            if (sf && sf.ready && !plainTones) {
                if (played.length > 0) {
                    sfPlayer = sf.play(played, offset, t0);
                    t0 = sfPlayer.t0;
                    end = Math.max(end, sfPlayer.end);
                }
            } else {
                end = Math.max(end, startOscillators(ctx, played, offset, t0));
            }
            // The sung parts' voices, in the instruments' place, from the same clock.
            startVoices(ctx, t0, offset, pre);
            // The recording beside the chart, from the same clock (docs/recording-design.md §1 7).
            if (origin !== undefined) end = Math.max(end, startRecordingAlong(t0, offset, origin, !!loopList));
            // A loop's rounds end where they were laid out to, notes or none (the recording alone goes round too).
            if (loopList && loopRound > 0) end = Math.max(end, (loopRound + loopGap) * loopRoundCount);
            playOrigin = origin;    // (a pause goes on from the same list, the same origin)
            setPlayUi(true);
            // A loop goes round when the last note ends; a plain playback lets it ring a moment.
            playEndTimer = setTimeout(playEnded, (end - offset + (loopList ? 0 : 0.5)) * 1000);
            playOffset = offset;
            playingNotes = notes;
            playEdited = false;
            playClock = () => audioCtx ? audioCtx.currentTime - t0 : 0;
            followAlong(notes, offset, playClock);
        }

        // The server's answer to a midiPlay: started (the wall-clock zero and the end), or
        // not — then the same list plays here, once, with the reason shown.
        function midiStarted(message) {
            const pending = midiPlayPending;
            if (!pending || pending.gen !== playGeneration) { return; }   // stopped, or another Play, meanwhile
            midiPlayPending = null;
            if (!message.ok) {
                flashInfo('MIDI output: ' + (message.error || 'did not start') + ' — playing here', 5000);
                startPlayback(pending.notes, pending.offset, false, true);
                return;
            }
            midiPlaying = true;
            const t0Ms = message.t0Ms;
            // The voice plays here, from where the port's playback stands now (the round trip's jitter).
            if (sungTracks().size > 0) {
                const ctx = audio();
                startVoices(ctx, ctx.currentTime + 0.05, pending.offset + (Date.now() - t0Ms) / 1000 + 0.05, 0);
            }
            playEndTimer = setTimeout(playEnded, (message.end - pending.offset + (loopList ? 0 : 0.5)) * 1000);
            playOffset = pending.offset;
            playClock = () => (Date.now() - t0Ms) / 1000;
            followAlong(pending.notes, pending.offset, playClock);
        }

        // Follow-along: light the notes SOUNDING now (a quarter stays lit while the other
        // hand's eighths change under it) — each entry carries the server-side printed-copy
        // ordinal (O), so repeats and seeks cannot drift — and keep the newest onset in
        // view without fighting manual scrolling more than needed. The cursor
        // (playbackCore.ts) walks the onsets once, so a tick costs what started since.
        // elapsed() is the playback's clock in seconds from the offset: the audio context's
        // for the synth here, the wall clock from the server's zero for the MIDI output.
        // Another text's notes (an AI candidate, playNotes) carry their own offsets as R: those
        // are followed for the candidate panel, which lights them on its score — here nothing.
        // Whether the view follows the notes sounding (owner's request 2026-11-11): a scroll of
        // the user's own that leaves them out of the view ends it, and the Follow button, the
        // notes scrolled back into view, or the next Play brings it back (followScroll.ts).
        ${FOLLOW_SCROLL_JS}
        const follow = installFollowScroll(mainContent, document.getElementById('followBtn'));
        follow.onResume = (els) => { if (els.length > 0) follow.auto(() => keepSoundingInView(els)); };
        function followAlong(notes, offset, elapsed) {
            const Cursor = window.LysSynth ? window.LysSynth.SoundingCursor : null;
            if (!Cursor) { return; }
            const relay = notes.some(n => n.R !== undefined);
            relaying = relay;
            if (!relay) follow.start();
            followSystemDocTop = null; followSystemOffset = null; followTopTarget = null;   // a new Play finds its place anew
            followStaff = relay ? -1 : followStaffOfCaret();
            // The rests (E) are the editor's text's alone: lit there while they last, not on the score
            // (owner's request 2026-11-17).
            const onScore = notes.filter(n => !n.E && !n.Z);
            // …and a part turned off lights nothing of its own, but its notes light the bar while nothing
            // else sounds — the recording alone, every part off (owner's request 2026-11-17).
            const quietCursor = relay ? null : new Cursor(notes.filter(n => n.Z && !n.E).map(n => ({ t: n.T - offset, d: n.D, s: n.S, o: n.O || 0 })));
            const cursor = new Cursor(onScore.map(n => ({ t: n.T - offset, d: n.D, s: relay ? n.R : n.S, o: n.O || 0 })));
            const restCursor = relay ? null : new Cursor(notes.filter(n => n.E).map(n => ({ t: n.T - offset, d: n.D, s: n.S, o: n.O || 0 })));
            // The notes about to sound, FOLLOW_LEAD seconds ahead (owner's request 2026-11-16: a
            // player must see the next line, or the notes cut off at the side, before they sound —
            // the view moved only once the next line's first note had sounded). The view is moved
            // for them and the notes sounding; the notes are lit when they sound.
            // (the parts turned off too: they still say where the music goes, for the view)
            const ahead = relay ? null : new Cursor(notes.filter(n => !n.E).map(n => ({ t: n.T - offset, d: n.D, s: n.S, o: n.O || 0 })));
            let aheadEls = [], soundingEls = [];
            // How evenly the 50 ms tick came (owner's report 2026-10-25: one eighth note lit in
            // four — the tick a second apart, as a browser throttles a page it thinks hidden):
            // the longest gap and whether the page said it was hidden, logged when it stops.
            tickWatch = { last: performance.now(), longest: 0, ticks: 0, hidden: document.hidden };
            // Run by the page's own timer and by the host's followTick, whichever comes: a
            // throttled timer leaves it to the host's (see the host's followTicker).
            const tick = () => {
                const now = performance.now();
                if (now - tickWatch.last < 25 && tickWatch.ticks > 0) return;
                tickWatch.longest = Math.max(tickWatch.longest, now - tickWatch.last);
                tickWatch.last = now;
                tickWatch.ticks++;
                tickWatch.hidden = tickWatch.hidden || document.hidden;
                const playedTo = elapsed();
                const r = cursor.at(playedTo);
                const a = ahead ? ahead.at(playedTo + FOLLOW_LEAD) : null;
                if (a && a.hadNew) {
                    aheadEls = [];
                    for (const entry of a.sounding) aheadEls.push.apply(aheadEls, elementsAt(entry));
                    if (aheadEls.length > 0 && follow.follows()) follow.auto(() => keepSoundingInView(soundingEls, aheadEls));
                }
                const rests = restCursor ? restCursor.at(playedTo) : null;
                const quiet = quietCursor ? quietCursor.at(playedTo) : null;
                if (quiet && quiet.changed && r.sounding.length === 0) {
                    const els = [];
                    for (const x of quiet.sounding) els.push.apply(els, elementsAt(x));
                    if (els.length > 0) {
                        lightPlayingBar(els);
                        if (quiet.hadNew && follow.follows()) follow.auto(() => keepSoundingInView(els, aheadEls));
                    }
                }
                if (!r.changed && !(rests && rests.changed)) return;
                if (relay) {
                    vscode.postMessage({ type: 'candidateSounding', sounding: r.sounding.map(x => ({ pos: x.pos, occ: x.occ })) });
                    return;
                }
                // …the host lights their text in the editor (the same notes, by their offsets), and
                // the rests lasting now with them.
                sourceLit = true;
                vscode.postMessage({ type: 'soundingSource', positions: r.sounding.map(x => x.pos).concat(rests ? rests.sounding.map(x => x.pos) : []) });
                if (!r.changed) return;
                clearHighlights();
                const painted = highlightPositions(r.sounding);
                if (r.sounding.length > 0) {
                    lastHighlightPos = r.sounding[0].pos;
                }
                if (painted.length > 0) { follow.sounding(painted); soundingEls = painted; }
                if (r.hadNew && painted.length > 0 && follow.follows()) follow.auto(() => keepSoundingInView(painted, aheadEls));
                if (painted.length > 0) lightPlayingBar(painted);
            };
            followTick = tick;
            playheadTimer = setInterval(tick, 50);
        }

        // Keeps what sounds now in view, the notes of every part TOGETHER (owner's report
        // 2026-10-29: a song's system — chords, voice, words, guitar, bass, drums — was taller
        // than half the view, and following one note at a time centred the voice, then the
        // drums it pushed off, then the voice: the view shook up and down). Their union is
        // centred when it fits the view; when it does not, its top is kept near the view's top,
        // moved only when that top is out of sight — so a voice's rest does not move it.
        // A system taller than the view (a big band zoomed in; owner's report 2026-11-16: the view
        // jerked up and down as the topmost sounding staff changed with every entry): the view keeps
        // its place IN the system — not moved within it, wherever the reader has scrolled — and takes
        // the same place in the next system. followSystemDocTop is the system followed (its top in
        // the content's own coordinates), followSystemOffset the view's top below it.
        // followTopTarget: the scrollTop a move to the next system is going to — a smooth scroll the
        // next sideways move must not cut short (a second smooth scrollBy stops the first where it
        // is: MEASURED 2026-11-16, the next system reached 2315 of 6483), and not the reader's place
        // while it runs.
        var followSystemDocTop = null, followSystemOffset = null, followTopTarget = null;
        // The notes about to sound lead the view (owner's request 2026-11-16): ahead are the
        // elements of the notes FOLLOW_LEAD seconds on, current those sounding. Both are kept in
        // view when they fit together; when they do not, the notes to come win — the next line,
        // or the notes cut off at the side, are on screen before they sound.
        const FOLLOW_LEAD = 1.5;
        function extentOf(els) {
            let top = Infinity, bottom = -Infinity, left = Infinity, right = -Infinity;
            for (const el of els || []) {
                const b = el.getBoundingClientRect();
                if (b.width === 0 && b.height === 0) continue;
                top = Math.min(top, b.top); bottom = Math.max(bottom, b.bottom);
                left = Math.min(left, b.left); right = Math.max(right, b.right);
            }
            return top === Infinity ? null : { top, bottom, left, right };
        }
        // The part followed (owner's request 2026-11-16): with several staves, the view follows the
        // part the editor's caret was in when Play was pressed — its staff's place among a system's
        // staff boxes, top first (rect.bar-staff.caret's, followStaffOfCaret) — and keeps still while
        // it rests. -1: every part, as before (no caret bar).
        var followStaff = -1;
        /** The staff boxes (client rects) of a bar column, top first. */
        function staffBoxesIn(column) {
            const list = [];
            for (const r of svgContainer.querySelectorAll('rect.bar-staff')) {
                const k = r.getBoundingClientRect();
                const mx = (k.left + k.right) / 2, my = (k.top + k.bottom) / 2;
                if (mx >= column.left && mx <= column.right && my >= column.top - 1 && my <= column.bottom + 1) list.push(k);
            }
            return list.sort((a, b) => a.top - b.top);
        }
        function followStaffOfCaret() {
            const c = svgContainer.querySelector('rect.bar-staff.caret');
            const column = c ? barBoxOf(c) : null;
            if (!column) return -1;
            const k = c.getBoundingClientRect();
            const list = staffBoxesIn(column);
            return list.length > 1 ? list.findIndex(b => Math.abs(b.top - k.top) < 0.5 && Math.abs(b.left - k.left) < 0.5) : -1;
        }
        /** The followed staff's band in an element's bar column — its box, widened halfway to the
         *  staves above and below (a high note stands over its staff) — or null. */
        function followedBand(el) {
            const column = barBoxOf(el);
            if (!column) return null;
            const list = staffBoxesIn(column);
            if (followStaff < 0 || followStaff >= list.length) return null;
            const b = list[followStaff];
            const above = followStaff > 0 ? list[followStaff - 1].bottom : column.top - 1e6;
            const below = followStaff + 1 < list.length ? list[followStaff + 1].top : column.bottom + 1e6;
            return { top: b.top, bottom: b.bottom, lo: (above + b.top) / 2, hi: (b.bottom + below) / 2 };
        }
        /** The elements on the followed staff (all of them when no staff is followed). */
        function onFollowedStaff(els) {
            if (followStaff < 0 || !els || els.length === 0) return els || [];
            const band = followedBand(els[0]);
            if (!band) return els;
            return els.filter(el => { const b = el.getBoundingClientRect(); const cy = (b.top + b.bottom) / 2; return cy >= band.lo && cy <= band.hi; });
        }
        function keepSoundingInView(current, ahead) {
            // The system is told by every part's notes to come: the followed part resting there
            // must not send the view back to the system it is leaving (MEASURED 2026-11-16: 5471 →
            // 6343 → 5383 when the part's notes to come were none and its sounding ones led).
            const leading = (ahead && ahead[0]) || (current && current[0]);
            if (followStaff >= 0) { current = onFollowedStaff(current); ahead = onFollowedStaff(ahead); }
            const cur = extentOf(current), next = extentOf(ahead);
            if (!cur && !next) return;
            const both = cur && next ? { top: Math.min(cur.top, next.top), bottom: Math.max(cur.bottom, next.bottom),
                left: Math.min(cur.left, next.left), right: Math.max(cur.right, next.right) } : (cur || next);
            const m = mainContent.getBoundingClientRect();
            const margin = 30;
            // One way (top/bottom, or left/right): the shift that brings box [lo, hi] into the view's
            // [vlo, vhi] — centred when it fits and is off, its start at the margin when it is too
            // long and its start is out of sight.
            const shift = (lo, hi, vlo, vhi) => {
                const fits = hi - lo <= vhi - vlo - 2 * margin;
                const off = fits ? lo < vlo + margin || hi > vhi - margin : lo < vlo + margin || lo > vhi - 2 * margin;
                return !off ? 0 : fits ? (lo + hi) / 2 - (vlo + vhi) / 2 : lo - (vlo + margin);
            };
            // Together when they fit that way, else the notes to come.
            const pick = (lo, hi, span) => (both[hi] - both[lo] <= span - 2 * margin || !next) ? both : next;
            // Sideways, in one move (owner's request 2026-11-16: not a little at a time): when the
            // notes to come would be cut off, or those sounding are out of sight, the note sounding
            // goes to the leftmost place the view shows — or, the notes to come being to its left
            // (the next line), they do.
            // The notes sounding are in sight when their left edge is (several staves' notes together
            // can be wider than a narrow view: their right edge out of it moved the view by the few
            // pixels between that left edge and the margin — MEASURED 2026-11-16, 37 px).
            const offRight = (b) => b && b.right > m.right - margin;
            const offLeft = (b) => b && b.left < m.left + margin - 1;
            const startOut = (b) => b && (b.left < m.left + margin - 1 || b.left > m.right - margin);
            // Notes to come cut off move the view only once the note sounding is past the first third
            // of it: a jump moves a third of the view or more, never the few pixels the sounding
            // note went on since the last (MEASURED 2026-11-16 in a 394 px view: 839 → 1259 → 1297).
            const pastThird = cur && cur.left > m.left + margin + (m.width - 2 * margin) / 3;
            let dx = 0;
            if ((offRight(next) && (pastThird || !cur)) || offLeft(next) || startOut(cur)) {
                const anchor = next && (!cur || next.left < cur.left) ? next.left : (cur || next).left;
                dx = anchor - (m.left + margin);
                if (Math.abs(dx) < 1) dx = 0;
            }
            const system = leading ? barBoxOf(leading) : null;
            if (system && system.height > m.height - 2 * margin) {
                const docTop = system.top + mainContent.scrollTop;
                const moving = followTopTarget !== null && Math.abs(mainContent.scrollTop - followTopTarget) > 2;
                if (!moving) followTopTarget = null;
                if (followSystemDocTop !== null && Math.abs(docTop - followSystemDocTop) < 1) {
                    if (!moving) followSystemOffset = m.top - system.top;   // the reader's place, kept
                } else {
                    if (followSystemOffset === null) {
                        // The first: where the view stands in it, or its top at the margin.
                        followSystemOffset = m.bottom > system.top && m.top < system.bottom ? m.top - system.top : -margin;
                    }
                    followSystemOffset = Math.max(-margin, Math.min(system.height - m.height + margin, followSystemOffset));
                    followTopTarget = mainContent.scrollTop + system.top + followSystemOffset - m.top;
                }
                followSystemDocTop = docTop;
                // The part followed out of sight: its staff to the middle of the view, at once.
                const band = followStaff >= 0 && leading ? followedBand(leading) : null;
                if (band && !moving && (band.top < m.top + margin || band.bottom > m.bottom - margin)) {
                    followTopTarget = mainContent.scrollTop + (band.top + band.bottom) / 2 - (m.top + m.bottom) / 2;
                    followSystemOffset = followTopTarget - mainContent.scrollTop + m.top - system.top;
                }
                // Where the view goes, both ways at once, so neither cuts the other short.
                const topTo = followTopTarget !== null ? followTopTarget : mainContent.scrollTop;
                const vertical = Math.abs(topTo - mainContent.scrollTop) > 1;
                if (vertical || dx !== 0) {
                    // A sideways move alone is one jump; with a move down they go together, smoothly.
                    mainContent.scrollTo({ top: topTo, left: mainContent.scrollLeft + dx, behavior: vertical ? 'smooth' : 'auto' });
                }
                return;
            }
            followSystemDocTop = null;
            const down = pick('top', 'bottom', m.height);
            const dy = shift(down.top, down.bottom, m.top, m.bottom);
            if (dy === 0 && dx === 0) return;
            mainContent.scrollBy({ top: dy, left: dx, behavior: dy !== 0 ? 'smooth' : 'auto' });
        }
        // The fallback: oscillator patches, one node pair per note, built up front (fine
        // for the short pieces this plays while no sound font is loaded). Timbre families
        // (note.I): waveform + envelope approximations; pluck = exponential decay. The sum
        // goes through a limiter — many voices at once used to clip. Returns when the last
        // note ends, in seconds from the piece's start.
        ${PLAIN_TONES_JS}

        // ---- Audition: sound the caret's note (held) or its whole measure ----
        // The caret's note rings while the key is held. VS Code has no key-up event,
        // so a held key arrives as an OS key-repeat of the play command; each fire
        // re-arms a watchdog, and the note stops once the repeats cease. The strike is
        // the SoundFont synth's (the part's program, PlaybackNote.G / the server's
        // Program) when it is ready, else an oscillator of the timbre family's wave.
        let heldOscs = [];
        let heldPitches = [];       // ringing on the SoundFont synth (or the MIDI output), on heldChannel
        let heldChannel = -1;
        let heldMidi = false;       // the strike went to the MIDI output: the release goes there too
        let heldKey = '';           // what rings, so a fast repeat sustains instead of re-striking
        let heldWatchdog = null;
        let pendingAudition = null; // audition awaiting a fresh note list
        // Each request for the notes is numbered, and the extension says the number back: only the answer
        // to the latest is played (owner's report 2026-11-17: A-B flags moved while it played — two
        // requests in flight, the first answer took the A-B span and the second played the whole score
        // from its start, past B). An audition's own answer still goes to the audition (auditionSeq).
        let playSeq = 0, auditionSeq = -1;
        function requestPlay() { vscode.postMessage({ type: 'requestPlayback', seq: ++playSeq }); }
        const AUD_WAVE = ['triangle','sine','square','sawtooth','triangle','sine','sawtooth','square','sine','triangle'];

        function stopHeldNote() {
            if (heldWatchdog) { clearTimeout(heldWatchdog); heldWatchdog = null; }
            if (heldMidi) {
                heldMidi = false;
                if (heldPitches.length) { vscode.postMessage({ type: 'midiRelease', channel: heldChannel, pitches: heldPitches }); }
            } else if (heldPitches.length && sfSynth) { sfSynth.release(heldChannel, heldPitches); }
            heldPitches = []; heldChannel = -1;
            if (heldOscs.length && audioCtx) {
                const t = audioCtx.currentTime;
                for (const o of heldOscs) {
                    try {
                        o.g.gain.cancelScheduledValues(t);
                        o.g.gain.setTargetAtTime(0.0001, t, 0.05);
                        o.osc.stop(t + 0.3);
                    } catch (e) { /* already ended */ }
                }
            }
            heldOscs = []; heldKey = '';
        }

        // strumS > 0: the pitches one after another in the order given, that many seconds apart
        // (a strum, playPitches), on the AUDIO clock — or, for the MIDI output, on the extension
        // host's — never on this page's timers: a hidden preview fires those about once a second,
        // and the strings after the first came out together as a chord (owner's report 2026-10-24).
        // apart (with a strum): each string stops as the next one sounds, so they never ring as a
        // chord — a bass's (owner's request 2026-10-24: a low chord is muddy).
        function holdNotes(pitches, timbre, program, gapMs, strumS, apart, velocity) {
            // (the audition's own 100, or the velocity the piece plays the pitches at: playPitches)
            const vel = velocity > 0 ? Math.max(1, Math.min(127, Math.round(velocity))) : 100;
            const strum = strumS > 0 ? strumS : 0;
            const separate = strum > 0 && !!apart;
            const key = pitches.slice().sort((a, b) => a - b).join(',') + '/' + timbre + '/' + program;
            // Fresh press (or a different note) re-attacks; a fast repeat of the SAME
            // note leaves the tone ringing and only re-arms the watchdog.
            const fresh = (heldOscs.length === 0 && heldPitches.length === 0) || key !== heldKey || gapMs > 700;
            if (fresh) {
                stopHeldNote();
                if (midiOutput) {
                    // The server strikes on its audition channel (the drums on theirs) with
                    // the part's program; the release names the same channel.
                    heldMidi = true;
                    heldChannel = timbre === 9 ? 9 : 15;
                    heldPitches = pitches.slice();
                    vscode.postMessage({ type: 'midiStrike', port: midiOutput, pitches: heldPitches, timbre: timbre, program: program,
                        gapMs: Math.round(strum * 1000), apart: separate });
                } else {
                    const ctx = audio();
                    const sf = soundFontSynth();
                    if (sf && sf.ready) {
                        heldChannel = sf.strike(pitches, timbre, program, vel, strum, separate);
                        heldPitches = pitches.slice();
                    } else {
                        const master = oscillatorOutput(ctx, 0.22);
                        const now = ctx.currentTime;
                        pitches.forEach((p, i) => {
                            const t0 = now + i * strum;
                            const osc = ctx.createOscillator();
                            osc.type = AUD_WAVE[timbre] || 'triangle';
                            osc.frequency.value = 440 * Math.pow(2, (p - 69) / 12);
                            const g = ctx.createGain();
                            // Silent until its onset: a string released before it starts (the
                            // release cancels the ramp) must not sound at the node's default gain.
                            g.gain.value = 0;
                            g.gain.setValueAtTime(0, t0);
                            g.gain.linearRampToValueAtTime(0.9 * vel / 100, t0 + 0.02);
                            if (separate && i + 1 < pitches.length) {
                                // Apart: fades as the next string sounds.
                                const next = t0 + strum;
                                g.gain.setTargetAtTime(0.0001, next, 0.03);
                                osc.stop(next + 0.3);
                            }
                            osc.connect(g); g.connect(master);
                            osc.start(t0);
                            heldOscs.push({ osc, g });
                        });
                    }
                }
                heldKey = key;
            }
            // Long window on a fresh press (bridge the OS repeat delay), short once
            // the repeats are flowing (a snappy release when the key goes up).
            if (heldWatchdog) { clearTimeout(heldWatchdog); }
            heldWatchdog = setTimeout(stopHeldNote, fresh ? 700 : 220);
        }

        function auditionNote(position, gapMs) {
            if (!playbackNotes) { return; }
            // The note token nearest the caret (smallest |S - position|).
            let best = null, bestD = Infinity;
            for (const n of playbackNotes) {
                if (n.S < 0 || n.L || n.P < 0) { continue; }   // (a light, a pedal: no pitch to hear)
                const d = Math.abs(n.S - position);
                if (d < bestD) { bestD = d; best = n; }
            }
            if (!best) { return; }
            const pitches = playbackNotes.filter(n => n.S === best.S && !n.L && n.P >= 0).map(n => n.P);
            if (pitches.length) {
                holdNotes(pitches, best.I || 0, typeof best.G === 'number' ? best.G : -1, gapMs);
            }
        }

        function auditionMeasure(start, end) {
            if (!playbackNotes) { return; }
            const sub = playbackNotes.filter(n => n.S >= start && n.S < end);
            if (sub.length === 0) { return; }
            let minT = Infinity, maxEnd = 0;
            for (const n of sub) { if (n.T < minT) { minT = n.T; } if (n.T + n.D > maxEnd) { maxEnd = n.T + n.D; } }
            // With the recording loaded and lined up, the bar plays WITH it: the recording's bar and
            // this part's notes, bar line to bar line (owner's decision 2026-11-17, Orecchiante request 17:
            // a chord is judged against the recording, as Transcribe! lets one).
            if (recordingAlong() && playBasis && playBasis.warp) {
                const sp = playedSpeed();
                let from = minT, to = -1;
                for (const b of playedBeats() || []) {
                    if (!b.A) continue;
                    const t = b.T / sp;
                    if (t <= minT + 1e-6) from = t;
                    else { to = t; break; }
                }
                if (to < 0) { const e = playedBarEnd(); to = typeof e === 'number' ? e / sp : maxEnd; }
                voicePlan = { pieces: [[from, to]], rounds: 1 };
                startPlayback(sub.map(n => Object.assign({}, n, { T: n.T - from })), 0, undefined, undefined, 0, from, to - from);
                return;
            }
            // The sung voice is cut to the bar too (owner's report 2026-11-09, session 1150: Alt+M
            // sang the first bar's words — the voice plan was the last Play's, the whole piece from
            // its 0, while the instruments played the bar moved to 0). One piece: the bar's stretch
            // of the score's time, as a Play with bars selected plays (spanPieces).
            voicePlan = { pieces: [[minT, maxEnd]], rounds: 1 };
            // Reschedule the measure to start at t=0, then reuse the full scheduler.
            startPlayback(sub.map(n => Object.assign({}, n, { T: n.T - minT })), 0);
        }

        /** A note list fetched for an audition, taken as Play takes one: kept as the last that could
         *  be read (so text being typed still auditions, from it) and put through the practice menu
         *  — the tempo, the parts turned off, the recording's time (practiced). Until 2026-11-17 the
         *  list was used as it came: Alt+M played at the written tempo whatever the menu said, and
         *  with no Play pressed yet, unreadable text left it silent (nothing kept to fall back on). */
        function takeAuditionNotes(message) {
            rawNotes = message.notes; rawParts = message.parts || null; rawBeats = message.beats || null; rawBarEnd = message.barEnd || null;
            rawExtra = message.extra || null; rawSung = message.sung || null; rawPlayed = message.played || null;
            playbackNotes = practiced(rawNotes);
        }

        function runAudition(a) {
            if (a.mode === 'measure') { auditionMeasure(a.rangeStart, a.rangeEnd); }
            else { auditionNote(a.position, a.gapMs); }
        }

        // Arbitrary pitches for a fixed time: the step keys and the automatic
        // audition (the caret landing on a note, a typed note, an @chord's shape).
        // The extension sends the compiler's MIDI numbers and the part's program, so
        // no note list is needed here. Same voice as the held audition: a fresh strike
        // (the huge gap), then released after durationMs instead of waiting for key-repeats.
        // strumMs > 0: the pitches one after another IN THE ORDER GIVEN, that far apart — a
        // tuning's open strings string after string (owner's request 2026-10-24), each joining the
        // ones already ringing (holdNotes times them). Not sorted: a re-entrant ukulele's high 4th
        // string comes first.
        // apart: each string stops as the next sounds (a bass's — holdNotes).
        // velocity / part (an @or's rotation, owner's report 2026-11-17): the velocity the piece plays
        // the chord at, under that part's fader — so the chord tried sounds as the row plays it.
        function playPitches(pitches, timbre, program, durationMs, strumMs, apart, velocity, part) {
            if (!pitches || pitches.length === 0) { return; }
            stopHeldNote();
            const prog = typeof program === 'number' ? program : -1;
            let vel = typeof velocity === 'number' && velocity > 0 ? velocity : -1;
            if (vel > 0 && part) {
                const levels = eff().levels || {};
                const lv = typeof levels[part] === 'number' && isFinite(levels[part]) ? Math.min(2, Math.max(0, levels[part])) : 1;
                vel = Math.min(127, vel * lv);
                if (vel < 1) return;   // (its fader all the way down)
            }
            holdNotes(pitches, timbre || 0, prog, 1e9, pitches.length > 1 ? (strumMs || 0) / 1000 : 0, !!apart, vel);
            if (heldWatchdog) { clearTimeout(heldWatchdog); }
            heldWatchdog = setTimeout(stopHeldNote, durationMs || 450);
        }

        document.getElementById('playBtn').addEventListener('click', () => {
            // The recording on but not loaded: the score plays alone this time (and says why).
            if (recOn && recording && !recording.ready) {
                flashInfo(recording.error || 'The recording is not loaded yet: the score alone', 4000);
            }
            // Always from the A flag (docs/transport-mixer-design.md §1 3; owner's decisions
            // 2026-11-17) — stopped, playing or paused alike: the bars A to B with A-B on, else
            // the score from A's bar on. The editor's caret has no say (it was the start point from
            // 2026-11-16: "why does it play from here?").
            paused = null;
            if (playingNow()) stopPlayback();
            showAFlag();
            setPlayUi(true); // immediate feedback while the request runs
            pendingStartBar = abOn ? -1 : Math.max(0, abFrom);
            pendingSpan = abOn ? { ab: [abFrom, abTo] } : null;
            pendingLoopRefresh = false;     // (this Play's notes are its own, not a loop's next round)
            requestPlay();
        });
        document.getElementById('stopBtn').addEventListener('click', () => {
            pendingLoopRefresh = false;     // (a loop's next round with the new text: off with it)
            loopList = null;
            paused = null;
            stopPlayback();
            if (recPlayer && recPlayer.playing) { recPlayer.stop(); setPlayUi(false); }
        });
        // Rewind (owner's request 2026-11-17): the A flag to the top — with A-B on, B stays. Playing, it
        // plays from there (Play's rule: from A); paused, it stops there.
        document.getElementById('rewindBtn').addEventListener('click', () => {
            setA(0, false);
            if (playingNow()) { document.getElementById('playBtn').click(); return; }
            if (paused) { paused = null; setPlayUi(false); }
        });
        // Pause (§1 3): playing, held where it is; paused, on from there. A does not move.
        document.getElementById('pauseBtn').addEventListener('click', () => {
            if (paused) {
                const p = paused;
                paused = null;
                if (p.recAt !== undefined) {
                    if (!playRecordingFrom(p.recAt)) setPlayUi(false);
                    return;
                }
                loopList = p.loop;
                startPlayback(p.notes, p.at, false, false, p.countFix, p.origin);
                loopList = p.loop;
                loopEntry = p.entry || 0;
                return;
            }
            if (!playingNow()) return;
            if (playClock && playingNotes) {
                const p = { notes: playingNotes, at: Math.max(0, playOffset + playClock()), origin: playOrigin,
                    countFix: countInFix, loop: loopList, entry: loopEntry, seconds: playbackScoreSeconds() };
                pendingLoopRefresh = false;
                stopPlayback();
                paused = p;
                setPlayUi(false);
            } else if (recPlayer && recPlayer.playing) {
                const at = recPlayer.position();
                recPlayer.stop();
                paused = { recAt: at || 0, seconds: at };
                setPlayUi(false);
            }
        });
        // Space, with the page in focus (not in a field): stopped, Play; playing or paused, Pause.
        window.addEventListener('keydown', (e) => {
            if (e.key !== ' ' || e.ctrlKey || e.metaKey || e.altKey || e.repeat) return;
            const t = e.target;
            if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT' || t.isContentEditable)) return;
            e.preventDefault();
            document.getElementById(playingNow() || paused ? 'pauseBtn' : 'playBtn').click();
        });
        const loopBtn = document.getElementById('loopBtn');
        loopBtn.addEventListener('click', () => {
            loopOn = !loopOn;
            // While playing, what plays is laid out again from here as Repeat now says, so B (or
            // the end) does what it says when it is reached: back to A on, stop off (owner's request
            // 2026-11-17: turned on while the bars played, it still stopped at B). Nothing to go on
            // from (the recording alone): off ends what plays where it ends.
            if (!playingNow() || !resumeHere()) {
                if (!loopOn) loopList = null;
            }
            showPractice();
        });

        // ---- The A-B repeat (docs/ab-repeat-design.md; owner's decisions 2026-11-16/17): the bars
        // A to B, as printed (PlaybackNote.M, from 0 — abFrom/abTo are those measure indices), both
        // included. A-B alone plays the bars once and stops; with Repeat (the header's button), round
        // and round. The range is the page's own (not kept), set on the page itself: a click on a bar
        // is A, a Shift+click B (A-B on), the range's last bar Shift+clicked again A-B off; the flags
        // are dragged. (Owner's decision 2026-11-19: the panel's A-B button, its bar numbers and the
        // eight kept ranges are gone.)
        var abOn = false, abFrom = -1, abTo = -1;
        /** The page's bars, by measure index: the printed number and the box (rect.bar-box). */
        function abBars() {
            const out = [];
            for (const facts of livePageFacts())
                for (const b of facts.bars) if (!out[b.m]) out[b.m] = b;
            return out;
        }
        /** The measure index of a staff's bar box (rect.bar-staff): the page's bar box in its column. */
        function measureOfStaffBox(r) {
            const b = r.getBoundingClientRect();
            let best = -1, gap = Infinity;
            for (const bb of svgContainer.querySelectorAll('rect.bar-box[data-m]')) {
                const c = bb.getBoundingClientRect();
                if (Math.abs(c.left - b.left) > 1) continue;
                const d = b.top > c.bottom ? b.top - c.bottom : c.top > b.bottom ? c.top - b.bottom : 0;
                if (d < gap) { gap = d; best = parseInt(bb.getAttribute('data-m'), 10); }
            }
            return best;
        }
        function showAb() { drawAbFlags(); }
        /** A-B moved while it plays: the new bars, from where it plays now when that is inside them
         *  (owner's request 2026-11-17: a flag moved sent it back to A each time), else from A. */
        function replayIfPlaying() {
            if (!playingNow()) return;
            if (resumeHere()) return;
            stopPlayback();
            setPlayUi(true);
            pendingStartBar = abOn ? -1 : Math.max(0, abFrom);
            pendingSpan = abOn ? { ab: [abFrom, abTo] } : null;
            requestPlay();
        }
        /** What plays laid out again from the notes in hand as A-B and Repeat now say (the text has
         *  not changed: no request) — the bars A to B (A-B on), the whole score round and round
         *  (Repeat on), or the whole score once — played on from the moment now sounding when it is
         *  in what is laid out, else from its start. So a flag moved, or Repeat turned on or off,
         *  while it plays decides what happens at B (owner's requests 2026-11-17: back to A each
         *  time a flag moved; Repeat turned on while playing still stopped at B). False when there
         *  is nothing to go on from (the notes not in hand, the recording playing alone). */
        function resumeHere() {
            if (!rawNotes || !playClock || relaying || !window.LysSynth) return false;
            // Where it plays now, in the list's own time (before the stretch was cut out of it; the
            // whole score, A-B just turned on, is the list's time itself).
            const place = placeNow();
            let tau = place.tau;
            const pieces = lastSpan === undefined ? [[0, Infinity]] : spanPieces || [[spanStart, spanStart + loopRound]];
            let u = pieces.length ? pieces[pieces.length - 1][1] : 0;
            for (const [a, b] of pieces) { if (tau < b - a) { u = a + tau; break; } tau -= b - a; }
            playbackNotes = practiced(rawNotes);
            const span = abOn ? { ab: [abFrom, abTo] } : loopOn ? null : undefined;
            lastSpan = span;
            if (span === undefined) {
                // The whole score once, from the moment now sounding (as a Play from there).
                loopList = null;
                voicePlan = { pieces: null, rounds: 1 };
                startPlayback(playbackNotes, u, false, false, 0, 0);
                return true;
            }
            lastOnce = false;
            const list = spanOf(playbackNotes, span, false);
            // …and that moment in the new stretch, if it is in it.
            let at = null, before = 0;
            for (const [a, b] of spanPieces || [[spanStart, spanStart + loopRound]]) {
                if (u >= a - 1e-6 && u < b - 1e-6) { at = before + (u - a); break; }
                before += b - a;
            }
            loopList = loopOn ? list : null;
            if (loopOn) {
                // Round and round: on from here — in a count-in, on with it; not in the new
                // stretch, from its start, its count-in first.
                const rounds = looped(list);
                voicePlan = loopVoicePlan();
                startPlayback(rounds, at === null ? 0 : loopOffset(at, place.counting), false, false, loopGap, spanStart);
            } else {
                voicePlan = { pieces: spanPieces, rounds: 1 };
                startPlayback(list, at === null ? 0 : at, false, false, 0, spanStart);
            }
            loopList = loopOn ? list : null;
            return true;
        }
        function setAb(from, to) {
            const last = Math.max(0, abBars().length - 1);
            abFrom = Math.max(0, Math.min(from, last));
            abTo = Math.max(abFrom, Math.min(to, last));
            showAb();
            showPractice();
            replayIfPlaying();
        }
        /** A-B off (B's own bar line Shift+clicked): the B flag goes, A stays; what plays goes on as
         *  the score from here. */
        function abOff() {
            abOn = false;
            showAb();
            showPractice();
            askStoppedPosition();
            replayIfPlaying();
        }

        // ---- The flags (docs/ab-repeat-design.md §1; owner's requests 2026-11-17). A stands on the
        // bar line before bar A, its pennant pointing right; B on the bar line after bar B, pointing
        // left; the bars between are tinted. Drawn into the page beside the bar boxes, in their
        // units, so they scroll and zoom with the score; drawn again after every picture.
        const AB_NS = 'http://www.w3.org/2000/svg';
        function abEl(name, attrs) {
            const el = document.createElementNS(AB_NS, name);
            for (const k in attrs) el.setAttribute(k, String(attrs[k]));
            return el;
        }
        // The A flag always stands — where Play starts, the top at first — and B with the bars
        // between tinted only while A-B is on (owner's decisions 2026-11-17,
        // docs/transport-mixer-design.md §1 3).
        // The line each flag was last drawn on, and where its pole stood: a flag that moves to another
        // line other than by a drag (a bar clicked, A Here, a range called up) slides there as a dragged
        // flag does (owner's request 2026-11-17) — a redraw on the same line (a zoom, a new page) does not.
        let abDrawnLine = { A: -1, B: -1 };
        function drawAbFlags() {
            const before = {}, sliding = {};
            for (const letter of ['A', 'B']) {
                const f = abFlagEl(letter), pole = f && f.querySelector('.ab-pole');
                if (pole) before[letter] = pole.getBoundingClientRect();
                // (a flag redrawn while it slides goes on sliding from where it has got to)
                sliding[letter] = !!(f && f.dataset.sliding === '1');
            }
            for (const el of svgContainer.querySelectorAll('.ab-mark')) el.remove();
            const bars = abBars();
            if (bars.length === 0) return;
            drawAbFlagsOn(bars);
            const now = { A: abFrom, B: abOn ? abTo + 1 : -1 };
            for (const letter of ['A', 'B']) {
                const f = abFlagEl(letter);
                if (f && before[letter] && !abDrag && ((now[letter] !== abDrawnLine[letter] && abDrawnLine[letter] >= 0) || sliding[letter]))
                    slideAbFlag(f, before[letter]);
            }
            abDrawnLine = now;
        }
        /** Slides a flag just drawn from where its pole stood (a screen rect) to where it stands now —
         *  along the line, or across to another system (moveAbFlag's slide, a drag's). */
        function slideAbFlag(after, from) {
            const pole = after.querySelector('.ab-pole'), to = pole.getBoundingClientRect();
            const unit = (parseFloat(pole.getAttribute('y2')) - parseFloat(pole.getAttribute('y1'))) / Math.max(1, to.height);
            if (Math.abs(from.left - to.left) < 0.5 && Math.abs(from.top - to.top) < 0.5) return;
            const across = Math.abs(from.top - to.top) >= 2;
            after.dataset.sliding = '1';
            after.style.transition = 'none';
            after.style.transform = 'translate(' + ((from.left - to.left) * unit) + 'px, ' + ((from.top - to.top) * unit) + 'px)';
            requestAnimationFrame(() => requestAnimationFrame(() => {
                after.style.transition = 'transform ' + (across ? '0.2s' : '0.12s') + ' ease-out';
                after.style.transform = '';
                setTimeout(() => { delete after.dataset.sliding; }, across ? 220 : 140);
            }));
        }
        function drawAbFlagsOn(bars) {
            if (abFrom < 0 || abFrom >= bars.length) abFrom = Math.max(0, Math.min(abFrom, bars.length - 1));
            if (abOn) {
                for (let m = abFrom; m <= abTo; m++) {
                    const r = bars[m] && bars[m].rect;
                    if (!r) continue;
                    const shade = abEl('rect', { class: 'ab-mark ab-shade', x: r.getAttribute('x'), y: r.getAttribute('y'),
                        width: r.getAttribute('width'), height: r.getAttribute('height') });
                    r.parentNode.insertBefore(shade, r);
                }
                if (bars[abTo]) drawAbFlag('B', bars[abTo].rect, true);
            }
            if (bars[abFrom]) drawAbFlag('A', bars[abFrom].rect, false);
        }
        /** The A flag to bar m — a bar clicked, A Here (docs/transport-mixer-design.md §1 3). With
         *  A-B on and m past B, A stands on bar m and B after it — that bar alone (owner's report
         *  2026-11-17: M10–M11 chosen, M21 clicked, the flags changed places as a drag does and A went
         *  to M11, on the system above; before that it carried the range, M5–M8 and M9 gave M9–M12).
         *  Off, a B left before it is forgotten (placed anew when A-B goes on). Plays on from where it
         *  plays when that is still in what is to play, else from A (replayIfPlaying). */
        function setA(m, replay = true) {
            const last = Math.max(0, abBars().length - 1);
            m = Math.max(0, Math.min(m, last));
            if (abOn && m > abTo) abTo = m;
            abFrom = m;
            if (!abOn && abTo < m) abTo = -1;
            showAb();
            showPractice();
            if (replay) replayIfPlaying();
            askStoppedPosition();
        }
        /** The B flag (owner's decisions 2026-11-17, docs/transport-mixer-design.md §7): atEnd — a bar
         *  Shift+clicked — on the bar line at bar m's
         *  right, so the bar is in the range as a click puts its own in (the owner's revision of the
         *  left line, the same day); else on the line at its left. A bar line at or before A's: B there
         *  and A one bar before it — that bar alone, as a click past B does for A (a Shift+click means to
         *  move B; the two used to change places); the very first bar line, the first bar. On B's own
         *  line (the range's last bar Shift+clicked): B goes, A-B off. A-B turns on. */
        function setB(m, atEnd) {
            const last = Math.max(0, abBars().length - 1);
            m = Math.max(0, Math.min(m, last));
            const line = atEnd ? m + 1 : m;     // the bar line, as the bar after it
            // On B's own bar line: B goes — A-B off (owner's request 2026-11-17).
            if (abOn && line === abTo + 1) { abOff(); return; }
            abOn = true;
            if (line > abFrom) setAb(abFrom, line - 1);
            else setAb(Math.max(0, line - 1), Math.max(0, line - 1));
            askStoppedPosition();
        }
        function drawAbFlag(letter, rect, atEnd) {
            const x = parseFloat(rect.getAttribute('x')) + (atEnd ? parseFloat(rect.getAttribute('width')) : 0);
            const y = parseFloat(rect.getAttribute('y')), h = parseFloat(rect.getAttribute('height'));
            const dir = atEnd ? -1 : 1, top = y - 3.4;
            const g = abEl('g', { class: 'ab-mark ab-flag', 'data-flag': letter });
            g.appendChild(abEl('rect', { class: 'ab-hit', x: x - 1.2, y: top, width: 2.4, height: h + 3.4 }));
            g.appendChild(abEl('line', { class: 'ab-pole', x1: x, y1: top, x2: x, y2: y + h }));
            const tip = x + dir * 2.4;
            g.appendChild(abEl('polygon', { class: 'ab-pennant', points: x + ',' + top + ' ' + tip + ',' + (top + 0.85) + ' ' + x + ',' + (top + 1.7) }));
            const label = abEl('text', { x: x + dir * 0.85, y: top + 1.25, 'text-anchor': 'middle' });
            label.textContent = letter;
            g.appendChild(label);
            g.addEventListener('pointerdown', e => startAbDrag(e, letter));
            // (the sheet's own clicks — select, play from here — are not the flag's)
            for (const type of ['mousedown', 'mouseup', 'click', 'dblclick', 'contextmenu']) g.addEventListener(type, e => e.stopPropagation());
            // In front of everything on the page (owner's request 2026-11-17: a section's mark drawn
            // after the system hid the flag): the last child of the page's outermost svg, carried
            // into the bar rect's coordinates by the matrix between the two. A page not laid out
            // (no matrix) keeps the flag beside the rect.
            const parent = rect.parentNode;
            let page = rect.ownerSVGElement;
            while (page && page.ownerSVGElement && svgContainer.contains(page.ownerSVGElement)) page = page.ownerSVGElement;
            const from = parent.getScreenCTM && parent.getScreenCTM(), to = page && page.getScreenCTM && page.getScreenCTM();
            // The matrix goes on a holder round the flag, not on the flag: a dragged flag slides by a
            // CSS transform on itself (moveAbFlag), which would replace a transform attribute there —
            // until 2026-11-17 the flag passed by the page's top while it slid (owner's report).
            if (page && page !== parent && from && to) {
                const m = to.inverse().multiply(from);
                const holder = abEl('g', { class: 'ab-mark ab-holder', transform: 'matrix(' + [m.a, m.b, m.c, m.d, m.e, m.f].join(' ') + ')' });
                holder.appendChild(g);
                page.appendChild(holder);
            } else parent.appendChild(g);
            return g;
        }
        function abFlagEl(letter) { return svgContainer.querySelector('.ab-flag[data-flag="' + letter + '"]'); }
        /** The view to the A flag — where Play starts (owner's request 2026-11-17: Play brings the page
         *  back to the flag, then follows what plays). A scroll of the page's own (follow.auto). */
        function showAFlag() {
            const f = abFlagEl('A');
            if (!f) return;
            const b = f.getBoundingClientRect(), m = mainContent.getBoundingClientRect(), margin = 30;
            const dy = b.top < m.top + margin || b.bottom > m.bottom - margin ? b.top - (m.top + margin) : 0;
            const dx = b.left < m.left + margin || b.left > m.right - margin ? b.left - (m.left + margin) : 0;
            if (Math.abs(dx) >= 1 || Math.abs(dy) >= 1) follow.auto(() => mainContent.scrollBy({ left: dx, top: dy }));
        }

        // Dragging: the flag stays on a bar line and moves to another once the pointer is nearer to
        // it than to the line the flag is on (a little more, so it does not flicker between two),
        // sliding there. The flag held is drawn bolder and larger, and a dashed line and a faint
        // flag follow the pointer, so what is held shows while the flag itself has not moved.
        // The flags stand on bar lines, numbered as the bars: line L is before bar L (and after bar
        // L-1) — A on line abFrom, B on line abTo + 1. A flag held past the other one swaps with it
        // (owner's request 2026-11-17): the held one becomes the other letter, the bars between the
        // two lines are the range.
        let abDrag = null;
        /** Every place a flag can stand: a bar's left edge is line m, its right edge line m + 1 (at a
         *  system's end and the next one's start, one line in two places). */
        function abLines() {
            const out = [];
            for (const b of abBars()) {
                if (!b) continue;
                const c = b.rect.getBoundingClientRect();
                out.push({ line: b.m, x: c.left, top: c.top, bottom: c.bottom });
                out.push({ line: b.m + 1, x: c.right, top: c.top, bottom: c.bottom });
            }
            return out;
        }
        /** The line the flag (A or B) stands on. */
        function abLineOf(letter) { return letter === 'A' ? abFrom : abTo + 1; }
        /** How far the pointer is from a line's system, up and down: 0 over the bar box or the flag
         *  above it (no taller than the box: zoomed out, the systems stand closer than 40 px). */
        function abSystemDistance(l, y) {
            const above = Math.min(40, (l.bottom - l.top) * 0.4);
            return y < l.top - above ? l.top - above - y : y > l.bottom ? y - l.bottom : 0;
        }
        function abOverlay() {
            let ghost = document.getElementById('abGhost'), rubber = document.getElementById('abRubber');
            if (!ghost) {
                ghost = document.createElement('div'); ghost.id = 'abGhost'; document.body.appendChild(ghost);
                rubber = abEl('svg', { id: 'abRubber' }); rubber.appendChild(abEl('line', {})); document.body.appendChild(rubber);
            }
            return { ghost, rubber, line: rubber.querySelector('line') };
        }
        function showAbGhost(e) {
            const o = abOverlay(), f = abDrag && abFlagEl(abDrag.letter);
            o.ghost.hidden = false; o.rubber.style.display = '';
            o.ghost.textContent = abDrag.letter;
            o.ghost.style.left = e.clientX + 'px'; o.ghost.style.top = e.clientY + 'px';
            const pole = f && f.querySelector('.ab-pole');
            if (pole) {
                const c = pole.getBoundingClientRect();
                o.line.setAttribute('x1', String(c.left + c.width / 2)); o.line.setAttribute('y1', String(c.top + 6));
                o.line.setAttribute('x2', String(e.clientX)); o.line.setAttribute('y2', String(e.clientY));
            }
        }
        function hideAbGhost() {
            const o = abOverlay();
            o.ghost.hidden = true; o.rubber.style.display = 'none';
        }
        function startAbDrag(e, letter) {
            if (e.button !== 0) return;
            e.preventDefault(); e.stopPropagation();
            abDrag = { letter, at: abLineOf(letter) };
            const f = abFlagEl(letter);
            if (f) f.classList.add('grabbed');
            document.body.classList.add('ab-grabbing');
            showAbGhost(e);
            // (no pointer capture: it brought no moves from out of the frame, and it held back the
            // mouseout the scroll below goes on by — MEASURED in a real VS Code, Lab p1290/e2e/cdp-flagscroll.mjs)
            abPointer = { clientX: e.clientX, clientY: e.clientY };
            window.addEventListener('pointermove', onAbMove, true);
            window.addEventListener('pointerup', onAbUp, true);
            window.addEventListener('pointercancel', onAbUp, true);
        }
        // Where the pointer last was, the capture, and the scroll that runs while it is near or past
        // the pane's top or bottom (owner's request 2026-11-17: dragged out of the pane, the preview
        // scrolled only as the mouse moved, 14 px a move): every frame, faster the further out.
        let abPointer = null, abCaptured = null, abScrollFrame = 0, abSwallowClick = false;
        window.addEventListener('click', e => {
            if (!abSwallowClick) return;
            abSwallowClick = false;
            e.stopPropagation(); e.preventDefault();
        }, true);
        function abScrollStep() {
            abScrollFrame = 0;
            if (!abDrag || !abPointer) return;
            const pane = mainContent.getBoundingClientRect(), zone = 40;
            const y = abPointer.clientY;
            // px a frame: up to 10 inside the edge zone, on to 40 well past it
            const reach = y < pane.top + zone ? y - (pane.top + zone) : y > pane.bottom - zone ? y - (pane.bottom - zone) : 0;
            if (reach === 0) return;
            const speed = Math.sign(reach) * Math.min(40, Math.abs(reach) / zone * 10);
            const before = mainContent.scrollTop;
            mainContent.scrollTop += speed;
            if (mainContent.scrollTop === before) return;     // at the top or the bottom already
            abTrack(abPointer);                                  // the score moved under the pointer
            abScrollFrame = requestAnimationFrame(abScrollStep);
        }
        // Out of the preview's frame its moves no longer come (MEASURED: a pointer taken below the frame
        // sent nothing more): the scroll goes on toward the side it left by, at full speed; and a release
        // out there is not heard either, so the drag ends when the pointer comes back with no button
        // down, or the window loses the focus.
        window.addEventListener('mouseout', e => {
            if (!abDrag || !abPointer || e.relatedTarget) return;
            const pane = mainContent.getBoundingClientRect();
            const mid = (pane.top + pane.bottom) / 2;
            abPointer = { clientX: abPointer.clientX, clientY: e.clientY < mid ? pane.top - 400 : pane.bottom + 400 };
            if (!abScrollFrame) abScrollFrame = requestAnimationFrame(abScrollStep);
        });
        window.addEventListener('blur', () => { if (abDrag) onAbUp(); });
        function onAbMove(e) {
            if (!abDrag) return;
            if (e.buttons === 0) { onAbUp(); return; }     // let go out of the frame
            e.preventDefault();
            abPointer = { clientX: e.clientX, clientY: e.clientY };
            abTrack(abPointer);
            if (!abScrollFrame) abScrollFrame = requestAnimationFrame(abScrollStep);
        }
        /** The line the held flag goes to for a pointer at (clientX, clientY), and the ghost there. */
        function abTrack(e) {
            if (!abDrag) return;
            // Any line but the other flag's (no bar between them): past it, the two swap.
            // The system the pointer is over (the nearest, between two) first, then its line nearest
            // across: a wide bar's far line is still its own system's, not the next system's
            // (measured 2026-11-17: zoomed out, a diagonal drag went 4, 9, 5, 10 by the lines' distance).
            // (A-B off: A alone stands — no other flag to pass, and no line after the last bar)
            const other = abOn ? abLineOf(abDrag.letter === 'A' ? 'B' : 'A') : Infinity;
            const lines = abOn ? abLines() : abLines().filter(l => l.line < abBars().length);
            let near = Infinity;
            for (const l of lines) { l.dy = abSystemDistance(l, e.clientY); near = Math.min(near, l.dy); }
            let best = null, bestD = Infinity, curD = Infinity;
            for (const l of lines) {
                if (l.dy > near + 0.5) continue;
                const v = Math.abs(l.x - e.clientX);
                if (l.line === abDrag.at) curD = Math.min(curD, v);
                if (l.line === other) continue;
                if (v < bestD) { bestD = v; best = l; }
            }
            if (best && best.line !== abDrag.at && bestD + 4 < curD) moveAbFlag(best.line);
            showAbGhost(e);
        }
        /** The flag held slides to the bar line given: before the other flag it is A, after it B. */
        function moveAbFlag(line) {
            const before = abFlagEl(abDrag.letter), from = before && before.querySelector('.ab-pole').getBoundingClientRect();
            const other = abOn ? abLineOf(abDrag.letter === 'A' ? 'B' : 'A') : Infinity;
            if (!abOn) { abFrom = line; }
            else if (line < other) { abFrom = line; abTo = other - 1; abDrag.letter = 'A'; }
            else { abFrom = other; abTo = line - 1; abDrag.letter = 'B'; }
            abDrag.at = line;
            drawAbFlags();
            showAb();
            const after = abFlagEl(abDrag.letter);
            if (!after) return;
            after.classList.add('grabbed');
            // From where it stood to where it lands — along the line, or across to another system, up
            // or down (owner's request 2026-11-17: to another system it jumped there at once).
            if (from) slideAbFlag(after, from);
            after.classList.add('ab-landed');
            setTimeout(() => after.classList.remove('ab-landed'), 400);
        }
        function onAbUp() {
            window.removeEventListener('pointermove', onAbMove, true);
            window.removeEventListener('pointerup', onAbUp, true);
            window.removeEventListener('pointercancel', onAbUp, true);
            document.body.classList.remove('ab-grabbing');
            hideAbGhost();
            if (abScrollFrame) { cancelAnimationFrame(abScrollFrame); abScrollFrame = 0; }
            if (abCaptured) {
                try { abCaptured.el.releasePointerCapture(abCaptured.id); } catch (err) { /* released */ }
                abCaptured = null;
                // The click the release makes lands on the pane (it held the pointer): not a bar's click.
                abSwallowClick = true;
                setTimeout(() => { abSwallowClick = false; }, 300);
            }
            abPointer = null;
            if (!abDrag) return;
            abDrag = null;
            if (abOn) setAb(abFrom, abTo);    // (on from where it plays, if it is in the new bars)
            else setA(abFrom);
        }

        // A loop's rounds are laid out ahead, ten minutes of them (loopNotes): the player's
        // clock times each round, not a page timer the browser may hold to a second.
        function looped(list) {
            if (!window.LysSynth || !window.LysSynth.loopNotes) {
                vscode.postMessage({ type: 'webviewPerf', text: 'loop: the synth script is an old build (no loopNotes) - reload the window' });
                return list;
            }
            // With the count-in on, every round has one before it (docs/transport-mixer-design.md §1 4,
            // owner's decision 2026-11-17): the clicks of the stretch's first bar, the music after them.
            // The list then starts with the first round's count — its countFix is loopGap.
            const c = loopRound > 0 ? counted([], 0, spanStart) : null;
            loopGap = c && c.lead > 0 ? c.lead : 0;
            const gap = loopGap > 0 ? { lead: loopGap, clicks: c.notes } : null;
            const rounds = window.LysSynth.loopNotes(list, 600, loopRound, gap);
            // (nothing to sound — every part off, the recording alone: as many rounds as loopNotes lays out)
            const period = loopRound + loopGap;
            loopRoundCount = period > 0 && window.LysSynth.loopRounds ? window.LysSynth.loopRounds(600, period)
                : list.length ? rounds.length / list.length : 1;
            vscode.postMessage({ type: 'webviewPerf', text: 'loop: ' + loopRoundCount
                + ' rounds of ' + (loopRound || 0).toFixed(3) + ' s' + (loopGap > 0 ? ' after a count-in of ' + loopGap.toFixed(3) + ' s' : '')
                + ', ' + rounds.length + ' notes' });
            return rounds;
        }
        // The plan of a loop's voices: its pieces, its rounds and the count-in before each.
        function loopVoicePlan() { return { pieces: spanPieces, rounds: loopRoundCount, gap: loopGap }; }
        // Where the list now sounding is: in a loop, the time into the round's music and, in a
        // count-in, how far into it (loopPlace); else the list's time less its count-in.
        function placeNow() {
            const lt = playOffset + playClock();
            // (a count-in before the music that starts at A — a first round from the A flag, or a Play
            // from A with Repeat off: the count shows A)
            if (lt < loopEntry) return { tau: Math.max(0, loopEntry - countInFix), counting: null };
            if (loopList && loopRound > 0 && window.LysSynth.loopPlace) return window.LysSynth.loopPlace(lt, countInFix, loopRound, loopGap);
            return { tau: Math.max(0, lt - countInFix), counting: null };
        }
        // Where a loop's new list (looped: its countFix is loopGap) starts to go on at "at" into
        // the round's music — or at the same moment of a count-in sounding now ("counting").
        function loopOffset(at, counting) {
            return counting !== null && counting !== undefined ? Math.min(counting, loopGap) : loopGap + at;
        }
        let loopRoundCount = 1;     // how many rounds looped() laid out last — the voice sings each
        let loopGap = 0;            // the count-in before each of those rounds (seconds of the list; 0: none)
        let loopEntry = 0;          // where the music first comes in when the first round starts from the A flag (list time; 0: from the round's start)
        // The stretch ▶ plays, from its bar's start to the next bar's (playSpanWindow), and how
        // long it lasts — the loop's round. The bars are the metronome's accented beats, in the
        // practice menu's tempo.
        let loopRound = 0;
        function spanOf(notes, span, once) {
            // The stretch is measured on EVERY part, the ones turned off kept (marked Z) and dropped
            // after (owner's report 2026-11-17: every part off but the recording, a loop's round was
            // 0 s — Play started again and again, the recording never sounding; a part with the last
            // bar's notes turned off shortened the round too). The callers' list is practiced(rawNotes).
            if (rawNotes) notes = practiced(rawNotes, true);
            const beats = playedBeats(), barEnd = playedBarEnd(), speed = playedSpeed();
            const bars = beats ? beats.filter(b => b.A).map(b => b.T / speed) : undefined;
            // And where the last bar ends (the server's closing bar line), the last bar's end.
            if (bars && typeof barEnd === 'number' && barEnd > 0) bars.push(barEnd / speed);
            // The A-B repeat's bars ({ ab: [from, to] }), as printed, once through (playBarsWindow).
            const w = span && span.ab && window.LysSynth.playBarsWindow
                ? window.LysSynth.playBarsWindow(notes, span.ab[0], span.ab[1], true, bars)
                : window.LysSynth.playSpanWindow
                ? window.LysSynth.playSpanWindow(notes, span ? span[0] : null, span ? span[1] : null, once, bars)
                : { notes: window.LysSynth.playSpan(notes, span ? span[0] : null, span ? span[1] : null, once), length: 0 };
            loopRound = w.length;
            spanStart = w.start || 0;
            spanPieces = w.pieces || [[spanStart, spanStart + loopRound]];
            return w.notes;
        }
        let spanStart = 0;          // where the stretch spanOf cut starts in the score's time (the count-in's bar)
        let spanPieces = null;      // the score's times the stretch plays, in order — the voice is cut from them
        // A count-in before what the user starts (practice.countIn): one bar of clicks, the music
        // moved after it (LysSynth.countIn). Null when off or there is no bar to count. Only a Play
        // counts in — a tempo change, the loop's next round and a seek go on at once.
        function counted(notes, start, origin) {
            if (!practice.countIn || !rawBeats || !window.LysSynth || !window.LysSynth.countIn) return null;
            const speed = playedSpeed();
            return window.LysSynth.countIn(notes, start, origin,
                playedBeats().map(b => ({ T: b.T / speed, A: b.A })));
        }
        let countInFix = 0;         // the played list's clock minus the score's, while a count-in plays
        // ---- The loop goes on with the text as it is now (owner's request 2026-11-17: a bar looped
        // while it is written by ear — the next round sounds the edit). The score changed while a
        // loop went round: the new notes were asked for (pendingLoopRefresh), and when they come
        // the round sounding plays to its end and the next starts with them. The rounds are laid
        // out ahead (looped), so the switch is a new list started at the round's end; one that
        // comes too late for it waits for the end of the next.
        var pendingLoopRefresh = false, loopSwitchTimer = null;
        function refreshLoop(message) {
            if (!playingNow() || !playClock || !loopList || lastSpan === undefined) return;
            const oldRound = loopRound, oldStart = spanStart, oldPieces = spanPieces;
            // (a loop's list is round after round from its 0 — a count-in first in each when there is one)
            const oldPeriod = loopRound + loopGap, lt = Math.max(0, playOffset + playClock());
            rawNotes = message.notes; rawParts = message.parts || null; rawBeats = message.beats || null; rawBarEnd = message.barEnd || null; rawExtra = message.extra || null; rawSung = message.sung || null; rawPlayed = message.played || null;
            playbackNotes = practiced(rawNotes);
            const list = spanOf(playbackNotes, lastSpan, false);
            const newRound = loopRound, newStart = spanStart, newPieces = spanPieces;
            // (the round sounding keeps its own until the switch: the position shown reads them)
            loopRound = oldRound; spanStart = oldStart; spanPieces = oldPieces;
            if (!list.length) return;
            let remain = oldRound > 0 ? oldPeriod - (lt % oldPeriod) : 0;
            if (remain < 0.25) remain += oldPeriod;
            if (loopSwitchTimer) clearTimeout(loopSwitchTimer);
            const gen = playGeneration;
            // A new list starts 0.15 s after it is handed over (startPlayback): handed over so
            // much before the round's end.
            loopSwitchTimer = setTimeout(() => {
                loopSwitchTimer = null;
                if (gen !== playGeneration || !playingNow()) return;
                loopRound = newRound; spanStart = newStart; spanPieces = newPieces;
                const rounds = looped(list);
                voicePlan = loopVoicePlan();
                startPlayback(rounds, 0, false, false, loopGap, spanStart);
                loopList = list;
            }, Math.max(0, (remain - 0.15) * 1000));
        }
        // Where a playback that ran to its end goes: round again while looping, else it stops.
        function playEnded() {
            const list = loopOn ? loopList : null;
            if (!list) { stopPlayback(); return; }
            const rounds = looped(list);
            voicePlan = loopVoicePlan();
            startPlayback(rounds, 0, false, false, loopGap, lastSpan === undefined ? 0 : spanStart);
            loopList = list;
        }

        // ---- The practice menu (owner's request 2026-10-25): a button that drops down the
        // controls a practice session wants and the header has no room for — the tempo (a slider,
        // 50–150%), the loop, the metronome, the parts on and off (a toggle each). A change while
        // playing goes on from the same place.
        // extraOn: the parts the score does not sound that the menu turned on; voicesOff: the sung
        // lyrics it turned off (owner's request 2026-11-08: both beside the parts).
        // levels / voiceLevels: the faders beside the parts and the voices (owner's request
        // 2026-11-09), part -> 0..1; a part at 1 is not listed.
        // voicesOn: the file's other singable lyrics the menu turned on (the score does not sing
        // them itself; session 1163).
        // instrumentsOn: the instruments turned on for parts the score only sings (session 1165).
        const practice = { speed: 1, muted: [], click: false, countIn: false, extraOn: [], voicesOff: [], voicesOn: [], instrumentsOn: [], levels: {}, voiceLevels: {} };
        // ---- The mixer (docs/transport-mixer-design.md §1 1-2; owner's decisions 2026-11-17): each line
        // muted (its toggle, as before), soloed (solo: when any line of a group is, only those of that
        // group sound — the score's parts and voices one group, the recording's parts the other; owner's
        // decision 2026-11-20) and faded; the score's group faded as
        // one (scoreLevel) and all on or off at once. What sounds is eff(): the menu's own state with
        // the solo and the group's fader applied — the lines themselves show their own.
        var solo = new Set();          // 'part:NAME', 'voice:KEY', 'rec:PART'
        var scoreLevel = 1;            // the score group's fader, 0..2
        // The score group on (owner's request 2026-11-18): off, none of its lines sounds, each line's
        // own on/off kept — on again, the lines turned on sound. (The recording's is recOn.)
        var scoreOn = true;
        // The score's All pressed in (owner's decision 2026-11-20): every part and voice the score
        // sounds is heard, the lines' own on/off — the parts one is after — left as they are, so All
        // let go is that choice again. (Until then All rewrote every line's on/off, and the choice
        // had to be made again.) The parts the score leaves out (extra), the voices it does not
        // sing and the sung parts' instruments are not its: they stay as their lines say.
        var scoreAll = false;
        // The recording's All, the same (its parts' own on/off, recPartsOff, kept).
        var recAllOn = false;
        // What a line's button says of Alt+click (owner's decision 2026-11-20): it keeps that line alone.
        const ONLY_THIS_HINT = 'Alt+click: only this one (the others off)';
        // The panel's button pressed while anything in it is not as it was (§6): the practice's or the mixer's.
        var practiceOn = false, mixerChanged = false;
        function paintPracticeBtn() { document.getElementById('practiceBtn').setAttribute('aria-pressed', String(practiceOn || mixerChanged)); }
        // The solo is each group's own (owner's decision 2026-11-20): a line soloed in the score
        // silences the score's other lines only — the recording sounds as its own group says — and
        // one soloed in the recording its other parts only. (Until then a solo in either group
        // silenced everything of both but the lines soloed.)
        function recSoloed() { for (const k of solo) { if (k.startsWith('rec:')) return true; } return false; }
        function scoreSoloed() { for (const k of solo) { if (!k.startsWith('rec:')) return true; } return false; }
        function eff() {
            // (the score's solo only: scoreSoloed, written out — the tests run eff on its own)
            let any = false;
            for (const k of solo) { if (!k.startsWith('rec:')) { any = true; break; } }
            if (!any && scoreLevel === 1 && scoreOn && !scoreAll) return practice;
            const heard = k => !any || solo.has(k);
            const parts = (rawParts || (lastPartsMessage && (lastPartsMessage.parts || []).concat(lastPartsMessage.extra || [])) || []).filter(Boolean);
            const voices = menuVoiceKeys || [];
            // All pressed in: every part and voice of the score heard, the lines' own on/off kept —
            // the group off (Play let go) too (owner's request 2026-11-20: All sounds every part,
            // whatever the other buttons say). The group off, All out: every line silent, the
            // lines' own on/off (practice) as they are.
            const own = scoreAll ? Object.assign({}, practice, { muted: [], voicesOff: [] })
                : !scoreOn ? Object.assign({}, practice, { muted: parts.slice(), voicesOff: voices.slice(), extraOn: [], voicesOn: [], instrumentsOn: [] })
                : practice;
            const out = Object.assign({}, own);
            if (any) {
                // A soloed line is HEARD — its own mute, its group off, an extra the score leaves
                // out, all aside (owner's decision 2026-11-17: S is "listen to this"; until then a line
                // muted by the All stayed silent soloed). The mute itself is kept: S off, it is back.
                const extras = rawExtra || (lastPartsMessage && lastPartsMessage.extra) || [];
                out.muted = own.muted.filter(n => !solo.has('part:' + n))
                    .concat(parts.filter(n => !heard('part:' + n) && !own.muted.includes(n)));
                out.extraOn = own.extraOn.filter(n => heard('part:' + n))
                    .concat(extras.filter(n => solo.has('part:' + n) && !own.extraOn.includes(n)));
                out.instrumentsOn = own.instrumentsOn.filter(n => heard('part:' + n));
                out.voicesOff = own.voicesOff.filter(k => !solo.has('voice:' + k))
                    .concat(voices.filter(k => !heard('voice:' + k) && !own.voicesOff.includes(k)));
                out.voicesOn = own.voicesOn.filter(k => heard('voice:' + k))
                    .concat(voices.filter(k => solo.has('voice:' + k) && !own.voicesOn.includes(k)));
            }
            if (scoreLevel !== 1) {
                out.levels = {};
                for (const n of parts) out.levels[n] = levelIn(practice.levels, n) * scoreLevel;
                out.voiceLevels = {};
                for (const k of voices) out.voiceLevels[k] = levelIn(practice.voiceLevels, k) * scoreLevel;
            }
            return out;
        }
        /** A mixer line's small toggle, S (the name is the mute: owner's decision 2026-11-17). */
        function mixerSmall(text, title) {
            const b = document.createElement('button');
            b.type = 'button';
            b.className = 'toggle msmall ' + (text === 'S' ? 'msolo' : 'mmute');
            b.textContent = text;
            b.title = title;
            b.setAttribute('aria-pressed', 'false');
            return b;
        }
        /** A line soloed or let go: every line repainted, what plays from here on with it. */
        function toggleSolo(key) {
            if (!key) return;
            if (solo.has(key)) solo.delete(key); else solo.add(key);
            refreshMixer();
            if (recPlayer) recPlayer.setLevel(recLevel());
            recReplayIfSwitched();
            practiceChanged(practice.speed);
        }
        function refreshMixer() {
            for (const refresh of partRefreshers) refresh();
            renderRecParts();
            showRecording();
            showPractice();
            showMixerHead();
        }
        /** The groups' heads (owner's request 2026-11-18: two buttons each): the group's on/off — the
         *  lines turned on sound, or none does, their own on/off kept — and All, pressed in while
         *  every line of the group is heard, their own on/off kept (owner's decision 2026-11-20:
         *  scoreAll / recAll); then the group's fader. */
        function showMixerHead() {
            const all = document.getElementById('scoreAllBtn');
            all.setAttribute('aria-pressed', String(scoreAll));
            all.title = scoreAll ? 'Every part of the score sounds — press to hear only the parts turned on below again'
                : 'Press to hear every part of the score; the parts turned on below are kept for when it is let go';
            const scoreOnBtn = document.getElementById('scoreOnBtn');
            scoreOnBtn.setAttribute('aria-pressed', String(scoreOn));
            scoreOnBtn.title = scoreOn ? 'The score sounds: the parts turned on below — press to silence it, each part\\'s own on or off kept'
                : 'The score is silent — press to sound the parts turned on below';
            const v = Math.round(scoreLevel * 100);
            document.getElementById('scoreLevelSlider').value = String(v);
            document.getElementById('scoreLevelValue').textContent = v === 100 ? '' : v + '%';
            // The recording's: its file's name is its on/off; All only when it has parts.
            document.getElementById('recOnBtn').setAttribute('aria-pressed', String(!!recOn));
            document.getElementById('recOnBtn').title = recOn ? 'The recording sounds with the score — press to silence it, each part\\'s own on or off kept'
                : 'Press to sound the recording with the score: the parts of it turned on';
            const recParts = (recording && recording.parts) || [];
            const recAll = document.getElementById('recAllBtn');
            recAll.hidden = recParts.length === 0;
            recAll.setAttribute('aria-pressed', String(recAllOn));
            recAll.title = recAllOn ? 'Every part of the recording sounds — press to hear only the parts turned on below again'
                : 'Press to hear every part of the recording; the parts turned on below are kept for when it is let go';
            const changed = solo.size > 0 || scoreLevel !== 1 || !scoreOn || scoreAll || recAllOn || practice.muted.length > 0 || practice.voicesOff.length > 0
                || practice.extraOn.length > 0 || practice.voicesOn.length > 0 || practice.instrumentsOn.length > 0
                || Object.keys(practice.levels).length > 0 || Object.keys(practice.voiceLevels).length > 0 || recOn;
            mixerChanged = changed;
            paintPracticeBtn();
        }
        // A fader runs 0 to 200% (owner's request 2026-11-09, session 1157): levelIn clamps to that.
        const FADER_MAX = 2;
        function levelIn(levels, name) {
            const v = levels[name];
            return typeof v === 'number' ? Math.min(FADER_MAX, Math.max(0, v)) : 1;
        }
        // What each line of the Parts list shows, refreshed after a reset (partsReset / fadersReset).
        let partRefreshers = [];
        // The document and score the page shows (updateContent's scoreKey), and the one the Parts
        // list was built for: the list is asked again when they differ while the menu is open
        // (owner's report 2026-11-09, session 1158: the menu kept the last score's parts after a
        // switch). Not on every render - a parts list costs the server a playback export: a part
        // given music by an edit is seen in the length's export, which every refresh asks for
        // already, and only then — or when a listed part sounds no more — is the list asked again
        // ('soundingParts', owner's report 2026-11-20); an edit that changes the voices' lines asks again too (voiceLinesChanged).
        // partsAskedFor: the sounding parts a list was last asked again for ('soundingParts').
        let lastScoreKey = null, partsKey = null, partsAskedFor = null;
        function requestParts() {
            partsKey = lastScoreKey;
            vscode.postMessage({ type: 'requestParts' });
        }
        let rawNotes = null, rawParts = null, rawBeats = null, rawBarEnd = null, rawExtra = null;  // the server's last answer, as written
        let rawSung = null, rawPlayed = null;    // the parts the score sings, and plays (a part may be both: two staves)
        let lastSpan;               // what plays: undefined the whole score, null/[lo, hi] a playSpan
        let lastOnce = false;
        let playOffset = 0, playClock = null;   // where the playback stands (startPlayback / midiStarted)
        // What the list practiced() built is timed by: the score's bars on the recording's (warp,
        // when the recording sounds with the score: the notes are on its seconds) and the speed
        // the times were divided by (with the recording, the recording's own rate: the practice
        // tempo times its panel's speed — the score follows the recording).
        let playBasis = null;
        var playSpan = 0;   // (startPlayback's stretch the recording ends with, 0 for none)
        function practiced(notes, keepSilent) {
            if (!window.LysSynth || !window.LysSynth.practiceNotes) return notes;
            const warp = warpNow();
            // With the recording, the SCORE's tempo sets the pace (owner's decision 2026-11-16): the
            // recording is played at the score's written length over its own (warpFit) — 131 written
            // over a recording of 131.8 plays it at 131/131.8 — times the practice tempo, and the
            // panel's speed on top (recordingPlan). recTempo is what the player is handed.
            const recTempo = warp ? practice.speed * window.LysSynth.warpFit(warp) : practice.speed;
            playBasis = { warp, recTempo, speed: warp ? window.LysSynth.recordingPlan(recSound(), recTempo).rate : practice.speed };
            const timed = warp ? window.LysSynth.warpNotes(notes, warp) : notes;
            // The parts turned off stay in the list, marked Z: nothing sounds them, and they still say
            // where the music is — a loop's round, the A-B window, and the bar lit while only the
            // recording sounds (owner's request 2026-11-17).
            return window.LysSynth.practiceNotes(timed, rawParts, playedBeats(), Object.assign({}, eff(), { speed: playBasis.speed }), rawExtra, true);
        }
        /** The score's beats and last bar line on the list's time base (playBasis), before the speed. */
        function playedBeats() {
            const w = playBasis && playBasis.warp;
            return rawBeats && w ? window.LysSynth.warpBeats(rawBeats, w) : rawBeats;
        }
        function playedBarEnd() {
            const w = playBasis && playBasis.warp;
            return typeof rawBarEnd === 'number' && w ? window.LysSynth.warpTime(w, rawBarEnd) : rawBarEnd;
        }
        function playedSpeed() { return playBasis ? playBasis.speed : practice.speed; }
        const practiceBtn = document.getElementById('practiceBtn');
        const practicePanel = document.getElementById('practicePanel');
        const clickBtn = document.getElementById('clickBtn');
        const countInBtn = document.getElementById('countInBtn');
        const tempoSlider = document.getElementById('tempoSlider');
        function showPractice() {
            const speedOn = practice.speed !== 1;
            document.getElementById('practiceSpeed').textContent = speedOn ? Math.round(practice.speed * 100) + '%' : '';
            // Pressed in while anything differs from the score as written.
            // (the parts are the mixer's, its own button says them: showMixerHead; Repeat has its
            // own button in the header, and A-B its flags on the page)
            practiceOn = speedOn || practice.click || practice.countIn;
            paintPracticeBtn();
            if (typeof showMixerHead === 'function' && document.getElementById('scoreAllBtn')) showMixerHead();
            // Repeat, the header's button (owner's decision 2026-11-19; it was a row of the panel).
            loopBtn.setAttribute('aria-pressed', String(loopOn));
            tempoSlider.value = String(Math.round(practice.speed * 100));
            document.getElementById('tempoValue').textContent = Math.round(practice.speed * 100) + '%';
            clickBtn.setAttribute('aria-pressed', String(practice.click));
            countInBtn.setAttribute('aria-pressed', String(practice.countIn));
            // The two resets under the parts (owner's request 2026-11-09): live while there is something to put back.
            document.getElementById('partsReset').disabled = scoreOn && !scoreAll && practice.muted.length === 0 && practice.extraOn.length === 0 && practice.voicesOff.length === 0 && practice.voicesOn.length === 0 && practice.instrumentsOn.length === 0;
            document.getElementById('fadersReset').disabled = Object.keys(practice.levels).length === 0 && Object.keys(practice.voiceLevels).length === 0
                && Object.keys(recLevels || {}).length === 0 && Object.keys(recPartLevels || {}).length === 0 && scoreLevel === 1;
            postPracticeState();
        }
        // The exports play as the menu does, the loop aside (owner's request 2026-11-07): the
        // extension keeps this page's menu for them (previewPractice) — and the recording, while it
        // sounds with the score (docs/recording-design.md §7: its bars, the panel and its fader).
        function postPracticeState() {
            const along = recOn && recording && recording.ready && !recording.error;
            const e = eff();    // (the exports sound as the mixer does: the solo, the group's fader)
            vscode.postMessage({ type: 'practiceState', practice: {
                speed: practice.speed, muted: e.muted.slice(), click: practice.click, countIn: practice.countIn,
                extraOn: e.extraOn.slice(), voicesOff: e.voicesOff.slice(), voicesOn: e.voicesOn.slice(),
                instrumentsOn: e.instrumentsOn.slice(),
                levels: Object.assign({}, e.levels), voiceLevels: Object.assign({}, e.voiceLevels),
                recording: along ? { bars: recording.bars.map(b => ({ start: b.start, end: b.end })), sound: recSound(), level: recLevel() } : null } });
        }
        /** The recording rendered whole for an export (recordingPlayer.render), sent back in pieces
         *  of ten seconds (a song's two channels are tens of megabytes). */
        async function renderRecordingFor(m) {
            const send = (extra) => vscode.postMessage(Object.assign({ type: 'recordingRendered', id: m.id }, extra));
            try {
                const player = recordingAlong() ? recPlayer : null;
                const out = player ? await player.render(recSound(), m.tempo) : null;
                if (!out) { send({ error: 'the recording is not loaded' }); return; }
                const piece = out.sampleRate * 10;
                const pieces = Math.max(1, Math.ceil(out.left.length / piece));
                for (let i = 0; i < pieces; i++) {
                    send({ index: i, pieces, length: out.left.length, sampleRate: out.sampleRate,
                        left: out.left.slice(i * piece, (i + 1) * piece), right: out.right.slice(i * piece, (i + 1) * piece) });
                }
            } catch (e) {
                send({ error: String(e && e.message || e) });
            }
        }
        // A change while playing goes on from the same place in the music: the list's time now
        // (its count-in aside, within the loop's round) to the score's time through the basis the
        // list was built on (its speed, and the recording's bars when the notes were on them),
        // and back through the new one. (oldSpeed is the caller's: the old basis already has it.)
        function practiceChanged(oldSpeed) {
            showPractice();
            const playing = playingNow();
            if (!playing || relaying || !rawNotes || !playClock) return;
            const old = playBasis || { warp: null, speed: oldSpeed };
            const oldOrigin = lastSpan === undefined ? 0 : spanStart;
            const place = placeNow();
            const tau = place.tau;
            const u = (oldOrigin + tau) * old.speed;
            const t = old.warp ? window.LysSynth.unwarpTime(old.warp, u) : u;
            playbackNotes = practiced(rawNotes);
            const u2 = playBasis.warp ? window.LysSynth.warpTime(playBasis.warp, t) : t;
            const p = u2 / playBasis.speed;
            if (lastSpan === undefined) {
                voicePlan = { pieces: null, rounds: 1 };
                startPlayback(playbackNotes, Math.max(0, p), false, false, 0, 0);
                return;
            }
            const list = spanOf(playbackNotes, lastSpan, lastOnce);
            loopList = loopOn ? list : null;
            const at = p - spanStart;
            const inside = at >= 0 && (!loopOn || at < loopRound);
            if (loopOn) {
                const rounds = looped(list);
                voicePlan = loopVoicePlan();
                startPlayback(rounds, inside ? loopOffset(at, place.counting) : 0, false, false, loopGap, spanStart);
            } else {
                voicePlan = { pieces: spanPieces, rounds: 1 };
                startPlayback(list, inside ? at : 0, false, false, 0, spanStart);
            }
            loopList = loopOn ? list : null;
        }
        var lastPartsMessage = null;    // the Parts list's last answer: drawn again when a recording comes or goes
        function renderParts(message) {
            lastPartsMessage = message;
            const list = document.getElementById('partsList');
            list.textContent = '';
            partRefreshers = [];
            const parts = message.parts || [], voices = message.voices || [], extra = message.extra || [];
            if (parts.length + voices.length + extra.length === 0) { list.textContent = '—'; return; }
            // One toggle each, pressed in while it sounds; the lists are by name, so a part muted
            // in another score stays muted.
            const without = (names, name) => names.filter(n => n !== name);
            // Every line of the score's group off, each as its own toggle reads (an Alt+click's
            // first half: the line clicked is then flipped on). (sungOnly and inScore are read at
            // the click, after they are set below.)
            // Only this score's lines: the lists are by name across scores, and another's are kept.
            const noLineOn = () => {
                const mine = new Set(parts.concat(extra)), myVoices = new Set(voices);
                practice.muted = practice.muted.filter(n => !mine.has(n)).concat(parts.filter(n => !sungOnly.has(n)));
                practice.instrumentsOn = practice.instrumentsOn.filter(n => !mine.has(n));
                practice.extraOn = practice.extraOn.filter(n => !mine.has(n));
                practice.voicesOff = practice.voicesOff.filter(k => !myVoices.has(k)).concat(voices.filter(k => inScore.has(k)));
                practice.voicesOn = practice.voicesOn.filter(k => !myVoices.has(k));
            };
            // One line a part: its toggle, and to its right a fader (owner's request 2026-11-09)
            // - 0 to 100%, the instrument's velocity or the voice's gain. The number follows the
            // thumb; the sound changes when it is let go (as the tempo's does), 100 means "as
            // written" and is not kept.
            // In the mixer (docs/transport-mixer-design.md §1 2): the name (pressed in: it sounds; a press mutes it), S,
            // then the fader. soloKey names the line for the solo (soloed: only the lines soloed sound).
            // byAll: a line All sounds while pressed in (the score's parts and the voices it sings).
            const toggle = (label, title, isOn, flip, cls, part, levels, levelKey, soloKey, byAll) => {
                const line = document.createElement('span');
                line.className = 'pline';
                const b = document.createElement('button');
                b.type = 'button';
                b.textContent = label;
                if (cls && cls.split(' ').includes('voice')) {
                    // The very drawing the Play button's badge is (renderVoiceBadge sets its state).
                    const mic = document.createElement('span');
                    mic.className = 'voice-badge';
                    mic.setAttribute('aria-hidden', 'true');
                    mic.appendChild(document.querySelector('#voiceBadge svg').cloneNode(true));
                    b.appendChild(mic);
                    const bar = document.createElement('span');
                    bar.className = 'voice-row-bar';
                    bar.hidden = true;
                    bar.setAttribute('aria-hidden', 'true');
                    bar.appendChild(document.createElement('span'));
                    b.appendChild(bar);
                }
                // (Alt+click — Option on a Mac — keeps this line alone: onlyThis)
                const hint = (title ? title + '\\n' : '') + ONLY_THIS_HINT;
                b.title = hint; b.dataset.title = hint;
                if (cls) for (const c of cls.split(' ')) b.classList.add(c);
                if (part) b.dataset.part = part;
                b.classList.add('pname');
                const sBtn = mixerSmall('S', 'Solo: of the score, only the lines soloed sound (the recording as it is)');
                const paint = () => {
                    const on = isOn();
                    b.setAttribute('aria-pressed', String(on));
                    sBtn.setAttribute('aria-pressed', String(solo.has(soloKey)));
                    // (the group off: every line of it dimmed, pressed or not as it is itself; All
                    // pressed in: its lines heard, each still showing its own on/off)
                    line.classList.toggle('unheard', (!on && !(scoreAll && byAll)) || (!scoreOn && !(scoreAll && byAll) && !solo.has(soloKey)) || (scoreSoloed() && !solo.has(soloKey)));
                };
                paint();
                // A line pressed while All is pressed in: All goes out and the line's own on/off
                // flips — what is heard is then the choice with this change (owner's decision 2026-11-20).
                // Alt+click: this line alone — every line of the score's group off, then this one on
                // (owner's decision 2026-11-20: the choice made again from nothing, in one click,
                // never silent on the way). All, pressed in, goes out as with a plain click.
                const mute = (e) => {
                    if (e && e.altKey) { scoreAll = false; noLineOn(); flip(); refreshMixer(); practiceChanged(practice.speed); return; }
                    if (scoreAll) { scoreAll = false; flip(); refreshMixer(); practiceChanged(practice.speed); return; }
                    flip(); paint(); showPractice(); practiceChanged(practice.speed);
                };
                b.addEventListener('click', mute);
                sBtn.addEventListener('click', () => toggleSolo(soloKey));
                line.appendChild(b);
                line.appendChild(sBtn);
                const fader = document.createElement('input');
                fader.type = 'range'; fader.min = '0'; fader.max = String(FADER_MAX * 100); fader.step = '1';
                fader.className = 'fader';
                // The number beside the thumb while it is not at 100 (session 1156: a click on the
                // slider's track puts the thumb where the click lands, and a thumb at the far left
                // was easy to miss - the part then sounded as good as silent); a double-click is 100.
                const num = document.createElement('span');
                num.className = 'flevel';
                const sayLevel = () => {
                    fader.title = 'Volume ' + fader.value + '% (double-click: 100%)';
                    fader.setAttribute('aria-label', levelKey + ' volume ' + fader.value + '%');
                    num.textContent = fader.value === '100' ? '' : fader.value + '%';
                };
                fader.value = String(Math.round(levelIn(levels, levelKey) * 100));
                sayLevel();
                fader.addEventListener('input', sayLevel);
                fader.addEventListener('change', () => {
                    const v = Number(fader.value);
                    if (v === 100) { delete levels[levelKey]; } else { levels[levelKey] = v / 100; }
                    sayLevel();
                    practiceChanged(practice.speed);
                });
                resetOnDoubleClick(fader, () => { fader.value = '100'; fader.dispatchEvent(new Event('change')); });
                line.appendChild(fader);
                line.appendChild(num);
                list.appendChild(line);
                partRefreshers.push(() => {
                    paint();
                    fader.value = String(Math.round(levelIn(levels, levelKey) * 100));
                    sayLevel();
                });
            };
            // The score's parts (owner's request 2026-10-25). A part the score only sings (a
            // singer's staff) has its instrument off by default - the voice sings - and on when
            // pressed, both then sounding (session 1165: the part's button is the instrument's,
            // the voice is the note's, each on its own).
            const sungOnly = new Set(message.sungOnly || []);
            for (const name of parts) {
                if (sungOnly.has(name)) {
                    toggle(name, 'Sung by its voice - press for its instrument too', () => practice.instrumentsOn.includes(name),
                        // (A mute left by another score, where this part is played, would keep it silent: it goes.)
                        () => { practice.muted = without(practice.muted, name); practice.instrumentsOn = practice.instrumentsOn.includes(name) ? without(practice.instrumentsOn, name) : practice.instrumentsOn.concat([name]); },
                        '', '', practice.levels, name, 'part:' + name);
                } else {
                    toggle(name, '', () => !practice.muted.includes(name),
                        () => { practice.muted = practice.muted.includes(name) ? without(practice.muted, name) : practice.muted.concat([name]); },
                        '', '', practice.levels, name, 'part:' + name, true);
                }
            }
            // The sung lyrics, one a part with a voice, beside the parts (owner's request
            // 2026-11-08; docs/singing-design.md §5): on, the part is sung; off, its instrument
            // plays in the voice's place. Every singable lyrics of the file is listed (owner's
            // request 2026-11-09, session 1163): the ones this score sings itself first, then in
            // italics the ones it does not, off until pressed - pressed, the voice sings in the
            // part's instrument's place.
            const inScore = new Set(message.voicesInScore || voices);
            // What each voice's line says (the lists stay by part): who sings, and on hover what.
            const voiceLabels = message.voiceLabels || {};
            const voiceTitles = message.voiceTitles || {};
            menuVoiceKeys = voices;
            menuVoiceNames = voiceLabels;
            const voiceLabel = name => voiceLabels[name] || name;
            const voiceTitle = name => voiceTitles[name] || voiceLabel(name);
            for (const name of voices) {
                if (!inScore.has(name)) continue;
                toggle(voiceLabel(name), voiceTitle(name), () => !practice.voicesOff.includes(name),
                    () => { practice.voicesOff = practice.voicesOff.includes(name) ? without(practice.voicesOff, name) : practice.voicesOff.concat([name]); },
                    'voice', name, practice.voiceLevels, name, 'voice:' + name, true);
            }
            for (const name of voices) {
                if (inScore.has(name)) continue;
                toggle(voiceLabel(name), voiceTitle(name) + ' (not in this score)', () => practice.voicesOn.includes(name),
                    () => { practice.voicesOn = practice.voicesOn.includes(name) ? without(practice.voicesOn, name) : practice.voicesOn.concat([name]); },
                    'voice extra', name, practice.voiceLevels, name, 'voice:' + name);
            }
            // The file's other parts, which this score does not sound: off until pressed (owner's
            // request 2026-11-08).
            for (const name of extra) {
                toggle(name, 'Not in this score — silent unless pressed', () => practice.extraOn.includes(name),
                    () => { practice.extraOn = practice.extraOn.includes(name) ? without(practice.extraOn, name) : practice.extraOn.concat([name]); },
                    'extra', '', practice.levels, name, 'part:' + name);
            }
            // (the recording has its own group in the mixer: its on/off, solo and fader, its parts — §1 1)
            renderVoiceBadge();     // the voices' signs on their fresh toggles
            showMixerHead();        // (the group's All, now that its lines are known)
        }
        function closePractice() {
            practicePanel.hidden = true;
            practiceBtn.setAttribute('aria-expanded', 'false');
        }
        practiceBtn.addEventListener('click', () => {
            if (!practicePanel.hidden) { closePractice(); return; }
            const b = practiceBtn.getBoundingClientRect();
            practicePanel.hidden = false;
            raisePanel(practicePanel);
            if (practicePanel.dataset.moved) clampPanel(practicePanel);
            else {
                practicePanel.style.top = (document.querySelector('.toolbar').getBoundingClientRect().bottom + 6) + 'px';
                practicePanel.style.right = 'auto';
                practicePanel.style.left = Math.max(6, Math.min(b.left, window.innerWidth - practicePanel.offsetWidth - 6)) + 'px';
            }
            practiceBtn.setAttribute('aria-expanded', 'true');
            // The file's own folds, as last left; a file whose panel opens for the first time opens
            // with every group folded (owner's requests 2026-11-17).
            applyFolds();
            if (recording) showRecording();
            requestParts();
        });
        // It closes as the Range panel does, by its × and its button, and stays open beside the
        // score (owner's request 2026-11-02: a click on the score closed it).
        document.getElementById('practiceClose').addEventListener('click', closePractice);
        // Each group folded or opened by its name (owner's decision 2026-11-17, §6), kept PER FILE in the
        // workspace by the extension (panelFolds, beside the A-B ranges); folded, its head (All, the
        // fader) stays. A file whose panel was never opened opens with every group folded.
        var folds = {};
        var foldsChart = null, foldsKept = null;    // the file shown, and its folds as the extension kept them (null: none)
        function applyFolds() {
            const all = !foldsKept;
            folds = {};
            for (const g of Array.from(practicePanel.querySelectorAll('.mgroup[data-fold]')))
                folds[g.dataset.fold] = all ? true : !!foldsKept[g.dataset.fold];
            if (all) keepFolds();   // (opened once: from now on, as left)
            showFolds();
        }
        function keepFolds() {
            foldsKept = Object.assign({}, folds);
            if (foldsChart) vscode.postMessage({ type: 'panelFolds', chart: foldsChart, folds: foldsKept });
        }
        function showFolds() {
            for (const g of Array.from(practicePanel.querySelectorAll('.mgroup[data-fold]'))) {
                const folded = !!folds[g.dataset.fold];
                g.classList.toggle('folded', folded);
                g.querySelector('.mfold').setAttribute('aria-expanded', String(!folded));
            }
        }
        for (const b of Array.from(practicePanel.querySelectorAll('.mfold'))) {
            b.addEventListener('click', () => {
                const key = b.closest('.mgroup').dataset.fold;
                folds[key] = !folds[key];
                keepFolds();
                showFolds();
            });
        }
        showFolds();
        // The Range and practice panels move where they are dragged (owner's request
        // 2026-10-29), inside the preview: what goes past its edge is cut off there, and a panel
        // keeps PANEL_KEEP pixels in sight on all four sides alike, the top one being the toolbar's
        // lower edge, behind which it goes (owner's request 2026-11-02), so it can always be taken
        // back. Once moved it stays where it was put, also when opened again.
        const PANEL_KEEP = 48;
        function clampPanel(panel) {
            if (panel.hidden || !panel.dataset.moved) return;
            const r = panel.getBoundingClientRect();
            const under = document.querySelector('.toolbar').getBoundingClientRect().bottom;
            const left = Math.min(Math.max(r.left, PANEL_KEEP - r.width), window.innerWidth - PANEL_KEEP);
            const top = Math.min(Math.max(r.top, under + PANEL_KEEP - r.height), window.innerHeight - PANEL_KEEP);
            panel.style.right = 'auto';
            panel.style.left = left + 'px';
            panel.style.top = top + 'px';
        }
        // The one pressed or opened last stands in front of the other (owner's request
        // 2026-11-02: the practice panel always did, being later in the page). Both stay under
        // the toolbar's z-index.
        function raisePanel(panel) {
            for (const p of [rangePanel, practicePanel]) p.style.zIndex = p === panel ? '6' : '5';
        }
        // A press anywhere but an input starts it, once it has gone 4 px: a click on a
        // button or a cell stays a click, and the click a drag ends with does nothing.
        function installPanelDrag(panel) {
            let drag = null, dragged = false;
            panel.addEventListener('pointerdown', (e) => {
                raisePanel(panel);
                dragged = false;
                if (e.button !== 0 || e.target.closest('input, select, textarea')) return;
                const r = panel.getBoundingClientRect();
                drag = { id: e.pointerId, x: e.clientX, y: e.clientY, left: r.left, top: r.top, moving: false };
            });
            panel.addEventListener('pointermove', (e) => {
                if (!drag || e.pointerId !== drag.id) return;
                const dx = e.clientX - drag.x, dy = e.clientY - drag.y;
                if (!drag.moving) {
                    if (Math.abs(dx) + Math.abs(dy) < 4) return;
                    drag.moving = true;
                    panel.setPointerCapture(e.pointerId);
                    panel.classList.add('moving');
                }
                panel.dataset.moved = '1';
                panel.style.right = 'auto';
                panel.style.left = (drag.left + dx) + 'px';
                panel.style.top = (drag.top + dy) + 'px';
                clampPanel(panel);
                e.preventDefault();
            });
            const end = (e) => {
                if (!drag || e.pointerId !== drag.id) return;
                dragged = drag.moving;
                drag = null;
                panel.classList.remove('moving');
            };
            panel.addEventListener('pointerup', end);
            panel.addEventListener('pointercancel', end);
            panel.addEventListener('click', (e) => {
                if (!dragged) return;
                dragged = false;
                e.stopPropagation();
                e.preventDefault();
            }, true);
        }
        installPanelDrag(rangePanel);
        installPanelDrag(practicePanel);
        window.addEventListener('resize', () => { clampPanel(rangePanel); clampPanel(practicePanel); });

        // ---- The recording beside the chart (docs/recording-design.md) ----------------------
        // The extension sends 'recording' when the chart has a file beside it (or no longer has):
        // its bars and its sound. The recording is a group of the practice panel (its head: on/off
        // and its fader — docs/recording-design.md §1 7). On, Play sounds it with the score
        // through LysSynth.RecordingPlayer (resampled, or stretched by Signalsmith Stretch), on the
        // score's clock: the score's notes are moved bar by bar onto the recording's seconds
        // (timeWarp), so the score plays as the recording does, and a bar clicked or the bars
        // selected play from there in both. A recording whose bars are not placed (only attached),
        // or whose bar count differs from the score's, plays alone.
        // (var: stopPlayback and Play, defined above, reach them before this line has run.)
        var recording = null;      // { chart, name, bars, pickup, ready, error }
        var recPlayer = null;
        var recOn = false;
        var recLevels = {};        // the fader, as the parts' are: { Recording: 0..2 } while not 100%
        // The recording's parts (Orecchiante's parts, docs/recording-design.md §8): the ones turned off,
        // and the faders not at 100% — the page's own, as the score's parts are (not kept).
        var recPartsOff = [];
        var recPartLevels = {};
        // The panel's own setting: the octave (owner's decision 2026-11-19 — the speed, the
        // semitones, the cents and "pitch with speed" are gone), kept per recording by the extension.
        let recSettings = { octave: 0 };
        const recOnBtn = document.getElementById('recOnBtn');
        const recLevelSlider = document.getElementById('recLevelSlider');
        // The chart's own transpose (the file's top-level transpose and the drawn score's, in
        // semitones — owner's decision 2026-11-16): the recording, made in the chart's written key,
        // follows it on top of the panel's octave. The extension says it with each picture.
        var chartShift = 0;
        /** What the recording is played with: the panel's octave and the chart's transpose. */
        function recSound() { return { octave: recSettings.octave, chart: chartShift }; }
        function recordingPlayer() {
            const cfg = window.lysSynthConfig;
            if (!recPlayer && window.LysSynth && cfg && cfg.stretch) {
                recPlayer = new window.LysSynth.RecordingPlayer(audioContext(), cfg.stretch);
                // (alone, its end is the playback's; with the score, the score's end is)
                recPlayer.onEnded = () => { if (!playClock) setPlayUi(false); };
            }
            return recPlayer;
        }
        /** Faders 100% stands at the right of the recording's Octave row, where there is room (owner's
         *  request 2026-11-19: it had a line of its own at the panel's foot) — and, with no recording
         *  (its group hidden), beside As scored. */
        function placeFadersReset() {
            const row = document.querySelector(recording ? '#recOctRow .tempo' : '#partsReset').closest('.tempo');
            row.appendChild(document.getElementById('fadersReset'));
        }
        function recStatus(text) {
            document.getElementById('recStatus').textContent = text || '';
        }
        function signed(n) { return n > 0 ? '+' + n : String(n); }
        function showRecording() {
            document.getElementById('recOctValue').textContent = signed(recSettings.octave);
            // (the score's transpose has no control here: said on the row)
            document.getElementById('recOctRow').title = chartShift !== 0
                ? 'The recording follows the score\\'s transpose (' + signed(chartShift) + ' semitones)' : '';
            showMixerHead();
            const level = Math.round(levelIn(recLevels, 'Recording') * 100);    // (the fader's own, not the solo's)
            recLevelSlider.value = String(level);
            recLevelSlider.title = 'Volume ' + level + '% (double-click: 100%)';
            recLevelSlider.setAttribute('aria-label', 'Recording volume ' + level + '%');
            document.getElementById('recLevelValue').textContent = level === 100 ? '' : level + '%';
            if (recPlayer) recPlayer.setLevel(recLevel());
            postPracticeState();    // (the exports mix the recording in as it sounds now)
        }
        /** The recording's level as it sounds: its fader. A solo in the score's group does not
         *  silence it (each group's own solo — owner's decision 2026-11-20); its own parts' solo is
         *  recPartMix's. */
        function recLevel() { return levelIn(recLevels, 'Recording'); }
        /** Each part's level for the player: 0 off, else its fader (§8). */
        function recPartMix() {
            const out = {};
            // (soloed lines: the parts not soloed silent)
            // …and a soloed part sounds though it is off (S is "listen to this", owner's decision 2026-11-17)
            const soloParts = recSoloed();
            for (const name of (recording && recording.parts) || [])
                // (All pressed in: every part heard, its own on/off kept — recAllOn)
                out[name] = (soloParts ? !solo.has('rec:' + name) : recPartsOff.includes(name) && !recAllOn) ? 0 : levelIn(recPartLevels, name);
            return out;
        }
        /** A part turned on or off, or its fader moved: between parts the gains change at once;
         *  from the recording to its parts (or back) what plays goes on from here (§8). */
        function applyRecParts() {
            renderRecParts();
            showPractice();
            recReplayIfSwitched();
        }
        /** The parts' levels to the player; when that switches what sounds (the recording ⇄ its
         *  parts), what plays goes on from here. */
        function recReplayIfSwitched() {
            if (!recPlayer) return;
            if (!recPlayer.setPartLevels(recPartMix())) return;
            if (playClock && playBasis && playBasis.warp) practiceChanged(practice.speed);
            else if (recPlayer.playing) void recPlayer.update(recSound());
        }
        /** The parts' lines under the panel's Recording line: a toggle and a fader each, in the
         *  order sent (shownPartOrder), greyed until loaded. */
        function renderRecParts() {
            const names = (recording && recording.parts) || [];
            const loaded = (recording && recording.partsLoaded) || null;
            document.getElementById('recPartsRow').hidden = names.length === 0;
            const box = document.getElementById('recPartsRows');
            box.textContent = '';
            for (const name of names) box.appendChild(recPartLine(name, !!loaded && loaded.includes(name)));
        }
        function recPartLine(name, ready) {
            const line = document.createElement('span');
            line.className = 'pline rec-part';
            const b = document.createElement('button');
            b.type = 'button';
            b.textContent = name;
            b.dataset.recPart = name;
            b.disabled = !ready;
            b.title = ready ? 'The recording\\'s ' + name + ' (a part of it): on or off\\n' + ONLY_THIS_HINT
                : (recording && recording.partsError && recording.partsError[name]) || 'The recording\\'s parts are loading';
            b.classList.add('pname');
            // (its own on/off, kept while the recording is off: the line is dimmed then)
            const on = !recPartsOff.includes(name);
            b.setAttribute('aria-pressed', String(on));
            // M and S, as the score's lines (the mixer, §1 2).
            // (its name mutes it: no M — owner's decision 2026-11-17)
            const sBtn = mixerSmall('S', 'Solo: of the recording, only the parts soloed sound (the score as it is)');
            sBtn.setAttribute('aria-pressed', String(solo.has('rec:' + name)));
            sBtn.disabled = !ready;
            line.classList.toggle('unheard', (!on && !recAllOn) || (!recOn && !recAllOn && !solo.has('rec:' + name)) || (recSoloed() && !solo.has('rec:' + name)));
            const mute = (e) => {
                recAllOn = false;   // (pressed while All is in: All goes out, as the score's lines)
                // Alt+click: this part alone (as the score's lines — owner's decision 2026-11-20).
                if (e && e.altKey) recPartsOff = ((recording && recording.parts) || []).filter(n => n !== name);
                else recPartsOff = recPartsOff.includes(name) ? recPartsOff.filter(n => n !== name) : recPartsOff.concat([name]);
                applyRecParts();
                showMixerHead();
            };
            b.addEventListener('click', mute);
            sBtn.addEventListener('click', () => toggleSolo('rec:' + name));
            const fader = document.createElement('input');
            fader.type = 'range'; fader.min = '0'; fader.max = String(FADER_MAX * 100); fader.step = '1';
            fader.className = 'fader';
            fader.disabled = !ready;
            const num = document.createElement('span');
            num.className = 'flevel';
            const say = () => {
                fader.title = 'Volume ' + fader.value + '% (double-click: 100%)';
                fader.setAttribute('aria-label', name + ' volume ' + fader.value + '%');
                num.textContent = fader.value === '100' ? '' : fader.value + '%';
            };
            fader.value = String(Math.round(levelIn(recPartLevels, name) * 100));
            say();
            fader.addEventListener('input', say);
            fader.addEventListener('change', () => {
                const v = Number(fader.value);
                if (v === 100) delete recPartLevels[name]; else recPartLevels[name] = v / 100;
                applyRecParts();
            });
            resetOnDoubleClick(fader, () => { fader.value = '100'; fader.dispatchEvent(new Event('change')); });
            line.appendChild(b);
            line.appendChild(sBtn);
            line.appendChild(fader);
            line.appendChild(num);
            return line;
        }
        function recChanged() {
            // Kept for this recording, for the next time it is opened (recording.ts).
            if (recording) vscode.postMessage({ type: 'recordingSettings', chart: recording.chart, name: recording.name, settings: recSettings });
            recApply();
        }
        /** The sound follows the panel's settings and the chart's transpose as they are now. */
        function recApply() {
            showRecording();
            // With the score, the score follows the recording's speed: both start again from here.
            if (playClock && playBasis && playBasis.warp) { practiceChanged(practice.speed); return; }
            if (recPlayer && recPlayer.playing) void recPlayer.update(recSound());
        }
        /** Whether the recording sounds with the next Play: on, and loaded. */
        function recordingAlong() {
            // (on — or one of its parts soloed: a soloed line is heard with its group off, owner's decision 2026-11-17)
            // (…or its All pressed in: All sounds every part whatever the group's button says — 2026-11-20)
            return !!((recOn || recSoloed() || (recAllOn && recording && (recording.parts || []).length > 0)) && recording && recording.ready && recordingPlayer());
        }
        /** The score's bars on the recording's (LysSynth.timeWarp), or null: the recording off or
         *  not loaded, or the two not lined up. */
        function warpNow() {
            if (!recordingAlong() || !window.LysSynth.timeWarp) return null;
            return window.LysSynth.timeWarp(rawBeats, rawBarEnd, recording);
        }
        /** Why the recording plays alone (warpNow null while it is on), in words. */
        function recAloneReason() {
            if (!recording || recording.bars.length === 0) return 'No bars are placed in the recording yet: it plays alone';
            const score = rawBeats && typeof rawBarEnd === 'number' && window.LysSynth.scoreBars
                ? window.LysSynth.scoreBars(rawBeats, rawBarEnd).length : 0;
            return 'The score plays ' + score + ' bars and the recording places ' + recording.bars.length + ': it plays alone';
        }
        /** The recording with the score, from the score's clock: \`t0\` is the context time the
         *  list's \`offset\` sounds at, \`origin\` the score's played time the list's 0 is (a stretch's
         *  start), the count-in's lead aside. Played time is the recording's second over the speed
         *  (the notes were moved onto its seconds), so it starts from there; in a count-in that
         *  begins before the recording's 0 it waits. Looping, it loops the stretch's seconds.
         *  Returns where it ends, in the list's time (0 when it does not play). */
        function startRecordingAlong(t0, offset, origin, looping) {
            const w = playBasis && playBasis.warp;
            if (!w || !recordingAlong()) return 0;
            const k = playBasis.speed;
            const p0 = origin + offset - countInFix;
            let sec = p0 * k, at = t0;
            if (sec < 0) { at = t0 - p0; sec = 0; }
            // A count-in before each round (loopGap): the recording goes on through it as through the
            // first one — the seconds before the stretch, silence where they are before its 0
            // (RecordingPlayer: a loop may begin before the recording does).
            recPlayer.presetLoop(looping ? { from: (origin - loopGap) * k, to: (origin + loopRound) * k } : null);
            recPlayer.setLevel(recLevel());
            void recPlayer.play(sec, recSound(), { tempo: playBasis.recTempo ?? practice.speed, at });
            if (looping) return 0;
            // A bar heard with the recording (Alt+M): the recording stops with the bar.
            if (playSpan) return playSpan + countInFix;
            // A stretch (the A-B bars) once: it ends where the stretch does, the recording with it —
            // not at the recording's end (owner's report 2026-11-17: Repeat off, the recording went
            // on past B, and nothing lit, the score's notes over).
            if (lastSpan) return loopRound + countInFix;
            return recPlayer.duration / k - origin + countInFix;
        }
        function setRecording(m) {
            if (m.none) {
                if (recPlayer) recPlayer.unload();
                const had = !!recording;
                recording = null;
                recOn = false;
                recAllOn = false;
                document.getElementById('mixRecGroup').hidden = true;   // (the mixer's recording group: §1 1)
                placeFadersReset();
                if (had && lastPartsMessage) renderParts(lastPartsMessage);
                return;
            }
            const same = recording && recording.chart === m.chart && recording.name === m.name;
            const fresh = !recording;
            const before = recording;
            recording = { chart: m.chart, name: m.name || '', bars: m.bars || [], pickup: !!m.pickup, ready: same ? recording.ready : false, error: m.error || null,
                // Its parts by name (§8); loaded when recordingParts comes (after the sound).
                parts: Array.isArray(m.parts) ? m.parts : [], partsLoaded: same ? before.partsLoaded : null, partsError: same ? before.partsError : null };
            // Another recording: its own octave, as kept the last time (recording.ts), else as recorded.
            if (!same) {
                const kept = m.settings && typeof m.settings === 'object' ? Number(m.settings.octave) : 0;
                recSettings = { octave: Number.isFinite(kept) ? Math.max(-2, Math.min(2, Math.round(kept))) : 0 };
            }
            if (!same) { recPartsOff = []; recPartLevels = {}; recAllOn = false; }
            // A chart just made from this recording starts with it on: Play sounds it (owner's
            // request 2026-11-16; the extension says so once, recording.ts justConverted).
            if (m.turnOn) recOn = true;
            document.getElementById('mixRecGroup').hidden = false;
            placeFadersReset();
            if (fresh && lastPartsMessage) renderParts(lastPartsMessage);
            document.getElementById('recName').textContent = recording.name;
            document.getElementById('recName').title = recording.name;
            // (said only when something is to be said — owner's request 2026-11-19: "4:15 — 118 bars
            // placed" took a line of the panel; the bars are in the header, showBarInfo)
            const barsSaid = recording.bars.length > 0 ? '' : 'No bars placed yet: the recording plays alone';
            if (m.error) {
                recording.ready = false;
                recStatus(m.error);
            } else if (m.preparing) {
                recStatus('Decoding the recording…');
            } else if (m.bytes) {
                const player = recordingPlayer();
                if (!player) { recStatus('The preview\\'s sound is not ready'); return; }
                recStatus('Loading…');
                // (kept: the parts are loaded once the sound is, which drops any before it — §8)
                recording.loading = player.load(m.bytes);
                recording.loading.then(seconds => {
                    if (!recording || recording.chart !== m.chart) return;
                    recording.ready = true;
                    postPracticeState();
                    recStatus(barsSaid);
                }, err => {
                    if (recording) recording.ready = false;
                    recStatus('The recording cannot be decoded: ' + (err && err.message || err));
                });
            } else {
                recStatus(barsSaid);
            }
            showRecording();
        }
        /** The recording's parts (recordingParts, §8), decoded into the player once the recording
         *  itself is in; their lines then turn live, and a part that cannot be used says why. */
        async function loadRecordingParts(m) {
            const rec = recording;
            if (!rec || rec.chart !== m.chart || rec.name !== m.name) return;
            const player = recordingPlayer();
            if (!player) return;
            try { if (rec.loading) await rec.loading; } catch (e) { return; }
            if (recording !== rec) return;
            const list = (m.parts || []).filter(p => p.bytes);
            const errors = {};
            for (const p of m.parts || []) if (p.error) errors[p.name] = p.error;
            // (decoded when first played — RecordingPlayer.ensureParts; a part that will not decode is said then)
            rec.partsLoaded = player.loadParts(list).slice();
            rec.partsError = errors;
            player.onPartsDecoded = failed => {
                if (recording !== rec) return;
                for (const f of failed) { const i = f.indexOf(':'); errors[f.slice(0, i)] = 'Cannot be decoded:' + f.slice(i + 1); }
                rec.partsLoaded = player.partsLoaded.slice();
                renderRecParts();
                sayRecPartErrors(errors);
                // The recording played on while they were decoded: now the parts, from here.
                recReplayIfSwitched();
            };
            player.setPartLevels(recPartMix());
            renderRecParts();
            sayRecPartErrors(errors);
        }
        function sayRecPartErrors(errors) {
            const bad = Object.keys(errors);
            if (bad.length > 0) recStatus('Parts not usable: ' + bad.map(n => n + ' (' + errors[n] + ')').join(', '));
        }
        /** Plays the recording alone from its second \`sec\` (its bars not lined up with the score's). */
        function playRecordingFrom(sec) {
            const player = recordingPlayer();
            if (!player || !recording || !recording.ready) return false;
            stopPlayback();
            audio();   // resumes the page's context (and says so when the browser holds it)
            setPlayUi(true);
            player.presetLoop(null);
            player.setLevel(recLevel());
            void player.play(sec, recSound(), { tempo: practice.speed });
            return true;
        }
        /** The recording on or off (its group's head button). While playing, what plays goes on
         *  from the same place with the recording or without it. */
        function flipRecording() {
            // (its parts' own on/off as they are: owner's request 2026-11-18)
            recOn = !recOn;
            renderRecParts();
            if (recOn && recording && !recording.ready) {
                recStatus(recording.error || 'The recording is still loading — it sounds from the next Play once it is ready');
            }
            showRecording();
            if (!playClock && recPlayer && recPlayer.playing) {
                // (the recording playing alone)
                recPlayer.stop();
                setPlayUi(false);
                return;
            }
            practiceChanged(practice.speed);
        }
        /** The recording on but not lined up with the score (playBasis has no warp): it plays alone
         *  from its start, and says why. False when the score is to play. */
        function recordingAlone() {
            if (playBasis && playBasis.warp && playBasis.warp.counts) recCountsDiffer(playBasis.warp.counts);
            if (!recordingAlong() || (playBasis && playBasis.warp)) return false;
            const why = recAloneReason();
            recStatus(why);
            flashInfo(why, 5000);
            playRecordingFrom(0);
            return true;
        }
        /** The score and the recording a bar apart or more (Orecchiante's request 11, the owner
         *  2026-11-16): they still play together as far as the shorter goes, and the panel says so. */
        function recCountsDiffer(counts) {
            const n = Math.min(counts.score, counts.rec);
            const why = 'The score plays ' + counts.score + ' bars and the recording places ' + counts.rec
                + ': they play together to bar ' + n + ' — past a bar added or taken out they are a bar apart';
            recStatus(why);
            flashInfo(why, 5000);
        }
        /**
         * A slider's double-click puts it back (owner's requests 2026-11-16). Told by the presses
         * themselves, not the browser's dblclick: the second press of a double-click moved the
         * thumb to where it landed, and a double-click the system did not count as one (a little
         * slow, the mouse moved a pixel) left it there (owner's report 2026-11-16). So a second
         * press soon after the first and near it is no press on the track at all: its default is
         * stopped (the thumb stays) and the slider is reset.
         */
        function resetOnDoubleClick(slider, reset) {
            let last = -Infinity, lastX = 0;
            const DOUBLE_MS = 500, NEAR_PX = 6;
            slider.addEventListener('pointerdown', e => {
                if (e.button !== 0) return;
                if (e.timeStamp - last < DOUBLE_MS && Math.abs(e.clientX - lastX) <= NEAR_PX) {
                    e.preventDefault();
                    last = -Infinity;
                    slider.__resetting = true;
                    if (!slider.disabled) reset();
                    return;
                }
                last = e.timeStamp;
                lastX = e.clientX;
            });
            // (the mouse press behind a stopped pointer press: kept from the slider too)
            slider.addEventListener('mousedown', e => { if (slider.__resetting) { e.preventDefault(); } });
            slider.addEventListener('pointerup', () => { setTimeout(() => { slider.__resetting = false; }, 0); });
            slider.addEventListener('dblclick', e => e.preventDefault());
        }
        function setRecLevel(v) {
            if (v === 100) delete recLevels.Recording; else recLevels.Recording = v / 100;
            showRecording();
            showPractice();
        }
        recOnBtn.addEventListener('click', flipRecording);
        // The recording's All, as the score's (owner's decision 2026-11-20; recAllOn): pressed in,
        // every part of it heard; let go, the parts turned on below again — never rewritten by it.
        document.getElementById('recAllBtn').addEventListener('click', () => {
            recAllOn = !recAllOn;
            applyRecParts();
            showMixerHead();
            // (the recording's button out: All brings the recording in or takes it out — what plays is planned again)
            if (!recOn) { showRecording(); practiceChanged(practice.speed); }
        });
        // The mixer's group heads (§1 2). The recording's: its on/off and All (above), its fader. (No group
        // solo: the other group's on/off released does it.)
        // The score's on/off: the lines' own on/off kept (owner's request 2026-11-18).
        document.getElementById('scoreOnBtn').addEventListener('click', () => {
            scoreOn = !scoreOn;
            refreshMixer();
            practiceChanged(practice.speed);
        });
        // The score's All (owner's decision 2026-11-20; scoreAll): pressed in, every part and voice of
        // the score heard; let go, the parts turned on below — never rewritten by it — again. With
        // the group off (Play let go) nothing sounds still; the group's fader is over every line's.
        document.getElementById('scoreAllBtn').addEventListener('click', () => {
            scoreAll = !scoreAll;
            refreshMixer();
            practiceChanged(practice.speed);
        });
        const scoreLevelSlider = document.getElementById('scoreLevelSlider');
        scoreLevelSlider.addEventListener('input', () => {
            document.getElementById('scoreLevelValue').textContent = scoreLevelSlider.value === '100' ? '' : scoreLevelSlider.value + '%';
        });
        function setScoreLevel(v) {
            scoreLevel = Math.min(FADER_MAX, Math.max(0, v / 100));
            showMixerHead();
            showPractice();
            practiceChanged(practice.speed);
        }
        scoreLevelSlider.addEventListener('change', () => setScoreLevel(Number(scoreLevelSlider.value)));
        resetOnDoubleClick(scoreLevelSlider, () => setScoreLevel(100));
        recLevelSlider.addEventListener('input', () => {
            document.getElementById('recLevelValue').textContent = recLevelSlider.value === '100' ? '' : recLevelSlider.value + '%';
        });
        recLevelSlider.addEventListener('change', () => setRecLevel(Number(recLevelSlider.value)));
        resetOnDoubleClick(recLevelSlider, () => setRecLevel(100));
        document.getElementById('recOctDown').addEventListener('click', () => { recSettings.octave = Math.max(-2, recSettings.octave - 1); recChanged(); });
        document.getElementById('recOctUp').addEventListener('click', () => { recSettings.octave = Math.min(2, recSettings.octave + 1); recChanged(); });
        // The slider says its value as it moves and sets the tempo where it is let go: a
        // playback restarts once, not at every step of a drag.
        function setSpeed(percent) {
            const old = practice.speed;
            practice.speed = percent / 100;
            if (old !== practice.speed) practiceChanged(old);
        }
        tempoSlider.addEventListener('input', () => {
            document.getElementById('tempoValue').textContent = tempoSlider.value + '%';
        });
        tempoSlider.addEventListener('change', () => setSpeed(Number(tempoSlider.value)));
        // A double-click is 100 %, as the reset beside it (owner's request 2026-11-16).
        resetOnDoubleClick(tempoSlider, () => { tempoSlider.value = '100'; setSpeed(100); showPractice(); });
        tempoSlider.title = 'Double-click: 100%';
        // The parts as the score shown has them (owner's request 2026-11-09): nothing muted, the
        // parts it leaves out off, every voice sung. The lists are by name across files, so this
        // is also the way out of a part silenced in another file.
        document.getElementById('partsReset').addEventListener('click', () => {
            // (As scored, the score's group alone: the recording has its own head button — §5)
            scoreOn = true;
            scoreAll = false;
            practice.muted = []; practice.extraOn = []; practice.voicesOff = []; practice.voicesOn = []; practice.instrumentsOn = [];
            for (const refresh of partRefreshers) refresh();
            showMixerHead();
            practiceChanged(practice.speed);
        });
        // Every fader back to 100% — the objects stay (the lines hold them), their entries go.
        document.getElementById('fadersReset').addEventListener('click', () => {
            for (const k of Object.keys(practice.levels)) delete practice.levels[k];
            for (const k of Object.keys(practice.voiceLevels)) delete practice.voiceLevels[k];
            delete recLevels.Recording;     // (the recording's too: §1 7)
            for (const k of Object.keys(recPartLevels)) delete recPartLevels[k];   // (and its parts': §8)
            scoreLevel = 1;                 // (and the score group's: §5)
            for (const refresh of partRefreshers) refresh();
            renderRecParts();
            if (recPlayer) recPlayer.setPartLevels(recPartMix());
            showRecording();
            practiceChanged(practice.speed);
        });
        clickBtn.addEventListener('click', () => {
            practice.click = !practice.click;
            practiceChanged(practice.speed);
        });
        // The count-in (owner's choice 2026-10-26): from the next Play on, nothing restarts — but a
        // loop going round lays its rounds out again from here, a count-in before each or none
        // (docs/transport-mixer-design.md §1 4).
        countInBtn.addEventListener('click', () => {
            practice.countIn = !practice.countIn;
            if (loopList && playingNow()) practiceChanged(practice.speed);
            else showPractice();
        });
        showPractice();

        document.getElementById('firstPageBtn').addEventListener('click', () => gotoPage(0));
        document.getElementById('lastPageBtn').addEventListener('click', () => gotoPage(pages.length - 1));
        // change = spinner click, Enter, or blur after typing. A blank or
        // non-number entry snaps back to the page actually in view.
        function gotoPageNumber(v) {
            const i = Math.max(0, Math.min(pages.length - 1, v - 1));
            gotoPage(i);
            showPageNumber(i + 1);
        }
        pageInput.addEventListener('change', () => {
            const v = parseInt(pageInput.value, 10);
            if (!Number.isFinite(v)) { showPageNumber(currentPageIndex() + 1); return; }
            gotoPageNumber(v);
        });
        // The spinner reads top-to-bottom like the score: down = next page. A
        // native step (spinner click, wheel) arrives as an input event with no
        // inputType — typing always has one — so undo its direction here.
        pageInput.addEventListener('input', (e) => {
            const v = parseInt(pageInput.value, 10);
            if (!Number.isFinite(v)) return;
            // Typed digits are the base a spinner click right after steps from.
            if (e.inputType) { shownPageNumber = v; return; }
            gotoPageNumber(shownPageNumber - (v - shownPageNumber));
        });
        pageInput.addEventListener('keydown', (e) => {
            if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                e.preventDefault();
                gotoPageNumber(shownPageNumber + (e.key === 'ArrowDown' ? 1 : -1));
            } else if (e.key === 'Enter') pageInput.blur();
            else if (e.key === 'Escape') {
                showPageNumber(currentPageIndex() + 1);
                pageInput.blur();
            }
        });
        pageInput.addEventListener('blur', updatePageInfo);
        mainContent.addEventListener('scroll', updatePageInfo);
        window.addEventListener('resize', () => {
            if (fitMode === 'width') fitWidth();
        });

        // The scan view's link (LilySharp-Omr proposal B4): the bar at the top of the
        // score goes out as the preview scrolls (the server's bar-box rects carry the
        // printed number), a bar the scan asks for is brought to the top, and the marks
        // the reader flagged are framed. A scroll asked for here is not reported back.
        // A mark frames what its box on the scan covers (owner's request 2026-10-17: the two
        // views frame the same thing; until then the whole system's bar was framed): its
        // lines ([from, to] of each part-bar it covers, as the scan's hover lights them —
        // lightPeerBars) frame every staff's bar whose first note is written on one of them;
        // a mark the scan does not know (no lines) frames the one staff's bar whose written
        // stretch (data-span) holds where it is written (at).
        // The frame itself is a shape of its own beside the staff's bar (path.flag-frame), the
        // bar's X range and two staff spaces taller above and below — the box the reader draws
        // round a part-bar on the scan (LilySharp-Omr Sidecar.AddPage: the staff's top line less
        // two spaces to its bottom line plus two; a staff is four spaces), so the two views
        // frame alike (owner's request 2026-10-17: the roomier frame is the easier to see).
        // An @todo memo is set over its staff's top line (under the bottom one), inside those two
        // spaces, so an edge of the frame can run through its words: the edge breaks there, a
        // quarter space clear of the memo's words and underline, and the frame keeps its size
        // (owner's report and decision 2026-10-27, tabi-13.lys: "枠線の y は狭めないでほしい").
        // The memo's leader may cross an edge; it runs down to the ring inside the frame anyway.
        let flaggedMarks = [];
        let barQuietUntil = 0, barReportTimer = null;
        function memoBoxes() {
            const boxes = [];
            for (const e of svgContainer.querySelectorAll('text[data-todo]:not(.music), line[data-todo]')) {
                if (e.tagName === 'line' && e.getAttribute('y1') !== e.getAttribute('y2')) continue; // a leader
                const b = e.getBoundingClientRect();
                if (b.width > 0 || b.height > 0) boxes.push(b);
            }
            return boxes;
        }
        function applyBarFlags() {
            for (const f of svgContainer.querySelectorAll('.flag-frame')) f.remove();
            // (no mark, no frame: a score nobody flagged does not have its bars read)
            if (flaggedMarks.length === 0) return;
            let memos = null;
            // What is left of [from, to] once the gaps are taken out.
            const pieces = (from, to, gaps) => {
                let left = [[from, to]];
                for (const [a, b] of gaps)
                    left = left.flatMap(([p, q]) => b <= p || a >= q ? [[p, q]] : [[p, a], [b, q]].filter(([u, v]) => v > u));
                return left;
            };
            for (const r of svgContainer.querySelectorAll('rect.bar-staff')) {
                const start = parseInt(r.getAttribute('data-start'), 10);
                const span = r.hasAttribute('data-span') ? r.getAttribute('data-span').split(' ').map(v => parseInt(v, 10)) : null;
                const flagged = flaggedMarks.some(m => m.lines && m.lines.length > 0
                    ? m.lines.some(l => start >= l[0] && start <= l[1])
                    : span !== null && m.at >= span[0] && m.at <= span[1]);
                if (!flagged) continue;
                const x = Number(r.getAttribute('x')), y = Number(r.getAttribute('y'));
                const w = Number(r.getAttribute('width')), h = Number(r.getAttribute('height'));
                const top = y - h / 2, bottom = y + 1.5 * h, right = x + w;
                // Each edge's gaps: where a memo's box, a quarter space wider all round, lies across it.
                const gaps = { top: [], bottom: [], left: [], right: [] };
                const ctm = r.getScreenCTM();
                if (ctm && (memos ??= memoBoxes()).length > 0) {
                    // The memos in the bar rect's own coordinates (the bar rects are a system's,
                    // the memos the page's).
                    const inv = ctm.inverse(), pad = h / 16;
                    for (const b of memos) {
                        const p1 = new DOMPoint(b.left, b.top).matrixTransform(inv);
                        const p2 = new DOMPoint(b.right, b.bottom).matrixTransform(inv);
                        const mLeft = Math.min(p1.x, p2.x) - pad, mRight = Math.max(p1.x, p2.x) + pad;
                        const mTop = Math.min(p1.y, p2.y) - pad, mBottom = Math.max(p1.y, p2.y) + pad;
                        if (mRight <= x || mLeft >= right || mBottom <= top || mTop >= bottom) continue;
                        if (mTop < top && top < mBottom) gaps.top.push([mLeft, mRight]);
                        if (mTop < bottom && bottom < mBottom) gaps.bottom.push([mLeft, mRight]);
                        if (mLeft < x && x < mRight) gaps.left.push([mTop, mBottom]);
                        if (mLeft < right && right < mRight) gaps.right.push([mTop, mBottom]);
                    }
                }
                const d = [
                    ...pieces(x, right, gaps.top).map(([a, b]) => 'M ' + a + ' ' + top + ' H ' + b),
                    ...pieces(top, bottom, gaps.right).map(([a, b]) => 'M ' + right + ' ' + a + ' V ' + b),
                    ...pieces(x, right, gaps.bottom).map(([a, b]) => 'M ' + a + ' ' + bottom + ' H ' + b),
                    ...pieces(top, bottom, gaps.left).map(([a, b]) => 'M ' + x + ' ' + a + ' V ' + b),
                ].join(' ');
                const f = document.createElementNS('http://www.w3.org/2000/svg', 'path');
                f.setAttribute('class', 'flag-frame');
                f.setAttribute('d', d);
                f.setAttribute('fill', 'none');
                f.setAttribute('pointer-events', 'none');
                r.parentNode.insertBefore(f, r.nextSibling);
            }
        }
        // The bar at the top edge of the view, and how much of it is cut off above that edge
        // (0: its top is at the edge; 0.5: half of it is out of sight; negative: it starts
        // below the edge). The scan view puts its own copy of the bar at the same cut, so the
        // two panes are cut alike at the top (owner's request 2026-10-17).
        function topVisibleBar() {
            const view = mainContent.getBoundingClientRect();
            let best = null, bestTop = Infinity, bestLeft = Infinity, cut = 0;
            for (const r of svgContainer.querySelectorAll('rect.bar-box')) {
                const b = r.getBoundingClientRect();
                if (b.bottom <= view.top + 4 || b.top >= view.bottom || b.height === 0) continue;
                const top = Math.round(Math.max(b.top, view.top));
                if (top < bestTop || (top === bestTop && b.left < bestLeft)) {
                    best = r; bestTop = top; bestLeft = b.left; cut = (view.top - b.top) / b.height;
                }
            }
            return best ? { bar: best, cut } : null;
        }
        // The scan view and this preview scroll together by the bar at the top of each, cut
        // alike (cut, see topVisibleBar) — except at the very top, which is its own place:
        // scrolled all the way up, a view says so (top: true) and the other goes all the way up
        // too, title and all, instead of putting the first bar at its top edge and hiding the
        // title (owner's request 2026-10-17).
        function showBar(bar, top, cut) {
            barQuietUntil = Date.now() + 400;
            if (top) {
                mainContent.scrollTop = 0;
                return;
            }
            const r = svgContainer.querySelector('rect.bar-box[data-bar="' + bar + '"]');
            if (!r) return;
            const b = r.getBoundingClientRect();
            mainContent.scrollTop += b.top - mainContent.getBoundingClientRect().top + (cut || 0) * b.height;
        }
        mainContent.addEventListener('scroll', () => {
            if (Date.now() < barQuietUntil) return;
            clearTimeout(barReportTimer);
            barReportTimer = setTimeout(() => {
                if (mainContent.scrollTop <= 0) {
                    vscode.postMessage({ type: 'visibleBar', top: true });
                    return;
                }
                const t = topVisibleBar();
                if (t) vscode.postMessage({ type: 'visibleBar', bar: Number(t.bar.dataset.bar), cut: t.cut });
            }, 150);
        });

        window.addEventListener('message', event => {
            const message = event.data;
            console.log('Webview received message:', message.type);

            switch (message.type) {
                case 'setTheme':
                    applyTheme(message.theme);
                    break;
                case 'hideTodos':
                    hideTodos(message.keys || []);
                    break;
                case 'showBar':
                    showBar(message.bar, message.top === true, typeof message.cut === 'number' ? message.cut : 0);
                    break;
                case 'markBars':
                    flaggedMarks = Array.isArray(message.marks) ? message.marks : [];
                    applyBarFlags();
                    break;
                case 'peerBars':
                    lightPeerBars(message.from, message.to);
                    break;
                case 'retarget':
                    // The follower turns to another file: put this one's zoom and scroll
                    // away, start the new one blank, and give it back its own view when
                    // its picture arrives (pendingView, in updateContent).
                    viewByFile.set(message.from, { scale, fitMode, scrollTop: mainContent.scrollTop });
                    pendingView = viewByFile.get(message.to) || { scale: 1, fitMode: 'width', scrollTop: 0 };
                    stopPlayback();
                    pendingAudition = null;
                    playbackNotes = null;
                    pendingStartBar = -1;
                    paused = null;
                    lastHighlightPos = -1;
                    lastHighlightTokenStart = -1;
                    lastHighlightRanges = null;
                    flaggedMarks = [];
                    hideErrorBanner();
                    svgContainer.classList.remove('stale');
                    svgContainer.innerHTML = '<div class="loading">Loading preview...</div>';
                    forgetShownPages();
                    invalidateSourcePositions();
                    collectPages();
                    break;
                case 'scanState':
                    docHasScan = !!message.hasScan;
                    break;
                case 'panelFolds':
                    // The practice panel's folds kept for the file shown (each render). Taken when the
                    // file changes — the page's own are newer than a render's copy of the same file's.
                    if (message.chart !== foldsChart) {
                        foldsChart = typeof message.chart === 'string' ? message.chart : null;
                        foldsKept = message.folds && typeof message.folds === 'object' ? message.folds : null;
                        if (!practicePanel.hidden) applyFolds();
                    }
                    break;
                case 'chordSpans':
                    chordSpanList = Array.isArray(message.spans) ? message.spans : [];
                    break;
                case 'todos':
                    todoMarks = Array.isArray(message.marks) ? message.marks : [];
                    break;
                case 'updateContent': {
                    updateRenderSelect(message.renders, message.selectedRender);
                    if (message.scoreKey) {
                        lastScoreKey = message.scoreKey;
                        if (!practicePanel.hidden && partsKey !== lastScoreKey) requestParts();
                    }
                    const hasPreview = !!svgContainer.querySelector('svg');
                    if (message.loading) {
                        // Don't replace an existing preview with a spinner — keep
                        // showing the last score while the server (re)starts.
                        if (!hasPreview) {
                            svgContainer.innerHTML = '<div class="loading">Waiting for language server...</div>';
                            forgetShownPages();
                            invalidateSourcePositions();
                        }
                    } else if (message.svg || message.pages) {
                        // A score arrived — it is CURRENT, so never dimmed. It may
                        // still carry an error banner: a file with parse errors
                        // renders best-effort (the bad parts are simply dropped).
                        // It comes as one string (an older server) or as pages — a
                        // delta against the picture on screen, or every page.
                        if (message.error) {
                            showErrorBanner(message.error);
                        } else {
                            hideErrorBanner();
                        }
                        // (the text read again: whether it can be played, as the banner says)
                        markUnreadable(!!message.error);
                        svgContainer.classList.remove('stale');
                        const applyStart = performance.now();
                        const applied = message.pages ? applyPages(message.pages) : applySvg(message.svg);
                        const applyEnd = performance.now();
                        // What follows the swap, lap by lap (each lap's ms go into the account
                        // below): which of them an update pays for is read, not guessed. The first
                        // lap, 'pages', is the browser's layout of the new picture — collectPages
                        // asks where the view is scrolled to, and that needs it (session 1302).
                        let lapAt = applyEnd;
                        const laps = [];
                        const lap = name => {
                            const now = performance.now();
                            laps.push(name + ' ' + (now - lapAt).toFixed(1));
                            lapAt = now;
                        };
                        if (applied === null) {
                            // The delta could not be applied to what is shown; the
                            // whole picture has been asked for and will replace it.
                            break;
                        }
                        invalidateSourcePositions();
                        // The score changed: the cached note list is stale, so the
                        // next audition / Play refetches fresh events. A Play still sounding
                        // is the old text's: a voice of the new one is cut into it only where
                        // it still fits (joinVoice, voiceFitsPlay).
                        playbackNotes = null;
                        playEdited = true;
                        // Going round a loop: the next round sounds the new text (owner's request
                        // 2026-11-17 — a bar looped while it is written by ear; refreshLoop).
                        if (loopOn && loopList && lastSpan !== undefined && playClock && playingNow()) {
                            pendingLoopRefresh = true;
                            requestPlay();
                        }
                        collectPages();
                        lap('pages');
                        applyBarFlags();
                        lightCaretBar();
                        lap('bars');
                        drawAbFlags();      // (the A-B flags are the page's: drawn into each new picture)
                        showBarInfo();          // (and the header's bar: the picture's bars may be others)
                        refreshRanges();
                        vscode.postMessage({ type: 'requestLength' });
                        lap('A-B');
                        // Keep the fit across re-renders (this is also what fits
                        // the FIRST render of a freshly opened preview); otherwise
                        // re-apply the current manual zoom to the fresh SVG.
                        if (pendingView) {
                            // The first picture after a turn: the view this file had.
                            scale = pendingView.scale;
                            fitMode = pendingView.fitMode;
                        }
                        if (fitMode === 'width') fitWidth();
                        else updateZoom();
                        if (pendingView) {
                            mainContent.scrollTop = pendingView.scrollTop;
                            pendingView = null;
                            updatePageInfo();
                        }
                        lap('fit');
                        if (lastHighlightRanges) {
                            highlightRange(lastHighlightRanges);
                        } else if (lastHighlightPos >= 0) {
                            highlightNearestElement(lastHighlightPos, lastHighlightTokenStart);
                        }
                        lap('highlight');
                        lightSelectedBars();
                        lap('selection');
                        const settled = performance.now();
                        const chars = message.pages ? pagesChars(message.pages) : message.svg.length;
                        vscode.postMessage({ type: 'webviewPerf', text: 'update: ' + applied
                            + ' in ' + (applyEnd - applyStart).toFixed(1) + ' ms, then pages/fit/highlight '
                            + (settled - applyEnd).toFixed(1) + ' ms (' + chars + ' chars): ' + laps.join(', '),
                            // (what this update took of the page, for the host's wait before the
                            // next refresh — PreviewSession.refreshDelay; a whole new picture is
                            // not what a keystroke costs)
                            took: settled - applyStart, whole: applied.indexOf('whole document') === 0 });
                    } else if (message.error) {
                        // Nothing could render at all. Keep the last good preview,
                        // DIM it, and show the error in a banner. Only when nothing
                        // has rendered yet do we show the error in place (there is
                        // nothing to preserve).
                        markUnreadable(true);
                        if (hasPreview) {
                            svgContainer.classList.add('stale');
                            showErrorBanner(message.error);
                        } else {
                            hideErrorBanner();
                            svgContainer.innerHTML = '<div class="error">' + escapeHtml(message.error) + '</div>';
                            forgetShownPages();
                            invalidateSourcePositions();
                        }
                    }
                    break;
                }
                case 'playAtCursor': {
                    // The synth needs a current note list. If it's stale (an edit
                    // cleared it) or absent, fetch it and play once it arrives — but
                    // only one fetch in flight, so a held key's repeats don't pile up.
                    // fresh (an @or turned by Ctrl+Shift+PageUp): the text changed under it, so the
                    // list in hand — or one already asked for — is the old text's: ask again.
                    if (playbackNotes && !message.fresh) {
                        runAudition(message);
                    } else {
                        const inFlight = !!pendingAudition;
                        pendingAudition = message;
                        if (!inFlight || message.fresh) { requestPlay(); auditionSeq = playSeq; }
                    }
                    break;
                }
                case 'playPitches':
                    playPitches(message.pitches, message.timbre, message.program, message.durationMs, message.strumMs || 0, !!message.apart,
                        message.velocity, message.part);
                    break;
                // Another text's notes — an AI candidate under review (owner's request
                // 2026-10-25) — played as the Play button plays this score's, from the top. They
                // are not this page's: the cached note list is left alone, and their source
                // positions (R) are the candidate's, followed for its panel — nothing here lights up.
                case 'followTick':
                    if (followTick) followTick();
                    break;
                case 'voiceLinesChanged':
                    if (!practicePanel.hidden) requestParts();
                    break;
                case 'recording':
                    setRecording(message);
                    showRecording();
                    renderRecParts();
                    break;
                case 'recordingParts':
                    void loadRecordingParts(message);
                    break;
                case 'renderRecording':
                    void renderRecordingFor(message);
                    break;
                case 'chartTranspose': {
                    const n = Number(message.semitones) || 0;
                    if (n !== chartShift) { chartShift = n; recApply(); }
                    break;
                }
                case 'partsData':
                    renderParts(message);
                    // Its width is known once filled: keep it inside the window.
                    if (!practicePanel.hidden && practicePanel.dataset.moved) clampPanel(practicePanel);
                    else if (!practicePanel.hidden) {
                        const b = practiceBtn.getBoundingClientRect();
                        practicePanel.style.left = Math.max(6, Math.min(b.left, window.innerWidth - practicePanel.offsetWidth - 6)) + 'px';
                    }
                    break;
                case 'lengthData': {
                    const s = message.seconds;
                    const len = document.getElementById('lengthInfo');
                    len.textContent = typeof s === 'number' && s > 0 ? clockText(s) : '';
                    askStoppedPosition();   // (a new render: the caret's time may have moved)
                    break;
                }
                case 'soundingParts': {
                    // The parts that sound now, read off the length's export (no export of its own):
                    // the menu, open, asks for its list again when one is not on it — a part given
                    // music while the menu stayed open (owner's report 2026-11-20) — or when one on
                    // it sounds no more (a part removed, or its music gone: owner's request the same
                    // day). The score's own parts only: a part added to the file and not to its
                    // score (an extra) shows when the menu is opened again — listing the extras
                    // here would cost the server a second export at every rest, for an edit that
                    // comes rarely (owner's decision 2026-11-20). Only such a change asks, and once
                    // per set of parts (a list that still differs asks no more).
                    const sounding = Array.isArray(message.parts) ? message.parts : [];
                    if (practicePanel.hidden || !lastPartsMessage) break;
                    const listedParts = lastPartsMessage.parts || [];
                    const listed = new Set(listedParts), heard = new Set(sounding);
                    const askKey = lastScoreKey + '\\n' + sounding.join('\\n');
                    const changed = sounding.some(p => !listed.has(p)) || listedParts.some(p => !heard.has(p));
                    if (changed && partsAskedFor !== askKey) {
                        partsAskedFor = askKey;
                        requestParts();
                    }
                    break;
                }
                case 'positionTime':
                    if (message.seq === positionSeq) {
                        stoppedSeconds = typeof message.seconds === 'number' ? message.seconds : null;
                        showPosition();
                    }
                    break;
                case 'rangesData':
                    rangeParts = message.parts || null;
                    rangeError = message.error || null;
                    renderRanges();
                    break;
                case 'playNotes':
                    loopList = null;
                    voicePlan = null;       // another text's notes: no voice of this score's
                    // Pressed on another panel, so no click here lets the sound through: said at
                    // every silent press, not once.
                    audioBlockedTold = false;
                    startPlayback(message.notes, 0);
                    break;
                case 'setSoundFont':
                    setSoundFont(message.uri);
                    break;
                case 'setMidiOutput':
                    setMidiOutput(message.name);
                    break;
                case 'voice':
                    setVoice(message);
                    break;
                case 'playVoiceSample':
                    playVoiceSample(message);
                    break;
                case 'voiceState':
                    setVoiceState(message.part, message.state, message.detail, message.estimateMs, message.singing, message.key);
                    break;
                case 'voiceProgress':
                    setVoiceTold(message);
                    break;
                case 'voiceParts':
                    keepVoices(message.parts);
                    break;
                case 'clearVoices':
                    clearVoices(!!message.pending);
                    break;
                case 'voiceSettled':
                    settleVoices();
                    break;
                case 'midiStarted':
                    midiStarted(message);
                    break;
                // Context-menu actions.
                case 'ctxStop':
                    stopPlayback();
                    break;
                case 'ctxFitWidth':
                    fitWidth();
                    break;
                // The A-B repeat's right-click: the bars of the section right-clicked as A-B, which
                // turns on. (A Here and B Here are gone — owner's decision 2026-11-19: a click on a
                // bar is A, a Shift+click B.)
                case 'ctxAbSection': {
                    const sec = ctxAbSec;
                    if (!sec || sec[1] < sec[0]) break;
                    abOn = true;
                    setAb(sec[0], sec[1]);
                    break;
                }
                case 'ctxResetZoom':
                    fitMode = null; // back to manual zoom, at 1:1
                    scale = 1;
                    updateZoom();
                    break;
                case 'ctxSelectBar':
                    // The bar the menu was opened on becomes the selection.
                    if (ctxBarRange) aiSetSelection(ctxBarRange[0], ctxBarRange[1]);
                    break;
                case 'ctxClearSelection':
                    aiClearSelection();
                    clearHighlights();
                    break;
                case 'playbackData':
                    if (typeof message.seq === 'number' && message.seq !== playSeq) {
                        // A later request replaced it: not played — an audition's own answer aside.
                        if (pendingAudition && message.seq === auditionSeq && !message.error && message.notes) {
                            takeAuditionNotes(message);
                            const a = pendingAudition; pendingAudition = null;
                            runAudition(a);
                        } else if (message.seq === auditionSeq) pendingAudition = null;
                        break;
                    }
                    // Whether the text as it is now can be played: the last answer says (the Play
                    // button's edge, while what sounds is the last version that could — markUnreadable).
                    markUnreadable(!!(message.error || !message.notes));
                    if (pendingLoopRefresh && !message.error && message.notes) {
                        pendingLoopRefresh = false;
                        refreshLoop(message);
                        break;
                    }
                    // A loop's next round asked for text that does not play yet — a part being
                    // typed, "bass {" with its brace not closed: the loop goes on as it was and the
                    // next edit asks again. MEASURED before (owner's report 2026-11-17, Lab
                    // sessions/p1290/e2e/cdp-loopadd2.mjs): the answer's error took the page out of
                    // playing (setPlayUi(false)) while the old rounds still sounded, so no later edit
                    // reached the loop — the part, written out, was never added — and Stop was greyed.
                    if (pendingLoopRefresh) {
                        pendingLoopRefresh = false;
                        if (playingNow()) break;
                    }
                    pendingLoopRefresh = false;
                    // Text that cannot be read (a note being typed: "d,8\" before its 3) plays as it
                    // last could be read — a Play, an audition alike — rather than refusing (owner's
                    // decision 2026-11-17: what can be read last goes on; stopping at every unreadable
                    // keystroke and starting again would chop the music being written by ear).
                    if ((message.error || !message.notes) && rawNotes) {
                        message.notes = rawNotes; message.parts = rawParts; message.beats = rawBeats; message.barEnd = rawBarEnd;
                        message.extra = rawExtra; message.sung = rawSung; message.played = rawPlayed;
                        message.error = null;
                        if (!pendingAudition) flashInfo('The text cannot be read yet — playing the last version that could be', 3000);
                    }
                    if (message.error || !message.notes) {
                        setPlayUi(false);
                        pendingAudition = null;
                        if (message.error) {
                            zoomInfo.textContent = 'Play: ' + message.error;
                            zoomInfo.classList.add('visible');
                            setTimeout(() => zoomInfo.classList.remove('visible'), 2500);
                        }
                    } else if (pendingAudition && (typeof message.seq !== 'number' || message.seq === auditionSeq)) {
                        // These notes were fetched for an audition, not the Play button.
                        takeAuditionNotes(message);
                        const a = pendingAudition; pendingAudition = null;
                        runAudition(a);
                    } else if ((pendingSpan || loopOn) && window.LysSynth && window.LysSynth.playSpan) {
                        // The selected bars, where they first play — and, looping, the stretch
                        // (or the whole score) as written once, round and round.
                        rawNotes = message.notes; rawParts = message.parts || null; rawBeats = message.beats || null; rawBarEnd = message.barEnd || null; rawExtra = message.extra || null; rawSung = message.sung || null; rawPlayed = message.played || null;
                        playbackNotes = practiced(rawNotes);
                        const span = pendingSpan;
                        pendingSpan = null;
                        lastSpan = span;
                        // The whole score round and round goes as it plays — its repeats and jumps
                        // (owner's decision 2026-11-17; it went as printed once). A-B goes as printed.
                        lastOnce = false;
                        const list = spanOf(playbackNotes, span, false);
                        loopList = loopOn ? list : null;
                        const startBar = pendingStartBar;
                        pendingStartBar = -1;
                        if (loopOn) {
                            // (looped lays the count-in before every round out itself, the first one too)
                            const rounds = looped(list);
                            voicePlan = loopVoicePlan();
                            if (recordingAlone()) break;
                            // The whole score round and round from the A flag (docs/transport-mixer-design.md
                            // §1 3; owner's report 2026-11-17: stopped, A moved by a click, Play went from the
                            // top with Repeat on): the first round from A's bar — its own count-in before
                            // it — and every round after it from the top. The list keeps its rounds' times.
                            if (!span && startBar > 0) {
                                const speed = playedSpeed();
                                const beats = (playedBeats() || []).map(b => ({ T: b.T / speed - spanStart, A: b.A }));
                                const at = barStartIn(list, beats, startBar);
                                if (at > 1e-6 && at < loopRound - 1e-6) {
                                    const entry = loopGap + at;
                                    const c = loopGap > 0 ? counted([], at, spanStart) : null;
                                    voicePlan.entry = entry;
                                    if (c && c.lead > 0) {
                                        const from = entry - c.lead;
                                        const music = window.LysSynth.fromOffset(rounds, entry);
                                        const notes = c.notes.map(n => Object.assign({}, n, { T: n.T + from })).concat(music).sort((x, y) => x.T - y.T);
                                        startPlayback(notes, from, false, false, loopGap, spanStart);
                                    } else {
                                        startPlayback(rounds, entry, false, false, loopGap, spanStart);
                                    }
                                    loopEntry = entry;
                                    break;
                                }
                            }
                            startPlayback(rounds, 0, false, false, loopGap, spanStart);
                            break;
                        }
                        voicePlan = { pieces: spanPieces, rounds: 1 };
                        if (recordingAlone()) break;
                        const c = counted(list, 0, spanStart);
                        if (c) { startPlayback(c.notes, 0, false, false, c.lead, spanStart); loopEntry = c.lead; }
                        else startPlayback(list, 0, false, false, 0, spanStart);
                    } else {
                        // The practice menu's tempo, parts and metronome, from the notes as written.
                        rawNotes = message.notes; rawParts = message.parts || null; rawBeats = message.beats || null; rawBarEnd = message.barEnd || null; rawExtra = message.extra || null; rawSung = message.sung || null; rawPlayed = message.played || null;
                        playbackNotes = practiced(rawNotes);
                        pendingSpan = null;
                        lastSpan = undefined;
                        loopList = null;
                        voicePlan = { pieces: null, rounds: 1 };
                        let startAt = 0;
                        if (pendingStartBar >= 0) {
                            // From the A flag's bar where it first plays (§1 3), on this list's time.
                            const speed = playedSpeed();
                            startAt = barStartIn(playbackNotes, (playedBeats() || []).map(b => ({ T: b.T / speed, A: b.A })), pendingStartBar);
                        }
                        pendingStartBar = -1;
                        if (recordingAlone()) break;
                        const c = counted(playbackNotes, startAt, 0);
                        if (c) { startPlayback(c.notes, 0, false, false, c.lead - startAt, 0); loopEntry = c.lead; }
                        else startPlayback(playbackNotes, startAt, false, false, 0, 0);
                    }
                    break;
                case 'highlightPosition':
                    editorCaret = { pos: message.position, tokenStart: message.tokenStart, caret: message.caret };
                    lightCaretBar(message.caret, message.tokenStart, message.lineEnd);
                    lastHighlightPos = message.position;
                    lastHighlightTokenStart = message.tokenStart;
                    lastHighlightRanges = null;
                    scheduleHighlight(() => highlightNearestElement(message.position, message.tokenStart));
                    break;
                case 'highlightRange':
                    editorCaret = { ranges: message.ranges, caret: message.caret };
                    lightCaretBar(message.caret, -1);
                    lastHighlightRanges = message.ranges;
                    lastHighlightPos = -1;
                    scheduleHighlight(() => highlightRange(message.ranges));
                    break;
            }
        });

        installDragScroll(mainContent, document);

        // Resizes the sheet keeping the sheet point under (x, y) — the pointer — where it is: the
        // point's share of the sheet's box before is where it stands after, and the scroller moves
        // by the difference. (Owner's report 2026-11-11: on a lower page of a long score Ctrl+wheel
        // slid the view back toward the pages before — the sheet grew under a scroll offset that
        // stayed, so the same offset was an earlier place.)
        function zoomKeeping(scroller, sheetRect, resize, x, y) {
            const b = sheetRect();
            const fx = b.width > 0 ? (x - b.left) / b.width : 0;
            const fy = b.height > 0 ? (y - b.top) / b.height : 0;
            resize();
            const a = sheetRect();
            scroller.scrollLeft += a.left + fx * a.width - x;
            scroller.scrollTop += a.top + fy * a.height - y;
        }

        document.addEventListener('wheel', (e) => {
            if (e.ctrlKey) {
                e.preventDefault();
                const delta = e.deltaY > 0 ? -scaleStep : scaleStep;
                const next = Math.min(maxScale, Math.max(minScale, scale + delta));
                fitMode = null; // manual zoom overrides a fit mode
                const sheet = svgContainer.querySelector('svg');
                const resize = () => { scale = next; updateZoom(); };
                if (sheet && next !== scale) zoomKeeping(mainContent, () => sheet.getBoundingClientRect(), resize, e.clientX, e.clientY);
                else resize();
            }
        }, { passive: false });

        // During playback a click on a played note SEEKS there instead of jumping the editor
        // (listening mode). The clicked printed COPY — the first of the data-pos's instances
        // (in document order) holding an element isCopy accepts — maps to the onset whose
        // server-side ordinal (O) matches. False when nothing is playing or no onset is the
        // position's. Clears the active highlight FIRST: it raises its element, corrupting
        // the document order.
        function seekToCopy(pos, isCopy) {
            if (playheadTimer === null || !playbackNotes) return false;
            clearHighlights();
            const copies = Array.from(document.querySelectorAll('[data-pos="' + pos + '"]'));
            const instances = clusterInstances(copies);
            let occ = 0;
            for (let k = 0; k < instances.length; k++) {
                if (instances[k].some(isCopy)) { occ = k; break; }
            }
            let best = null;
            for (const n of playbackNotes) {
                if (n.S !== pos) continue;
                if ((n.O || 0) === occ && (best === null || n.T < best.T)) best = n;
            }
            if (best === null) {
                for (const n of playbackNotes) {
                    if (n.S === pos && (best === null || n.T < best.T)) best = n;
                }
            }
            if (best === null) return false;
            seekPlay(best.T);
            return true;
        }
        // The list from the score's time t, as it is being played: round and round with Repeat on
        // (the whole score: its rounds laid out again and entered at t), else once from t. Until
        // 2026-11-19 a seek handed the instruments the score ONCE whatever Repeat said, while the
        // voices kept the loop's plan and the end stayed the last round's: at the score's end the
        // instruments stopped and the voice went on into the next round alone (owner's report,
        // Spring-Breeze-Bicycle.lys, seen once; measured in a real VS Code: after a click on a bar
        // the list ended at 104 s under a plan of 6 rounds).
        function seekPlay(t) {
            if (loopOn && loopList && lastSpan !== undefined && loopRound > 0) {
                const rounds = looped(loopList);
                voicePlan = loopVoicePlan();
                let at = null, before = 0;
                for (const [a, b] of spanPieces || [[spanStart, spanStart + loopRound]]) {
                    if (t >= a - 1e-6 && t < b - 1e-6) { at = before + (t - a); break; }
                    before += b - a;
                }
                startPlayback(rounds, at === null ? 0 : loopOffset(at, null), false, false, loopGap, spanStart);
                return;
            }
            startPlayback(playbackNotes, t, false, false, 0, 0);   // (the recording seeks too)
        }
        // A clicked bar whose first note is not in the list: one tied over from the bar before
        // ("a8 d'8~ | d'2. r4 |" — the list has the one onset, in the bar before) has no entry of
        // its own, so seekToCopy finds nothing and the playing went on where it was (owner's
        // report 2026-11-19, Moon-on-the-River.lys: bar 3 playing, bar 14 clicked; measured in
        // a real VS Code, the A flag on bar 14 and the list still at 7 s). The bar's own start
        // instead, where it first plays — Play's rule from the A flag (barStartIn).
        function seekToBar(m) {
            if (playheadTimer === null || !playbackNotes || m < 0) return false;
            clearHighlights();
            const speed = playedSpeed();
            const at = barStartIn(playbackNotes, (playedBeats() || []).map(b => ({ T: b.T / speed, A: b.A })), m);
            seekPlay(at);
            return true;
        }

        installBarHover();

        // What a click means (owner's request 2026-11-03: the upper half of a chord's lower note went
        // to the note above it). A head's click box answers beyond its edges and the next head of a
        // chord lies over it: of the boxes there, the one whose middle is nearest. Anything else drawn
        // gives way only to a box it truly lies over, a mark's text answering for its whole em box.
        // Anything undrawn is what was pressed.
        function clickTargetOf(pressed, stackAtPointer, x, y) {
            const isBox = (el) => !!el && !!el.classList && el.classList.contains('nh-hit');
            const drawn = !!pressed && !!pressed.hasAttribute && pressed.hasAttribute('data-pos');
            if (!isBox(pressed) && !drawn) return pressed;
            return giveWay(pressed, isBox(pressed), stackAtPointer().filter(isBox), x, y);
        }
        function giveWay(pressed, onABox, boxes, x, y) {
            if (onABox) return boxes.length === 0 ? pressed : nearestTo(boxes, x, y);
            const inside = boxes.filter((el) => {
                const r = el.getBoundingClientRect();
                return x >= r.left && x <= r.left + r.width && y >= r.top && y <= r.top + r.height;
            });
            return inside.length === 0 ? pressed : nearestTo(inside, x, y);
        }
        // Of these elements, the one whose middle is nearest the point.
        function nearestTo(els, x, y) {
            const off = (el) => {
                const r = el.getBoundingClientRect();
                return Math.hypot(r.left + r.width / 2 - x, r.top + r.height / 2 - y);
            };
            return els.reduce((a, b) => (off(b) < off(a) ? b : a));
        }
        // At the press: a click's coordinates are whole pixels and a pointer's are not, which on a
        // head three pixels tall can be the next note.
        let pressedAt = null;
        window.addEventListener('pointerdown', (e) => { pressedAt = { x: e.clientX, y: e.clientY }; }, true);
        const noteUnder = (e) => {
            const x = pressedAt ? pressedAt.x : e.clientX, y = pressedAt ? pressedAt.y : e.clientY;
            return clickTargetOf(e.target, () => document.elementsFromPoint(x, y), x, y);
        };

        // Shift+click on a bar places the B flag (owner's decision 2026-11-17, docs/transport-mixer-design.md
        // §7): the score no longer selects by Shift+click or Shift+drag — the editor selects. A shifted
        // press over the sheet selects no text.
        svgContainer.addEventListener('pointerdown', (e) => {
            if (e.pointerType !== 'mouse' || e.button !== 0 || !e.shiftKey) return;
            const t = noteUnder(e);
            if ((t && t.hasAttribute && t.hasAttribute('data-pos')) || barStaffAt(e.clientX, e.clientY)) e.preventDefault();
        });

        document.addEventListener('click', (e) => {
            const target = noteUnder(e);
            if (target && target.hasAttribute && target.hasAttribute('data-pos')) {
                const pos = parseInt(target.getAttribute('data-pos'), 10);
                // Shift+click: the B flag to its bar (§7) — it never seeks or jumps.
                if (e.shiftKey) {
                    const b = barStaffAt(e.clientX, e.clientY); const m = b ? measureOfStaffBox(b) : -1;
                    if (m >= 0) setB(m, true);
                    return;
                }
                // Plain click: any prior score selection is dropped. The note's bar takes
                // the A flag, where Play starts (the playing goes on from the note, seeking below).
                { const b = barStaffAt(e.clientX, e.clientY); const m = b ? measureOfStaffBox(b) : -1; if (m >= 0) setA(m); }
                aiRangeLo = -1; aiRangeHi = -1;
                lightSelectedBars();
                // During playback a click on a played note SEEKS there instead
                // of jumping the editor (listening mode). Non-note grobs
                // (barlines, marks) fall through to the normal click.
                if (!abOn && seekToCopy(pos, (copy) => copy === target)) return;     // (A-B on: laid out by setA)
                vscode.postMessage({ type: 'jumpToPosition', position: pos });
            } else if (e.shiftKey && !e.ctrlKey && !e.metaKey && !e.altKey
                && target && svgContainer.contains(target) && barStaffAt(e.clientX, e.clientY)) {
                // Shift+click on a bar: the B flag on its RIGHT bar line, the bar in the range as a
                // click puts its bar in (owner's decision 2026-11-17, revising the left line). (The
                // margin right of a line's last bar line took B too, and a click there A — gone the same
                // day, owner's decision: the line's last bar, Shift+clicked, puts B there.)
                const bar = barStaffAt(e.clientX, e.clientY);
                const m = measureOfStaffBox(bar);
                if (m >= 0) setB(m, true);
            } else if (!e.shiftKey && !e.ctrlKey && !e.metaKey && !e.altKey
                && target && svgContainer.contains(target) && barStaffAt(e.clientX, e.clientY)) {
                // A bar: its first note's place in the editor — or, while playing, the
                // bar's own copy of that note (a section played twice prints it twice).
                const bar = barStaffAt(e.clientX, e.clientY);
                const pos = parseInt(bar.getAttribute('data-start'), 10);
                // The bar takes the A flag: Play starts there (docs/transport-mixer-design.md §1 3).
                // (playing: laid out again as the flags now say, as a dragged flag — owner's report 2026-11-17)
                const m = measureOfStaffBox(bar);
                if (m >= 0) setA(m);
                lastHighlightPos = -1;
                aiClearSelection();
                const box = bar.getBoundingClientRect();
                const inBar = (copy) => {
                    const b = copy.getBoundingClientRect();
                    const cx = (b.left + b.right) / 2, cy = (b.top + b.bottom) / 2;
                    return cx >= box.left && cx <= box.right
                        && cy >= box.top - box.height && cy <= box.bottom + box.height;
                };
                // (A-B on: the bars as the flags now say are already laid out above — a seek would go
                // through the whole score, out past B: owner's report 2026-11-17)
                // (a bar that opens on a note tied over has no note of its own to seek to: its start)
                if (!abOn && (seekToCopy(pos, inBar) || seekToBar(m))) return;
                clearHighlights();
                vscode.postMessage({ type: 'jumpToPosition', position: pos });
            } else if (target && mainContent.contains(target)) {
                // Clicked empty space / a non-clickable grob: just drop the
                // highlight. Don't move the editor cursor (no message sent), and
                // forget the position so a re-render doesn't bring it back.
                // Only on the sheet: a header button (▶ plays the selection — owner's report
                // 2026-10-25) or a panel over the score keeps the selection.
                lastHighlightPos = -1;
                aiClearSelection();
                clearHighlights();
            }
        });

        // Right-click: publish what the menu needs to know about THIS click —
        // whether it landed on a note (data-pos, same test the click handler
        // uses) and whether playback is running. VS Code reads
        // data-vscode-context off the element chain in its own contextmenu
        // listener, so this one runs in the CAPTURE phase to get there first.
        let ctxNotePos = -1;
        let ctxBarRange = null;   // the bar right-clicked, as its first and last note (ctxSelectBar)
        let docHasScan = false;   // the file has an OMR side file (scanState, with each render)
        // The chords of the text, [its "<", its ">"] in order (chordSpans, with each render):
        // Select Chord on a member right-clicked puts the caret past the ">" (owner's request
        // 2026-10-27: where a chord's octave marks and decorations are written).
        let chordSpanList = [];
        // The @todo marks of the text, {start, hostStart} (todos, with each render): Resolve This
        // TODO on a bar that holds one, Resolve All TODOs off the staves (owner's request
        // 2026-10-31). A bar's marks are the ones whose host is written in its span (data-span).
        let todoMarks = [];
        function chordEndAt(pos) {
            let lo = 0, hi = chordSpanList.length - 1;
            while (lo <= hi) {
                const mid = (lo + hi) >> 1, s = chordSpanList[mid];
                if (pos <= s[0]) hi = mid - 1;
                else if (pos >= s[1]) lo = mid + 1;
                else return s[1] + 1;
            }
            return -1;
        }
        // The A-B repeat's right-click (docs/ab-repeat-design.md §1): the labelled section the bar
        // under the pointer (or under the heading over it) is in, as its first and last bar
        // (data-sec opens one; it runs to the bar before the next); null when it is in none.
        let ctxAbSec = null;
        function abSectionAt(x, y) {
            const bars = abBars();
            const last = bars.length - 1;
            // The bar whose box holds the point, else the nearest one in its column: a heading
            // over a system is the bar's below it, so the distance up to a box counts for less.
            let hit = null, hitD = Infinity;
            for (const b of bars) {
                if (!b) continue;
                const c = b.rect.getBoundingClientRect();
                if (x < c.left || x > c.right) continue;
                const v = y < c.top ? (c.top - y) * 0.5 : y > c.bottom ? (y - c.bottom) * 2 : 0;
                if (v < hitD && v <= 24) { hitD = v; hit = { b, c }; }
            }
            if (!hit) return null;
            const m = hit.b.m;
            for (let s = m; s >= 0; s--) {
                if (!bars[s] || !bars[s].rect.hasAttribute('data-sec')) continue;
                let end = m;
                while (end < last && !(bars[end + 1] && bars[end + 1].rect.hasAttribute('data-sec'))) end++;
                return [s, end];
            }
            return null;
        }
        document.addEventListener('contextmenu', (e) => {
            const t = noteUnder(e);
            ctxNotePos = (t && t.hasAttribute && t.hasAttribute('data-pos'))
                ? parseInt(t.getAttribute('data-pos'), 10)
                : -1;
            // The bar under the pointer (owner's request 2026-10-24: Fix OMR Marks with AI and
            // Edit Bars Beside the Scan on the bar right-clicked): the staff box of a bar that
            // holds the point, where its text starts — else the note's own offset. The menu's
            // command gets it as its argument (lysAt).
            let ctxBarAt = -1;
            let ctxTodoStarts = [];
            for (const r of document.querySelectorAll('rect.bar-staff')) {
                const b = r.getBoundingClientRect();
                if (e.clientX >= b.left && e.clientX <= b.right && e.clientY >= b.top && e.clientY <= b.bottom) {
                    const s = parseInt(r.getAttribute('data-start'), 10);
                    if (!isNaN(s)) { ctxBarAt = s; }
                    const span = (r.getAttribute('data-span') || '').split(' ').map(Number);
                    // The bar's span runs from its first ITEM — past a group's "<<" — so a mark
                    // is the bar's by where the MARK is written (owner's report 2026-10-31:
                    // "<< c e g >>@todo" offered nothing), else by its host.
                    if (span.length === 2 && span[1] > span[0]) {
                        ctxTodoStarts = todoMarks.filter(m => (m.start >= span[0] && m.start < span[1])
                                || (m.hostStart >= span[0] && m.hostStart < span[1]))
                            .map(m => m.start).sort((a, b) => a - b);
                    }
                    if (!isNaN(s)) { break; }
                }
            }
            // What the menu's Change … with AI works on, and says it does (owner's requests
            // 2026-10-24 and 2026-10-25: Ctrl+I's door on the score, everywhere on it): the score
            // selection when there is one, else the note under the pointer, else the bar under
            // it, else — off the staves — the whole score, as Ctrl+I on an empty line does.
            ctxBarRange = null;
            // Where Split Section Here cuts (owner's decision 2026-11-17, the menus' reorganisation): the
            // bar line nearer the pointer — before the bar (its first note: data-start) or after it (the
            // end of its text, then on to its bar line: snap).
            let ctxCutAt = -1, ctxCutSnap = false;
            for (const r of document.querySelectorAll('rect.bar-staff')) {
                const b = r.getBoundingClientRect();
                if (e.clientX >= b.left && e.clientX <= b.right && e.clientY >= b.top && e.clientY <= b.bottom) {
                    ctxBarRange = barNoteRange(r);
                    const span = (r.getAttribute('data-span') || '').split(' ').map(Number);
                    const start = parseInt(r.getAttribute('data-start'), 10);
                    if (e.clientX - b.left > b.right - e.clientX) {
                        if (span.length === 2 && !isNaN(span[1])) { ctxCutAt = span[1]; ctxCutSnap = true; }
                    } else if (!isNaN(start)) {
                        ctxCutAt = start;
                    }
                    if (ctxBarRange) break;
                }
            }
            let ctxAiKind = 'score', ctxAiFrom = -1, ctxAiTo = -1, ctxAiSelected = '';
            if (aiRangeLo >= 0) {
                ctxAiKind = 'selection'; ctxAiFrom = aiRangeLo; ctxAiTo = aiRangeHi;
                ctxAiSelected = selectedLabel();
            } else if (ctxNotePos >= 0) {
                ctxAiKind = 'note'; ctxAiFrom = ctxAiTo = ctxNotePos;
                ctxAiSelected = '1 note selected';
            } else if (ctxBarRange) {
                ctxAiKind = 'bar'; ctxAiFrom = ctxBarRange[0]; ctxAiTo = ctxBarRange[1];
                ctxAiSelected = '1 bar selected';
            }
            if (ctxBarAt < 0) ctxBarAt = ctxNotePos;
            const ctxChordEnd = ctxNotePos >= 0 ? chordEndAt(ctxNotePos) : -1;
            ctxAbSec = abSectionAt(e.clientX, e.clientY);
            document.body.dataset.vscodeContext = JSON.stringify({
                preventDefaultContextMenuItems: true,
                // The OMR doors only for a file with a scan (owner, 2026-10-24); the scan
                // state comes with each render (scanState).
                lysOnBar: docHasScan && ctxBarAt >= 0,
                lysAt: ctxBarAt,
                lysAiKind: ctxAiKind,
                lysAiFrom: ctxAiFrom,
                lysAiTo: ctxAiTo,
                lysAiSelected: ctxAiSelected,
                lysOnStaff: ctxBarRange !== null,
                lysHasSel: aiRangeLo >= 0,
                lysOnChord: ctxChordEnd >= 0,
                lysChordEnd: ctxChordEnd,
                lysBarTodo: ctxTodoStarts.length > 0,
                lysTodoStarts: ctxTodoStarts.join(','),
                lysHasTodos: todoMarks.length > 0,
                lysAbSec: ctxAbSec !== null,
                lysCanCut: ctxCutAt >= 0,
                lysCutAt: ctxCutAt,
                lysCutSnap: ctxCutSnap,
                // The Stop button's enabled state is the playback flag that also
                // covers the optimistic window between Play and the first events.
                lysPlaying: playingNow()
            });
        }, true);

        // M3 keyboard: Ctrl/Cmd+I asks the AI in Copilot's chat, as the header button does (owner's
        // request 2026-10-29) — about the bars selected, else the file; Escape clears the selection.
        window.addEventListener('keydown', (e) => {
            if ((e.ctrlKey || e.metaKey) && (e.key === 'i' || e.key === 'I')) {
                e.preventDefault(); askInChat();
            } else if (e.key === 'Escape') {
                if (aiRangeLo >= 0) { aiClearSelection(); clearHighlights(); }
            }
        });

        // Everything above is registered — tell the extension it is now safe
        // to post content (messages before this point would have been lost).
        vscode.postMessage({ type: 'webviewReady' });
    </script>
</body>
</html>`;
}
