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

/**
 * Whether a Play's view follows the notes sounding, as the script a page includes (owner's
 * request 2026-11-11: a score dragged away while it played jumped back to the sounding note the
 * moment it was dropped).
 *
 * The view follows from the start of each Play. A scroll of the user's own — a drag, the wheel,
 * the scroll bar, a key — ends the following AT ONCE, wherever the sounding notes are (owner's
 * report the same day, on the first build: it ended only once they were out of the view, and
 * until then the page and the hand pulled the score to and fro). The page then moves only by the
 * user's hand, and a small button appears at the foot of the score: "Follow", with an arrow
 * towards the music when it is out of the view. The following comes back by that button and with
 * the next Play — not by itself. While a pointer is held down on the score the view is never
 * moved under it.
 *
 * A scroll is the user's when a hand was at it: a pointer held on the scroller (a drag, the
 * scroll bar), or a wheel or a key within the last moment. Any other scroll — the page laid out
 * again, a view restored — is not, and ends nothing.
 *
 * installFollowScroll(scroller, button) answers:
 *   start()        a Play begins: following, the button away
 *   stop()         it ended: the button away
 *   sounding(els)  the elements sounding now (the button's arrow follows them)
 *   follows()      whether the page may move the view to them now
 *   auto(scroll)   runs a scroll of the page's own, which is not taken for the user's
 *   onResume       set by the page: called with the sounding elements when the button is pressed
 *
 * A scroll of the page's own is known by `auto`, and ends with the scroller's `scrollend` (or
 * 1.5 s, where a scroll that moved nothing sends none); the user's wheel, pointer or key ends it
 * at once, so a hand that takes over in the middle of a smooth scroll is the user's from there.
 *
 * ONE text for the two pages that follow a Play: the preview and the AI candidate panel. It is
 * page script, not extension code: no template substitution stands in it, and no backtick.
 */
export const FOLLOW_SCROLL_JS = `        function installFollowScroll(scroller, button) {
            let playing = false, off = false, auto = false, autoTimer = null, held = false, handUntil = 0, els = [];
            // Where the sounding notes are: '' in the view, an arrow towards them when out of
            // it, null when nothing drawn sounds (a rest, a note on another page).
            function where() {
                let top = Infinity, bottom = -Infinity, left = Infinity, right = -Infinity;
                for (const el of els) {
                    const b = el.getBoundingClientRect();
                    if (b.width === 0 && b.height === 0) continue;
                    top = Math.min(top, b.top); bottom = Math.max(bottom, b.bottom);
                    left = Math.min(left, b.left); right = Math.max(right, b.right);
                }
                if (top === Infinity) return null;
                const m = scroller.getBoundingClientRect();
                if (bottom <= m.top) return '\\u2191';
                if (top >= m.bottom) return '\\u2193';
                if (right <= m.left) return '\\u2190';
                if (left >= m.right) return '\\u2192';
                return '';
            }
            let arrow = '';
            function show() {
                const shown = off && playing;
                if (shown) {
                    // In the view: no arrow. A rest, or a note not drawn: the arrow it had.
                    const w = where();
                    if (w !== null) arrow = w;
                    button.textContent = arrow ? arrow + ' Follow' : 'Follow';
                    // At the foot of the score, in its middle, wherever the page puts the score.
                    const m = scroller.getBoundingClientRect();
                    button.style.left = Math.round(m.left + m.width / 2) + 'px';
                    button.style.top = Math.round(m.bottom - 44) + 'px';
                }
                button.hidden = !shown;
            }
            function endAuto() {
                auto = false;
                if (autoTimer !== null) { clearTimeout(autoTimer); autoTimer = null; }
            }
            function hand() { endAuto(); handUntil = Date.now() + 500; }
            scroller.addEventListener('scroll', function () {
                if (auto || !playing) return;
                if (!off && !held && Date.now() > handUntil) return;    // not a hand's scroll
                off = true;
                show();
            });
            scroller.addEventListener('scrollend', endAuto);
            scroller.addEventListener('wheel', hand, { passive: true });
            scroller.addEventListener('pointerdown', function () { endAuto(); held = true; });
            window.addEventListener('pointerup', function () { held = false; });
            window.addEventListener('pointercancel', function () { held = false; });
            window.addEventListener('keydown', hand);
            window.addEventListener('resize', function () { if (off && playing) show(); });
            const api = {
                onResume: null,
                start: function () { playing = true; off = false; els = []; arrow = ''; show(); },
                stop: function () { playing = false; off = false; els = []; endAuto(); show(); },
                sounding: function (list) { els = list || []; if (off && playing) show(); },
                follows: function () { return !playing || (!off && !held); },
                auto: function (scroll) {
                    auto = true;
                    if (autoTimer !== null) clearTimeout(autoTimer);
                    autoTimer = setTimeout(endAuto, 1500);
                    scroll();
                },
            };
            button.addEventListener('click', function () {
                off = false;
                show();
                if (api.onResume) api.onResume(els);
            });
            return api;
        }
`;

/** The button's look, for both pages: a small pill over the score, in the editor's widget colours. */
export const FOLLOW_SCROLL_CSS = `
        .follow-btn {
            position: fixed; z-index: 6; transform: translateX(-50%);
            padding: 4px 12px; border-radius: 14px; font-size: 12px; line-height: 18px; cursor: pointer;
            background: var(--vscode-button-background, #0e639c);
            color: var(--vscode-button-foreground, #ffffff);
            border: 1px solid var(--vscode-contrastBorder, transparent);
            box-shadow: 0 2px 8px rgba(0, 0, 0, 0.35);
        }
        .follow-btn:hover { background: var(--vscode-button-hoverBackground, #1177bb); }
        .follow-btn[hidden] { display: none; }
`;
