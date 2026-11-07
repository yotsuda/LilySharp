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

// Writes the MP4s the server prepared (lilysharp/export, mp4, playbackOnly): each one's sound
// rendered as the preview plays it, joined by ffmpeg to the pictures the server drew. The
// arguments are videoExportCore.ts.

import * as cp from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import { AudioJob, renderAudioFiles } from './audioRender';
import { WAV_SAMPLE_RATE, frameCount } from './audioRenderCore';
import { videoArgs } from './videoExportCore';

/** One video to write, as the server gives it (PlaybackExport with its pictures). */
export interface VideoJob extends AudioJob {
    Frames?: string | null;
    WorkDirectory?: string | null;
    Lead?: number;
}

/** Runs a program to its end: its error output when it fails, null when it succeeds. */
function run(program: string, args: string[]): Promise<string | null> {
    return new Promise(resolve => {
        const child = cp.spawn(program, args, { windowsHide: true });
        let stderr = '';
        child.stderr.on('data', (d: Buffer) => { stderr += d.toString(); });
        child.on('error', e => resolve(String(e)));
        child.on('close', code => resolve(code === 0 ? null : (stderr.trim() || `exit code ${code}`)));
    });
}

/** True when `program` runs (`-version`): asked before anything is rendered. */
export async function ffmpegRuns(program: string): Promise<boolean> {
    return (await run(program, ['-version'])) === null;
}

/**
 * Writes every job: its sound through the sound font at `bankPath` into its work folder, then
 * ffmpeg joins the pictures and the sound into its MP4. Every job's work folder (the server's
 * pictures) is deleted afterwards, written or not — a failure part-way leaves none behind.
 * Throws with ffmpeg's own words when it fails.
 */
export async function writeVideos(jobs: readonly VideoJob[], bankPath: string, ffmpeg: string): Promise<void> {
    try {
        for (const job of jobs) {
            if (!job.Frames || !job.WorkDirectory) {
                throw new Error(`no pictures were drawn for ${job.OutputPath}`);
            }
            const wav = path.join(job.WorkDirectory, 'sound.wav');
            await renderAudioFiles([{ OutputPath: wav, Notes: job.Notes }], bankPath, 'wav');
            const lead = job.Lead ?? 0;
            const seconds = lead + frameCount(job.Notes) / WAV_SAMPLE_RATE;
            fs.mkdirSync(path.dirname(job.OutputPath), { recursive: true });
            const failure = await run(ffmpeg, videoArgs(job.Frames, wav, job.OutputPath, lead, seconds));
            if (failure !== null) {
                throw new Error(`ffmpeg could not write ${path.basename(job.OutputPath)}: ${failure.split(/\r?\n/).slice(-3).join(' ')}`);
            }
        }
    } finally {
        for (const job of jobs) {
            if (job.WorkDirectory) { fs.rmSync(job.WorkDirectory, { recursive: true, force: true }); }
        }
    }
}
