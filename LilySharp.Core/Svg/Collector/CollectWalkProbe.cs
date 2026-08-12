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

using LilySharp.Core.Semantics;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Svg.Collector;

/// <summary>
/// S5 substrate (like <see cref="MeasureContentKey"/>): the checkpoint/resume
/// probe for the collector's primary voice walk, on which the per-measure
/// collect memo will stand. In RECORD mode <see cref="MeasureCollector"/> takes
/// a <see cref="WalkCheckpoint"/> at every eligible measure boundary of every
/// primary walk (<c>CollectMeasures</c>). In RESUME mode a later collect of the
/// SAME document skips everything in walk order before a recorded checkpoint —
/// adopting the recorded prefix measures and the side-table slices verbatim —
/// restores the checkpointed value state, and re-enters the same walk at the
/// checkpoint's node index. The resumed collect must be indistinguishable from
/// a full collect; <c>CollectResumeTests</c> is the completeness net that holds
/// the checkpoint's state inventory to that.
/// </summary>
/// <remarks>
/// <para>
/// WHY SAME-DOCUMENT ONLY (this slice): resuming across an EDIT additionally
/// needs (1) the dirty window (old/new text common prefix+suffix), (2) a source
/// position shifter over the adopted prefix/suffix (every burned-in
/// <c>SourcePosition</c>, <c>Measure.SourceStart/End</c>, side-table positions
/// — HANDOFF §1 ⒭ ⑶ has the inventory), and (3) checkpoint-vs-state equality
/// modulo that shift. All of that composes ON TOP of this machinery without
/// changing it: the Δ=0 resume already exercises the whole skip/adopt/restore
/// path, which is where the correctness risk lives.
/// </para>
/// <para>
/// WHY "SAME WALK RE-ENTRY": HANDOFF §2C ⑴ forbids growing a third walk. The
/// resume does not re-implement any collection; it fast-forwards the ONE
/// existing walk to a recorded state and lets it run. Anything the walk does
/// after the checkpoint (section prologues/epilogues, extra-voice tracks,
/// post-passes, finalizers) runs live and unchanged.
/// </para>
/// <para>
/// ELIGIBILITY over cleverness: a boundary is checkpointed only when every
/// cross-measure carry is quiescent (no pending grace/tremolo/empty-chord-slur,
/// phrase stacks empty, top-level stream). A whole walk is marked ineligible
/// when it crosses a regime the resume does not support yet: a form repeat
/// block (its bookkeeping reads builder state the skip would corrupt) or a
/// chord repetition <c>q</c> (its <c>_resolvedChordMembers</c> entry would be
/// missing when the original chord sits in the adopted prefix). A missed
/// checkpoint only costs reuse; a wrong one costs correctness.
/// </para>
/// </remarks>
internal sealed class CollectWalkProbe
{
    private CollectWalkProbe(bool recording) => IsRecording = recording;

    /// <summary>True = take checkpoints; false = consume resume plans.</summary>
    public bool IsRecording { get; }

    /// <summary>RECORD mode: one recording per primary walk, keyed by the walk's
    /// ordinal (the Nth <c>CollectMeasures</c> call of the collect).</summary>
    public Dictionary<int, VoiceWalkRecording> Recordings { get; } = new();

    /// <summary>RESUME mode: plans keyed by walk ordinal. A walk with no plan
    /// runs fully.</summary>
    public Dictionary<int, VoiceResumePlan> ResumePlans { get; } = new();

    public static CollectWalkProbe Recorder() => new(recording: true);

    public static CollectWalkProbe Resumer() => new(recording: false);
}

/// <summary>Everything one primary walk recorded: its checkpoints plus the
/// walk-local lists a resume must adopt by prefix (they are cleared per walk,
/// so the collector's final state does not retain them for earlier walks).</summary>
internal sealed class VoiceWalkRecording
{
    public string? VoiceName;

    public List<WalkCheckpoint> Checkpoints { get; } = new();

    /// <summary>The walk's measures BEFORE <c>FinalizeMeasures</c> mutates them
    /// (repeat collapse, trailing clef column) — the values a resumed builder
    /// must re-enter with, so its own finalize reproduces those mutations.</summary>
    public List<Measure>? PreFinalizeMeasures;

    /// <summary>Walk-local, cleared-per-walk lists, copied at walk end. Their
    /// prefix (by checkpoint count) is what a resume adopts.</summary>
    public List<(int, int, string, bool, int)>? PendingInlineVoltas;
    public List<(Syntax.ParallelExpressionSyntax, int, OctaveSnapshot)>? ParallelSpans;

    /// <summary>Non-null when the walk crossed a regime the resume does not
    /// support; its checkpoints must not be resumed from.</summary>
    public string? IneligibleReason;

    public void MarkIneligible(string reason) => IneligibleReason ??= reason;
}

/// <summary>A resume instruction for one primary walk: skip to
/// <see cref="Checkpoint"/>, adopting the prefix from <see cref="Recording"/>
/// (walk-local lists, pre-finalize measures) and the cumulative side-table
/// slices from <see cref="Source"/>'s final lists (append-only, so their first
/// N entries are exactly what stood at the checkpoint).</summary>
internal sealed class VoiceResumePlan
{
    public required WalkCheckpoint Checkpoint { get; init; }
    public required VoiceWalkRecording Recording { get; init; }
    public required MeasureCollector Source { get; init; }

    /// <summary>Set when the walk actually restored this plan — a plan left
    /// unconsumed means the walk never reached its target (a bug).</summary>
    public bool Consumed;
}

/// <summary>Full snapshot of <see cref="OctaveContext"/> (the existing
/// <see cref="OctaveSnapshot"/> deliberately carries only the two running
/// fields for nested frames; a walk checkpoint needs all of them).</summary>
internal readonly record struct OctaveCheckpoint(
    int CurrentOctave, char LastPitchName, int InitialOctave, int OctaveBase,
    bool OctaveAbsolute, bool InitialOctaveAbsolute, int DiatonicShiftSteps,
    bool HasTranspose, int TransposeStep, int TransposeAlt, int TransposeOctave)
{
    public static OctaveCheckpoint Capture(OctaveContext o) => new(
        o.CurrentOctave, o.LastPitchName, o.InitialOctave, o.OctaveBase,
        o.OctaveAbsolute, o.InitialOctaveAbsolute, o.DiatonicShiftSteps,
        o.HasTranspose, o.TransposeStep, o.TransposeAlt, o.TransposeOctave);

    public void Restore(OctaveContext o)
    {
        o.CurrentOctave = CurrentOctave;
        o.LastPitchName = LastPitchName;
        o.InitialOctave = InitialOctave;
        o.OctaveBase = OctaveBase;
        o.OctaveAbsolute = OctaveAbsolute;
        o.InitialOctaveAbsolute = InitialOctaveAbsolute;
        o.DiatonicShiftSteps = DiatonicShiftSteps;
        o.HasTranspose = HasTranspose;
        o.TransposeStep = TransposeStep;
        o.TransposeAlt = TransposeAlt;
        o.TransposeOctave = TransposeOctave;
    }
}

/// <summary>
/// One eligible measure boundary of a primary walk: where it is in walk order
/// (section visit + node-list invocation + node index), the builder's state,
/// the collector's value state, and how far every append-only output had grown.
/// The inventory follows HANDOFF §1's session-145 design memo (checkpoint =
/// value-state snapshot + per-table counts; accidentals are empty at a measure
/// boundary by construction, so they contribute nothing).
/// </summary>
internal sealed class WalkCheckpoint
{
    // --- walk-order address ---
    /// <summary>Ordinal of the ProcessSection call this boundary sits inside
    /// (per walk), or -1 for the section-less root path.</summary>
    public required int SectionVisit { get; init; }
    /// <summary>Ordinal of the ProcessNodes invocation (per walk).</summary>
    public required int Invocation { get; init; }
    /// <summary>Index of the next node to process within that invocation.</summary>
    public required int NodeIndex { get; init; }
    /// <summary>The section's start measure (ProcessSection's local), so the
    /// padding epilogue of a partially-resumed section computes the bars it
    /// produced from the right base.</summary>
    public required int SectionStartMeasure { get; init; }

    // --- builder ---
    public required MeasureBuilder.BuilderCheckpoint Builder { get; init; }

    // --- collector value state ---
    public required OctaveCheckpoint Octave { get; init; }
    public required MetadataState Meta { get; init; }          // a private clone
    public required Fraction DefaultDuration { get; init; }
    public required int DefaultDots { get; init; }
    public required int AmbientTonicStep { get; init; }
    public required int AmbientTonicAlter { get; init; }
    public required bool AmbientTonicValid { get; init; }
    public required (int Sharps, string? Custom)? OpeningKeyOverride { get; init; }
    public required int TremoloRepeatCount { get; init; }
    public required (int, int, int, int)? TremoloPairShape { get; init; }
    public required bool TremoloPairFirst { get; init; }
    public required HashSet<(string, string)> SectionActiveGrobProps { get; init; }
    public required SortedDictionary<int, (int TonicStep, int Sharps)> KeyByMeasure { get; init; }
    public required Dictionary<string, int> SectionStartMeasures { get; init; }
    public required Dictionary<string, List<int>> SectionAllStarts { get; init; }

    // --- append-only output watermarks ---
    /// <summary>Counts of the cumulative side tables, index-aligned with
    /// <c>MeasureCollector.CumulativeSideTables()</c>.</summary>
    public required int[] TableCounts { get; init; }
    /// <summary>Counts of the walk-local lists (adopted from the recording).</summary>
    public required int PendingInlineVoltaCount { get; init; }
    public required int ParallelSpanCount { get; init; }
    /// <summary>Measures emitted so far (= prefix length to adopt).</summary>
    public required int MeasureCount { get; init; }
}
