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

// What the host keeps of ONE preview panel (no vscode import: tested with node:test).
//
// Until session 1294 this was seven maps keyed by the document's URI — the page's load, the
// refresh's timer and when its wait began, the picture last posted, the pages' version, the
// request's stamp and its cancellation — and the three places that must touch them all (the
// follower turning to another file, an untitled score saved, the panel closed) each kept its own
// list, which no longer agreed (review of session 1291, F2: when the wait began was dropped by
// none of them). The state is the PANEL's: a panel that shows another file takes it along
// with nothing to move, and each of the three says what happened to it in one call.

/** What cancels a request still on its way (vscode.CancellationTokenSource is one). */
export interface Cancellable {
    cancel(): void;
    dispose(): void;
}

export class PreviewSession {
    /** Settles when the page has loaded its HTML: a message posted before that is dropped. */
    readonly loaded: Promise<void>;
    private settleLoaded!: () => void;
    private hasLoaded = false;

    /**
     * The picture last posted as one string (svgPostKey), so that the same one is not posted
     * again: the page keeps its content while hidden. Undefined: nothing the page shows is known.
     */
    lastPostedSvg: string | undefined;

    /**
     * The version of the pages the page holds (SvgPages.Version of the last page answer posted),
     * sent with the next request so that the server answers with the pages that changed since.
     * Undefined: the next answer carries every page.
     */
    shownPagesVersion: number | undefined;

    private generation = 0;
    private request: Cancellable | undefined;
    private timer: ReturnType<typeof setTimeout> | undefined;
    private waitingSince: number | undefined;

    constructor() {
        this.loaded = new Promise<void>(resolve => { this.settleLoaded = resolve; });
    }

    /** Whether the page has reported in: one that has is not waited for. */
    get pageHasLoaded(): boolean { return this.hasLoaded; }

    /** The page (re)loaded: it is blank, whatever an earlier instance of it showed. */
    pageLoaded(): void {
        this.forgetPage();
        this.hasLoaded = true;
        this.settleLoaded();
    }

    /** What the page shows is not known any more: the next answer is posted, with every page. */
    forgetPage(): void {
        this.lastPostedSvg = undefined;
        this.shownPagesVersion = undefined;
    }

    /**
     * A render request leaves: the one before it, if still on its way, is cancelled. The stamp
     * handed back orders the ANSWERS (isLatest) — an old slow one arriving after a new fast one
     * must not paint over it.
     */
    beginRequest(request: Cancellable): number {
        this.cancelRequest();
        this.request = request;
        return this.generation;
    }

    /** Whether no request left, and nothing happened to the panel, after the one stamped `generation`. */
    isLatest(generation: number): boolean { return this.generation === generation; }

    /** Cancels the request on its way, if any: its answer is not the latest any more. */
    cancelRequest(): void {
        this.generation++;
        if (this.request) {
            this.request.cancel();
            this.request.dispose();
            this.request = undefined;
        }
    }

    /**
     * A change: `run` after `delay`, starting over at each change — but no later than `maxWait`
     * after the first change still waiting (a held key edits every ~30 ms, and the preview stood
     * still until it was let go; owner's report 2026-11-16).
     */
    refreshAfter(delay: number, maxWait: number, now: number, run: () => void): void {
        if (this.timer !== undefined) { clearTimeout(this.timer); }
        const since = this.waitingSince ?? now;
        this.waitingSince = since;
        this.timer = setTimeout(() => {
            this.timer = undefined;
            this.waitingSince = undefined;
            run();
        }, Math.max(0, Math.min(delay, since + maxWait - now)));
    }

    /** Drops a refresh still waiting, and with it when its wait began. */
    cancelRefresh(): void {
        if (this.timer !== undefined) { clearTimeout(this.timer); }
        this.timer = undefined;
        this.waitingSince = undefined;
    }

    /**
     * The panel shows another document now. What was on its way was about the one before: the
     * request is cancelled and a waiting refresh dropped (its callback holds that document).
     * @param samePicture An untitled score saved to a file: the page still shows that picture, so
     *   the same one is not posted again — but its pages' version is the old document's, which
     *   the server's session for the new one does not know. Otherwise (the follower turned) the
     *   page holds neither file's picture.
     */
    turned(samePicture: boolean): void {
        this.cancelRequest();
        this.cancelRefresh();
        if (samePicture) { this.shownPagesVersion = undefined; } else { this.forgetPage(); }
    }

    /** The panel closed. */
    dispose(): void {
        this.cancelRequest();
        this.cancelRefresh();
        this.forgetPage();
    }
}
