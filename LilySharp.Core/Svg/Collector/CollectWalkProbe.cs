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

using System.Collections.Immutable;
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
/// CROSS-EDIT (Δ≠0) PREFIX RESUME: <see cref="CollectResumePlanner"/> extends the
/// same-document resume across an edit, for the PREFIX side only. The planner
/// computes the dirty window (old/new text common prefix P + common suffix) and
/// resumes each walk from its last checkpoint whose <b>entire read set</b> lies
/// in the unchanged prefix (<see cref="WalkCheckpoint.MaxSourceRead"/> ≤ P) —
/// so every adopted position is textually unchanged and NO position shifter is
/// needed. That is also why adopting old-tree node references
/// (<see cref="VoiceWalkRecording.ParallelSpans"/>) is sound here: their whole
/// subtrees live in the unchanged prefix, so walking them later produces the
/// same values a new-tree walk would.
/// </para>
/// <para>
/// CROSS-EDIT SUFFIX SPLICE (⒭ ⑵, second slice — second half): once the live
/// walk has processed PAST the dirty window, its state can meet a recorded
/// checkpoint again. At each clean boundary the walk looks the current address
/// up in <see cref="VoiceResumePlan.SuffixCandidates"/> (recorded checkpoints
/// whose own node and remaining header reads lie outside the window); on an
/// address hit it compares the ENTIRE live value state against the recorded
/// checkpoint — position-bearing fields compared through the per-position map
/// p ≤ P → p, p ≥ S → p+Δ (mixed provenance is real: a tail item cites its own
/// shifted text, a phrase-expanded tail item cites an unshifted prefix body).
/// When the state matches, the recorded tail — measures, cumulative side-table
/// slices up to <see cref="VoiceWalkRecording.EndCheckpoint"/>'s watermarks,
/// walk-local lists — is adopted as position-shifted COPIES, old-tree node
/// references (<see cref="VoiceWalkRecording.ParallelSpans"/>) are re-resolved
/// against the new tree, the collector's value state jumps to the recorded
/// end-of-walk state, and the rest of the walk is skipped. Any doubt — a
/// position inside the window, an unresolvable node, a changed canonical bar
/// count — declines the splice and the walk simply keeps running live: a
/// declined splice costs speed, never correctness (same posture as the plan).
/// Δm ≠ 0 needs no separate bail: the state comparison includes the emitted
/// measure count and every side-table watermark, so an edit that changed how
/// many measures/items the window produces never matches any checkpoint.
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
/// phrase stacks empty, top-level stream). Two regimes that used to mark a whole
/// walk ineligible were refined per finding 3-4 (2026-08-26 review): a form
/// repeat block now merely suppresses the checkpoints INSIDE it (its bookkeeping
/// lives in locals no checkpoint captures; boundaries before and after resume
/// normally), and a chord repetition <c>q</c> / bare duration is served by the
/// recording's <see cref="VoiceWalkRecording.ResolvedSpellings"/> log, replayed
/// re-keyed at restore and splice (with
/// <see cref="VoiceWalkRecording.RepetitionOriginalReads"/> guarding the splice
/// against a stale copy). A whole walk is still marked ineligible when the
/// expansion budget trips. A missed checkpoint only costs reuse; a wrong one
/// costs correctness.
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

    /// <summary>RESUME mode: the dirty window in OLD-text coordinates — the
    /// old/new common prefix length, the old-text start of the common suffix,
    /// and the length delta. The per-position shift map every suffix-splice
    /// comparison and copy goes through is
    /// <c>p ≤ WindowPrefix → p; p ≥ WindowSuffixStart → p + WindowDelta;
    /// else undefined</c> (an undefined position declines the splice).</summary>
    public int WindowPrefix;
    /// <inheritdoc cref="WindowPrefix"/>
    public int WindowSuffixStart;
    /// <inheritdoc cref="WindowPrefix"/>
    public int WindowDelta;

    /// <summary>RESUME mode: whether the canonical section bar counts of the
    /// edited text were verified equal to the recording's (null = not yet
    /// checked; the first splice attempt of the collect computes it once).
    /// The adopted tail contains section spacer padding whose COUNT is the
    /// canonical bar count — a function of every part's cell text, which the
    /// per-position output checks cannot see (HANDOFF §1 ⒭: "Δm≠0 は bail —
    /// canonical bars／spacer padding が他 part の小節数を読む").</summary>
    public bool? CanonicalBarsVerified;

    /// <summary>RESUME mode: the two trees, for the LAZY parse-agreement check
    /// (layer 4's suffix half — see <see cref="CollectResumePlanner"/>'s layer-4
    /// remarks), plus its once-per-collect memo. Lazy on purpose: the whole-tree
    /// structural walk is worth paying only when a splice's cheaper guards have
    /// all passed — pricing it at plan time taxed every keystroke whose splice
    /// candidates all decline (measured +30ms on perf-v2bow1k, session 148).
    /// ⚠️ Do NOT read that as "perf-v2bow1k can never splice" (session 149,
    /// measured and byte-verified): its one m=0 candidate splices the WHOLE
    /// primary-walk recording whenever the edit sits in a non-primary voice of
    /// the whole-book parallel span — the primary walk adopts no edited byte,
    /// and voices 2..N are rebuilt live from the re-resolved spans. What always
    /// declines is a PRIMARY-voice edit, whose adopted tail overlaps the window.
    /// Both regimes are held by CollectEditResumeTests' V2bowWholeBookSpan
    /// tests.</summary>
    public Syntax.SyntaxNode? BaselineRoot;
    /// <inheritdoc cref="BaselineRoot"/>
    public Syntax.SyntaxNode? NewRoot;
    /// <inheritdoc cref="BaselineRoot"/>
    public bool? ParseAgreementsVerified;

    public static CollectWalkProbe Recorder() => new(recording: true);

    public static CollectWalkProbe Resumer() => new(recording: false);
}

/// <summary>
/// A resume could not proceed: a runtime validation (walk-order address,
/// cumulative-table watermark, voice identity) found the document's structure
/// drifted from the recording in a way the plan-time guards could not see.
/// The collect in progress is unusable; the caller falls back to a full
/// collect with a FRESH collector (<see cref="IncrementalCompiler"/> does, and
/// re-records the baseline there). Correctness is never at stake — a bail only
/// costs the reuse.
/// </summary>
internal sealed class CollectResumeAbortException : Exception
{
    public CollectResumeAbortException(string message) : base(message) { }
}

/// <summary>Everything one primary walk recorded: its checkpoints plus the
/// walk-local lists a resume must adopt by prefix (they are cleared per walk,
/// so the collector's final state does not retain them for earlier walks).</summary>
internal sealed class VoiceWalkRecording
{
    public string? VoiceName;

    /// <summary>The cumulative side tables' counts when this walk STARTED — the
    /// cross-edit resume verifies the live counts against these at walk entry,
    /// because a checkpoint's <see cref="WalkCheckpoint.TableCounts"/> watermark
    /// bakes in every earlier walk's contribution: if an edit changed an earlier
    /// walk's item count, the watermark slices would land at shifted offsets.</summary>
    public int[]? StartTableCounts;

    /// <summary>The header spans this walk read, in walk order: the part's
    /// name/config children at entry, then each visited section's name and
    /// header directives. These reads burn label/data-pos positions and seed
    /// prologue state but are NOT in <see cref="WalkCheckpoint.MaxSourceRead"/>
    /// (declarations often sit BELOW the music that references them, so a scalar
    /// max would reject everything); instead each checkpoint records how many
    /// were read (<see cref="WalkCheckpoint.HeaderReadCount"/>) and the planner
    /// verifies that prefix span-by-span — content AND position stable.</summary>
    public List<Syntax.TextSpan>? HeaderReads;

    public List<WalkCheckpoint> Checkpoints { get; } = new();

    /// <summary>The walk's measures BEFORE <c>FinalizeMeasures</c> mutates them
    /// (repeat collapse, trailing clef column) — the values a resumed builder
    /// must re-enter with, so its own finalize reproduces those mutations.</summary>
    public List<Measure>? PreFinalizeMeasures;

    /// <summary>The end-of-walk state, captured at the walk's final clean
    /// boundary (null when the walk ends mid-measure or a carry is in flight —
    /// such a walk cannot suffix-splice, only prefix-resume). Its address
    /// fields are sentinels (-2/-1); what a splice consumes is the VALUE state
    /// (the state post-walk passes read: metadata for CaptureScoreContent,
    /// the section maps, the key timeline) and the watermark counts that end
    /// the adopted tail slices.</summary>
    public WalkCheckpoint? EndCheckpoint;

    /// <summary>Walk-local, cleared-per-walk lists, copied at walk end. Their
    /// prefix (by checkpoint count) is what a resume adopts.</summary>
    public List<(int, int, string, bool, int)>? PendingInlineVoltas;
    public List<(Syntax.ParallelExpressionSyntax, int, Fraction, OctaveSnapshot)>? ParallelSpans;

    /// <summary>The walk's resolved-spelling log, in insertion order: every note or
    /// chord this walk resolved that IS the original of some <c>q</c> / bare duration
    /// in the tree (the reverse sets of <c>Music.ChordRepetitions</c> /
    /// <c>Music.BareDurations</c> filter the writes, so a book without repetition
    /// spellings logs nothing). A resume replays the checkpoint's prefix of this
    /// list into <c>_resolvedNotes</c> / <c>_resolvedChordMembers</c>, RE-KEYED onto
    /// the new tree's nodes (the readers look originals up by the new tree's
    /// <c>OriginalOf</c> answers, and red nodes are not shared between trees) —
    /// what used to make any such book resume-ineligible wholesale
    /// (2026-08-26 review, finding 3-4). Replayed in order so a form replay's
    /// overwrite lands last, exactly as the recorded walk left the dictionaries.</summary>
    public List<(Syntax.SyntaxNode Node, ImmutableArray<MeasureCollector.ResolvedChordMember> Members)>?
        ResolvedSpellings;

    /// <summary>Per <c>q</c> / bare duration this walk built, in walk order: the
    /// FULL SPAN of the ORIGINAL it copied, or (-1, -1) when none resolved. The
    /// suffix splice reads its checkpoint range of this list to decline a tail
    /// whose repetition copies state from the re-walked live region — certified
    /// only when the original lies ENTIRELY in the unchanged common prefix or
    /// entirely in the adopted tail (the whole span matters: an edit INSIDE the
    /// original node leaves its start before the window) — see
    /// <c>TrySpliceSuffix</c>'s guard.</summary>
    public List<(int Start, int End)>? RepetitionOriginalReads;

    /// <summary>Non-null when the walk crossed a regime the resume does not
    /// support; its checkpoints must not be resumed from.</summary>
    public string? IneligibleReason;

    public void MarkIneligible(string reason) => IneligibleReason ??= reason;
}

/// <summary>A resume instruction for one primary walk: skip to
/// <see cref="Checkpoint"/> (the PREFIX side; null when no prefix checkpoint
/// qualifies but a suffix splice might), adopting the prefix from
/// <see cref="Recording"/> (walk-local lists, pre-finalize measures) and the
/// cumulative side-table slices from <see cref="Source"/>'s final lists
/// (append-only, so their first N entries are exactly what stood at the
/// checkpoint). <see cref="SuffixCandidates"/> is the SUFFIX side: recorded
/// checkpoints the live walk may re-meet past the dirty window, keyed at walk
/// entry by their shifted address for O(1) lookup per clean boundary.</summary>
internal sealed class VoiceResumePlan
{
    public required WalkCheckpoint? Checkpoint { get; init; }
    public required VoiceWalkRecording Recording { get; init; }
    public required MeasureCollector Source { get; init; }

    /// <summary>Recorded checkpoints eligible as suffix splice targets: their
    /// own node and every header read at-or-after theirs lies outside the dirty
    /// window, and the recording has an <see cref="VoiceWalkRecording.EndCheckpoint"/>
    /// to jump to. In walk order (a subrange of the recording's checkpoints).</summary>
    public IReadOnlyList<WalkCheckpoint>? SuffixCandidates { get; init; }

    /// <summary>Set when the walk actually restored this plan's prefix
    /// checkpoint — a prefix plan left unconsumed means the walk never reached
    /// its target (a bug).</summary>
    public bool Consumed;

    /// <summary>Set when the walk spliced the recorded suffix: how many
    /// measures the splice adopted (the tail from the matched checkpoint to the
    /// walk's end). 0 = no splice (the walk ran live to its end).</summary>
    public int SplicedMeasures;
}

/// <summary>Full snapshot of <see cref="OctaveContext"/> (the existing
/// <see cref="OctaveSnapshot"/> deliberately carries only the two running
/// fields for nested frames; a walk checkpoint needs all of them).</summary>
internal readonly record struct OctaveCheckpoint(
    int CurrentOctave, char LastPitchName, int InitialOctave, int OctaveBase,
    bool OctaveAbsolute, bool InitialOctaveAbsolute,
    bool HasTranspose, int TransposeStep, int TransposeAlt, int TransposeOctave)
{
    public static OctaveCheckpoint Capture(OctaveContext o) => new(
        o.CurrentOctave, o.LastPitchName, o.InitialOctave, o.OctaveBase,
        o.OctaveAbsolute, o.InitialOctaveAbsolute,
        o.HasTranspose, o.TransposeStep, o.TransposeAlt, o.TransposeOctave);

    public void Restore(OctaveContext o)
    {
        o.CurrentOctave = CurrentOctave;
        o.LastPitchName = LastPitchName;
        o.InitialOctave = InitialOctave;
        o.OctaveBase = OctaveBase;
        o.OctaveAbsolute = OctaveAbsolute;
        o.InitialOctaveAbsolute = InitialOctaveAbsolute;
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

    // --- cross-edit (Δ≠0) validity ---
    /// <summary>The end of the furthest source text the walk had READ when this
    /// boundary was captured: every processed node (any depth, phrase bodies
    /// included), every lookahead peek, and — at each section end — the whole
    /// span every part contributes to that section (the padding epilogue prices
    /// the section off ALL parts' bar counts). A checkpoint is valid across an
    /// edit iff this is ≤ the old/new common prefix: then the adopted state is a
    /// function of unchanged text, positions included. The walk's OTHER reads —
    /// part headers, section names/header directives, file-level defaults — are
    /// deliberately not folded here (a scalar high-water mark would poison every
    /// phrase-style book whose declarations sit below the music); they are
    /// plan-time-checkable constants, verified per edit by
    /// <see cref="CollectResumePlanner"/>'s top-level window guard.</summary>
    public required int MaxSourceRead { get; init; }
    /// <summary>Source start (full span) of the node at <see cref="NodeIndex"/>
    /// when recorded. Revalidated on the NEW tree at restore: the prefix text is
    /// unchanged, so an unchanged address holds a node with the same start —
    /// anything else is structural drift and the resume bails.</summary>
    public required int NodeStart { get; init; }
    /// <summary>How many of the walk's <see cref="VoiceWalkRecording.HeaderReads"/>
    /// had been read at this boundary — the planner validates exactly that prefix,
    /// so a shifted LATER section header does not reject an EARLIER checkpoint.</summary>
    public required int HeaderReadCount { get; init; }

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
    /// <summary>Watermarks into the collector's key-modulation / section-start
    /// journals (<c>_keyByMeasureLog</c> / <c>_sectionStartLog</c>): the maps they
    /// materialize used to be COPIED per boundary — O(boundaries × (sections +
    /// modulations)) per full collect — where the journals are append-only across
    /// the collect, so a count pins the same state and a restore replays the
    /// source's journal prefix (2026-08-26 review Tier 5-3).</summary>
    public required int KeyLogCount { get; init; }
    public required int SectionStartLogCount { get; init; }

    // --- append-only output watermarks ---
    /// <summary>Counts of the cumulative side tables, index-aligned with
    /// <c>MeasureCollector.CumulativeSideTables()</c>.</summary>
    public required int[] TableCounts { get; init; }
    /// <summary>Counts of the walk-local lists (adopted from the recording).</summary>
    public required int PendingInlineVoltaCount { get; init; }
    public required int ParallelSpanCount { get; init; }
    /// <summary>Watermarks into <see cref="VoiceWalkRecording.ResolvedSpellings"/> /
    /// <see cref="VoiceWalkRecording.RepetitionOriginalReads"/> (finding 3-4).</summary>
    public required int ResolvedSpellingCount { get; init; }
    public required int RepetitionReadCount { get; init; }
    /// <summary>Measures emitted so far (= prefix length to adopt).</summary>
    public required int MeasureCount { get; init; }
}
