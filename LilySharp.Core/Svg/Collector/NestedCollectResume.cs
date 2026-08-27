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

using System;
using System.Collections.Generic;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// The session's checkpoint/resume channels for the collector's NESTED collects
/// (2026-08-26 review, finding 3-5): <c>HarvestOmittedStructure</c> — an undrawn
/// part with score-level structure (<c>|:</c>, a navigation mark, an inline
/// volta) — and <c>CollectMelodyFor</c> — a lyrics row that sings a part the
/// score does not engrave — each run a COMPLETE separate collect, per keystroke,
/// outside the resume machinery. This holds one (source, recording, baseline)
/// per channel and puts those collects on the SAME plan/restore/splice road the
/// main walk rides (<see cref="CollectResumePlanner"/>): a keystroke's nested
/// collect adopts its unchanged prefix instead of walking the omitted part's
/// whole book again.
/// </summary>
/// <remarks>
/// <para>
/// Channels are independent of the main collect's baseline: each is keyed by its
/// purpose (<c>"harvest"</c>, <c>"melody:&lt;part&gt;"</c>) and planned against
/// its OWN recorded baseline, so a main full collect and a nested resume can
/// coexist in one compile. A resumed nested collect does not re-record (the same
/// pinning as the main baseline, without the adoption heuristic — a nested
/// channel's stale window costs only the nested walk's speed).
/// </para>
/// <para>
/// SOUNDNESS is entirely the planner's and the walk's (the same guards, the same
/// nets): this class only decides record vs resume. The one obligation callers
/// carry is the ABORT contract — a <see cref="CollectResumeAbortException"/> from
/// a resumed nested collect must fall back to a FULL nested collect, never to
/// "no result": the nested sites' historical catch-all returns an EMPTY harvest
/// on any failure, which for a bailed resume would silently drop the score's
/// repeat barlines. Both sites re-run full inside their try before their
/// catch-all can see anything.
/// </para>
/// <para>
/// The CLI path never constructs one (a null <c>MeasureCollector.NestedResume</c>
/// falls back to the historical fresh-collector call), so the production full
/// path is untouched by construction.
/// </para>
/// </remarks>
internal sealed class NestedCollectResume
{
    private sealed class Channel
    {
        public MeasureCollector? Source;
        public CollectWalkProbe? Recording;
        public SyntaxTree? Baseline;
    }

    private readonly Dictionary<string, Channel> _channels = new(StringComparer.Ordinal);
    private SyntaxTree? _tree;
    private bool _allowResume;

    /// <summary>Nested collects resumed / run full over this instance's lifetime
    /// (diagnostics / the liveness half of the nets).</summary>
    public (int Resumed, int Full) Stats { get; private set; }

    /// <summary>Arms the channels for one compile: the tree every nested collect
    /// of this compile sees, and whether resuming is allowed at all (the session
    /// passes its own allowResume; the first compile records).</summary>
    public void BeginCompile(SyntaxTree tree, bool allowResume)
    {
        _tree = tree;
        _allowResume = allowResume;
    }

    /// <summary>Drops every channel (the session's spec-resolution drift guard —
    /// a channel's baseline was recorded under the old resolution).</summary>
    public void Reset()
    {
        _channels.Clear();
        _tree = null;
    }

    /// <summary>The probe for one nested collect: a planned RESUMER when the
    /// channel's baseline can serve this compile's tree, else a fresh RECORDER —
    /// complete the record path with <see cref="Complete"/> so the channel's
    /// baseline moves to this tree. Null when no compile is armed.</summary>
    public (CollectWalkProbe Probe, bool IsResume)? Begin(string key)
    {
        if (_tree == null)
            return null;
        if (_allowResume
            && _channels.TryGetValue(key, out var ch)
            && ch.Recording != null && ch.Source != null && ch.Baseline != null)
        {
            var resumer = CollectResumePlanner.Plan(ch.Baseline, _tree, ch.Recording, ch.Source);
            if (resumer != null)
            {
                Stats = (Stats.Resumed + 1, Stats.Full);
                return (resumer, true);
            }
        }
        Stats = (Stats.Resumed, Stats.Full + 1);
        return (CollectWalkProbe.Recorder(), false);
    }

    /// <summary>Stores a completed FULL nested collect as the channel's baseline.
    /// (A resumed nested collect keeps the recorded baseline pinned, like the
    /// main walk's.)</summary>
    public void Complete(string key, CollectWalkProbe recorder, MeasureCollector source)
    {
        if (_tree == null)
            return;
        if (!_channels.TryGetValue(key, out var ch))
            _channels[key] = ch = new Channel();
        ch.Source = source;
        ch.Recording = recorder;
        ch.Baseline = _tree;
    }
}
