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
using System.Globalization;
using System.Linq;
using System.Text;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Rendering.Svg;

/// <summary>Identifies one page-level overlay drawer's fragment stream in
/// <see cref="SvgSystemFragmentCache"/> (⒭ second slice). One id per drawer that has
/// been put on the fragment mechanism — extend as further drawers earn their keep
/// (each needs its own value fold; see the class remarks' OVERLAY ENTRIES).</summary>
internal enum OverlayDrawerId
{
    Fingerings = 0,
}

/// <summary>
/// F3/S5 (⒭ render, first slice): a session-scoped memo of ONE SYSTEM's DrawSystem
/// SVG text. On an edit, a system whose content keys and drawn geometry are unchanged
/// re-appends its recorded text instead of re-walking the whole draw (the dominant
/// render term on a keystroke — measured session 151: render.systems 96/156/57 ms of
/// the plain1k/fingbeam1k/v2bow1k keystroke floors, page overlays ≈ 0 outside
/// fingerings/bows). SVG backend only; the PDF/PNG backends never see this type.
/// </summary>
/// <remarks>
/// <para>
/// SOUNDNESS — the key is the full inventory of what <c>SharedRenderer.DrawSystem</c>
/// (and its callees, DrawBeams included) reads, each read traced to its fold
/// (session 151, the ⒟⁗ discipline):
/// <list type="bullet">
/// <item>SCORE-side reads — every staff's voices' items, the ENTRY CONTEXT (the active
/// clef/key/time that ResolveClef/ResolveKeySignature re-derive at the system start),
/// staff identity (tab tuning, text-row, lines, instrument name, per-staff key…),
/// group identity, side-tables (PercentRepeats included), MMR run membership and the
/// boundary clef allowance — are all folded per measure by
/// <see cref="MeasureContentKey"/> ("key-equality means render-identity"). Score-global
/// quantities the draw derives (MaxClefWidth, ClefGroupInkLeft, sharedKeyX/TimeX,
/// lead-sheet-ness, lyric-row structure) are functions of staff structure + entry
/// context, folded into EVERY key (the ⒟⁗ inventory).</item>
/// <item>NEIGHBOUR reads: GetSystemEndKeyChange / GetSystemEndTimeChange read the
/// OPENING items of the measure after the system's last (the end-of-line courtesy) —
/// key[last+1]. The left neighbour (key[first−1]) has no read found in the walk and is
/// folded anyway, matching <c>IncrementalCompiler.SpringReusable</c>'s window shape:
/// over-sensitivity only costs one extra live system per edit. Neighbour EXISTENCE
/// must match on both sides (last system's edge pins the window).
/// ⚠️ The window has no isolating positive control yet (measured, session 151): every
/// edit that changes the courtesy also lands a slot inside the edit window (the
/// courtesy carries the neighbour item's data-pos) or moves the reservation geometry,
/// so a window-dropping poison leaves all current nets green. Kept on the fold
/// argument — an item whose SourcePosition lags outside its own value text would slip
/// past both other layers.</item>
/// <item>GEOMETRY reads — system.Y/Width/PrefixWidth/Indent/SystemIndex, every
/// measure's X/Width/Items/Columns/LooseChangeHangs, the staff-group table (staff Ys,
/// heights, hidden flags, delimiter box) and the page height (the Y-flip bakes it into
/// every emitted Y) — are folded by VALUE into <see cref="Entry.GeometryHash"/>. A 64-bit
/// FNV fold decides equality, the same bound <see cref="MeasureContentKey"/> already
/// accepts for reuse (~2⁻⁶⁴ per differing leaf). NOT the measure NUMBERS: they select
/// content the slice certifies and are emitted nowhere, and folding them made a bar
/// inserted before a system a guaranteed miss for every later system (session 330 —
/// see KeyMatches). In the preview's system-local frames
/// (<see cref="IDocumentContext.SystemLocalFrames"/>) system.Y is a constant too, which
/// is what lets a system that moved down the page replay.</item>
/// <item>PER-ITEM LAYOUT answers (VoiceOffsets / HeadWipes / DotAdjustments /
/// RestShifts / RestDotOffsets) are keyed by (measure, voice, item) and are functions
/// of the measures at that index — folded by the content keys.</item>
/// <item>BEAMS: DrawBeams draws the groups whose first measure lies in this system;
/// their quanted values are functions of the system's content slice
/// (<see cref="SystemLayoutCache.GetOrComputeStaffSystemBeams"/>'s coverage claim) —
/// EXCEPT a group whose members cross a system boundary, which reads the neighbour
/// system's measures. <see cref="PrepareRender"/> declines every system such a group
/// touches (correctness-neutral: they draw live).</item>
/// <item>DECLINE CLASSES (per pass): grob overrides/reverts (spread spacing globally —
/// same gate as every other memo), any ossia staff (OssiaAppearedBefore scans EARLIER
/// systems — outside any per-system window), a custom `font "NAME"`
/// (TextFontDrawingContext rewrites families in the emitted bytes), missing content
/// keys. All are "no cache this pass", never a wrong reuse.</item>
/// </list>
/// </para>
/// <para>
/// DATA-POS RE-RESOLUTION — the part the HANDOFF ticket flags. The recorded text bakes
/// in <c>data-pos</c>/<c>data-alt</c> SOURCE OFFSETS, and a real keystroke (insert or
/// delete, Δ≠0) shifts every offset at/after the edit even where the drawn geometry is
/// untouched, so the fragment is stored as text-around-number SEGMENTS plus a slot list
/// and each replay re-emits the numbers through the edit window: offset &lt; prefix →
/// unchanged; offset ≥ suffixStart → +Δ; offset INSIDE the window → the whole fragment
/// declines. This is the same arithmetic the collect splice applies to burned positions
/// (<c>CollectTailShifter</c>): outside the window the text is byte-identical, so the
/// live model's re-derived offsets are exactly the shifted ones. Slot positions are not
/// guessed from the text: the drawing context logs every source value it emits during
/// capture (<see cref="SvgDrawingContext.SourceLog"/>) and the capture scan must
/// reproduce that log exactly, or the system is not cached (a lyric that happens to
/// contain the literal <c>data-pos="…"</c> text defeats the scan and is declined).
/// </para>
/// <para>
/// SIDE CHANNEL: MusicFaceAttr RECORDS each glyph's Emmentaler design so the document
/// embeds the faces the score used. A replayed fragment skips those calls, so each
/// entry carries the design set its capture recorded and the replay merges it back —
/// without this, an edit far from the score's only grace note would silently drop the
/// small design's @font-face from an EmbedFont render.
/// </para>
/// <para>
/// ⚠️ THIS REMOVES A SAFETY NET, deliberately (the ⒟⁗ ticket's requirement to say so):
/// for unchanged systems the live draw — which re-reads the whole model every keystroke
/// — no longer runs, so a render input missing from this key would go unnoticed here.
/// What stands guard is the incremental==full byte-identity net over content-changing,
/// content-preserving and Δ≠0 edits (IncrementalCompilerTests), with poison controls
/// proving both the key window and the slot shift are load-bearing.
/// </para>
/// <para>
/// STALENESS: slot values are offsets into the text of the render that produced them,
/// so an entry is replayable ONLY on the immediately following render (the window maps
/// exactly one text to the next). <see cref="BeginPass"/> makes the previous pass's
/// table the only source of replays and starts an empty one for this pass; an entry
/// replayed or re-captured is carried into it under the index it was drawn at, and a
/// declined or skipped system's entry is simply not carried — the next eligible edit
/// re-captures it from the live draw. (The overlay entries keep a generation stamp for
/// the same rule.)
/// </para>
/// <para>
/// OVERLAY ENTRIES (⒭ second slice — the page-level drawers that run AFTER the
/// per-system loop, drawer-major, so their per-PAGE output is the contiguous unit):
/// keyed by (drawer, page) and by a VALUE FOLD of the drawer's exact draw inputs, not
/// by content keys. An overlay drawer reads nothing but its layout annotation array
/// and the page's geometry maps, so the caller folds, per drawn item, the very values
/// the emission is a function of (resolved x/y, the glyph-selecting payload, the page
/// height the Y-flip bakes into every emitted Y) — key-equality here IS
/// emission-identity, with no model-fold inventory to keep in sync. Source positions
/// stay out of the fold (they legitimately shift) and ride the same anchor + slot
/// machinery as the system entries. Same pass gates (<see cref="PrepareRender"/>),
/// same staleness, same all-or-nothing replay.
/// </para>
/// </remarks>
internal sealed class SvgSystemFragmentCache
{
    private sealed class Entry
    {
        // Key.
        public ImmutableArray<MeasureContentKey> Slice; // [first-1 .. last+1] as present
        public bool HasLeft, HasRight;
        public int MeasureCount, SystemIndex;
        public long GeometryHash;
        public string GeometryScalars = "";   // the fold spelled out, for a mismatch report
        // The system's source ANCHORS at capture time (see PositionFingerprint):
        // replay demands the live score's anchors equal these mapped through the edit
        // window, or the fragment declines.
        public int[] Anchors = [];
        // Value: Text with the slot numbers removed; InsertAt[i] is the position in
        // Text where Values[i] is re-emitted. Values are CURRENT-text offsets.
        public string Text = "";
        public int[] InsertAt = [];
        public int[] Values = [];
        public int[]? Designs;   // Emmentaler designs the capture recorded (usually null)
    }

    // Page-level overlay fragments (⒭ second slice): one drawer's output on one page,
    // keyed by the caller's value fold of that drawer's draw inputs (see class remarks).
    private sealed class OverlayEntry
    {
        public long ValueHash;
        public int[] Anchors = [];
        public string Text = "";
        public int[] InsertAt = [];
        public int[] Values = [];
        public int[]? Designs;
        public int Generation;
    }

    // The system entries of the PREVIOUS pass (what this pass may replay from) and of
    // THIS pass (what it stores and carries forward), each by the system index the entry
    // was drawn or replayed at. Two dictionaries rather than one with a generation
    // stamp, because a system may replay from the entry of a NEIGHBOURING index — a
    // `break` inserted before it moved its number by one — and writing that entry under
    // its new index must not overwrite the entry the next system is about to look for.
    // An entry that neither replays nor is re-captured this pass is simply not carried
    // forward, which is the staleness rule the generation stamp used to enforce.
    private Dictionary<int, Entry> _previous = new();
    private Dictionary<int, Entry> _current = new();
    private readonly Dictionary<(OverlayDrawerId Drawer, int Page), OverlayEntry> _overlays = new();
    private ImmutableArray<MeasureContentKey> _keys;
    private int _generation;

    // Edit window (old-text coordinates) mapping the PREVIOUS render's offsets to the
    // current text; invalid on the first render and whenever the caller cannot name it.
    private bool _windowValid;
    private int _windowPrefix, _windowSuffixStart, _windowDelta;

    // Per-render state set by PrepareRender.
    private bool _enabled;
    private HashSet<int>? _declinedSystems;

    /// <summary>(Replayed, Drawn-and-captured) system counts of the most recent render
    /// pass. Systems declined per pass count as drawn. For diagnostics / tests.</summary>
    public (int Replayed, int Drawn) LastPass { get; private set; }

    /// <summary>(Replayed, Drawn-and-captured) overlay-fragment counts of the most
    /// recent render pass, over (drawer, page) units. For diagnostics / tests.</summary>
    public (int Replayed, int Drawn) LastOverlayPass { get; private set; }

    /// <summary>Starts an edit pass: the content keys of the CURRENT score and the text
    /// window mapping the previous render's source offsets onto the current text
    /// (<paramref name="windowValid"/> false disables replay; capture still runs).
    /// Bumps the staleness generation — entries not touched by the previous pass
    /// never replay.</summary>
    public void BeginPass(ImmutableArray<MeasureContentKey> keys,
        bool windowValid, int prefix, int suffixStart, int delta)
    {
        _keys = keys;
        _windowValid = windowValid;
        _windowPrefix = prefix;
        _windowSuffixStart = suffixStart;
        _windowDelta = delta;
        _generation++;
        _enabled = false;
        _declinedSystems = null;
        // This pass replays from what the previous pass left; what it leaves is a
        // fresh dictionary (see the fields' remark).
        _previous = _current;
        _current = new Dictionary<int, Entry>();
    }

    /// <summary>
    /// Per-render eligibility: computes the decline classes that are cheaper to decide
    /// once per pass than per system. Called by <c>SharedRenderer.RenderTo</c> before
    /// the first system is drawn.
    /// </summary>
    public void PrepareRender(MultiStaffScore score, ScoreLayout layout)
    {
        LastPass = (0, 0);
        LastOverlayPass = (0, 0);
        LastPassDeclines.Clear();
        _enabled = !_keys.IsDefault
            && score.GrobOverrides.IsDefaultOrEmpty
            && score.GrobReverts.IsDefaultOrEmpty
            // A bound face changes the font-family attributes in the emitted bytes, and a
            // fragment recorded under one plan is not replayable under another. Declining
            // the whole memo is the same conservative answer `font "NAME"` already got —
            // keying fragments by the plan's signature would let it stay on, and is worth
            // doing only once a score that binds faces is also a score being edited live.
            && score.Fonts.IsDefault;
        if (!_enabled)
            return;
        foreach (var (_, st, _) in score.EnumerateStaves())
        {
            if (st.IsOssia)
            {
                _enabled = false;
                return;
            }
        }

        // Cross-system beam groups: every system such a group touches draws live.
        // measure → system, one pass over the layout (not per beam member).
        int maxMeasure = -1;
        foreach (var s in layout.AllSystems)
            if (!s.Measures.IsDefaultOrEmpty && s.Measures.Length > 0)
                maxMeasure = Math.Max(maxMeasure, s.Measures[^1].MeasureIndex);
        var measureToSystem = new int[maxMeasure + 1];
        Array.Fill(measureToSystem, -1);
        foreach (var s in layout.AllSystems)
        {
            if (s.Measures.IsDefaultOrEmpty)
                continue;
            foreach (var m in s.Measures)
                if (m.MeasureIndex >= 0 && m.MeasureIndex <= maxMeasure)
                    measureToSystem[m.MeasureIndex] = s.SystemIndex;
        }
        int SystemOf(int mi) => mi >= 0 && mi <= maxMeasure ? measureToSystem[mi] : -1;

        HashSet<int>? declined = null;
        foreach (var beam in layout.BeamLayouts)
        {
            int owner = SystemOf(beam.Group.MeasureIndex);
            bool crosses = false;
            foreach (var member in beam.Group.Members)
            {
                int mi = member.MeasureIndex >= 0 ? member.MeasureIndex : beam.Group.MeasureIndex;
                if (SystemOf(mi) != owner)
                {
                    crosses = true;
                    break;
                }
            }
            if (!crosses)
                continue;
            declined ??= new HashSet<int>();
            if (owner >= 0)
                declined.Add(owner);
            foreach (var member in beam.Group.Members)
            {
                int mi = member.MeasureIndex >= 0 ? member.MeasureIndex : beam.Group.MeasureIndex;
                int sys = SystemOf(mi);
                if (sys >= 0)
                    declined.Add(sys);
            }
        }
        _declinedSystems = declined;
    }

    /// <summary>
    /// Replays the system's recorded text into the current page when the key matches,
    /// the live score's source anchors equal the recorded ones mapped through the edit
    /// window, and every slot offset maps. Returns false when the system must be drawn
    /// live (then <see cref="BeginCapture"/> records it).
    /// </summary>
    public bool TryReplay(MultiStaffScore score, SystemLayout system,
        SvgDocumentContext host, double pageHeight)
    {
        if (!_enabled || _declinedSystems?.Contains(system.SystemIndex) == true || !_windowValid)
            return Decline(system, !_enabled ? "memo disabled" : !_windowValid ? "no window" : "declined system");
        // The entry drawn at this index last pass — or, when a system was inserted or
        // removed before this one, the entry a neighbouring index left: the number is a
        // stamp (see KeyMatches), so the same music one or two indices away is the same
        // text. Nearest first; the key check is what certifies the candidate.
        // ⚠️ THE ANCHOR CHECK IS LOAD-BEARING, not belt: content keys deliberately
        // exclude source positions, so "same content keys + same geometry" does NOT
        // imply the live model's positions follow the text window — the session-151
        // fuzz found an error-recovery edit (`melody {` → `me{dy {`, Δ=−1) whose
        // recovered model converges to the SAME content anchored 2 chars away, where
        // the window shifts by 1. The live anchors must equal the recorded ones mapped
        // through the window, or the recorded slot values are not the live offsets.
        // ⚠️ AND IT IS PART OF CHOOSING THE CANDIDATE, not a check after: a book of
        // repeated bars has neighbouring systems with the same keys and the same
        // geometry, and the one at this index — another system's entry, once a system
        // was inserted before it — passes the key and fails here; the entry one index
        // over is the one whose anchors follow (session 330's inserted-system net).
        var liveAnchors = PositionFingerprint(score, system);
        Entry? e = null;
        int foundAt = -1;
        int[]? final = null;
        string? nearest = null;
        foreach (int d in NeighbourOffsets)
        {
            int at = system.SystemIndex + d;
            if (at < 0 || !_previous.TryGetValue(at, out var candidate))
                continue;
            string why;
            if (!KeyMatches(candidate, system, pageHeight, out string mismatch))
                why = mismatch;
            else if (!AnchorsFollowWindow(candidate.Anchors, liveAnchors))
                why = "anchors do not follow the window";
            // Every slot must map before anything is appended (all-or-nothing).
            else if (!TryMapSlots(candidate.Values, out final))
                why = "a slot lies inside the window";
            else
            {
                e = candidate;
                foundAt = at;
                break;
            }
            nearest = (nearest == null ? "" : nearest + " / ") + "entry " + at + ": " + why;
        }
        if (e == null || final == null)
            return Decline(system, nearest ?? "no entry within " + NeighbourOffsets[^1] + " of this index");

        AppendFragment(host.CurrentContent!, e.Text, e.InsertAt, final);

        if (e.Designs != null)
            foreach (var d in e.Designs)
                host.UsedDesigns.Add(d);

        e.Values = final;
        // The anchors move WITH the values: both are offsets in the text of the render
        // that last touched the entry, and the next replay's window maps exactly that
        // text to its successor. Leaving the anchors at their capture-time values while
        // the values advance was the session-151 second fuzz catch — after two replays
        // the anchor check compared a stale basis and passed by coincidence while the
        // live model had drifted. The live vector IS the mapped vector here (the loop
        // above proved them equal), so it is stored as-is.
        e.Anchors = liveAnchors;
        // Carried forward under the index it was replayed at; taken out of the previous
        // pass's table so no second system can replay the same entry.
        e.SystemIndex = system.SystemIndex;
        _previous.Remove(foundAt);
        _current[system.SystemIndex] = e;
        LastPass = (LastPass.Replayed + 1, LastPass.Drawn);
        return true;
    }

    // Where a system's entry may sit in the previous pass's table, nearest first: its
    // own index, then one either side (a system inserted or removed before it), then two.
    private static readonly int[] NeighbourOffsets = [0, -1, 1, -2, 2];

    /// <summary>Why each system of the most recent pass did NOT replay, by system index —
    /// the memo's own account, for the nets and the output channel. Cleared per pass.</summary>
    internal List<(int SystemIndex, string Reason)> LastPassDeclines { get; } = new();

    private bool Decline(SystemLayout system, string reason)
    {
        LastPassDeclines.Add((system.SystemIndex, reason));
        return false;
    }

    /// <summary>The recorded source anchors, mapped through the edit window, must equal
    /// the live vector — the certificate that the recorded slot values ARE the live
    /// offsets (see the load-bearing remark in <see cref="TryReplay"/>). One spelling
    /// for the system and overlay entries.</summary>
    private bool AnchorsFollowWindow(int[] recorded, int[] live)
    {
        if (live.Length != recorded.Length)
            return false;
        for (int i = 0; i < live.Length; i++)
        {
            int v = recorded[i];
            if (v >= _windowPrefix)
            {
                if (v < _windowSuffixStart)
                    return false; // inside the edit window — undefined shift
                v += _windowDelta;
            }
            if (live[i] != v)
                return false;
        }
        return true;
    }

    /// <summary>Maps every recorded slot value through the edit window, all-or-nothing:
    /// false when any value lands inside the window (undefined shift — the fragment
    /// must decline). Returns the original array when nothing moved.</summary>
    private bool TryMapSlots(int[] values, out int[] final)
    {
        int[]? mapped = null;
        for (int i = 0; i < values.Length; i++)
        {
            int v = values[i];
            if (v < _windowPrefix)
                continue;
            if (v < _windowSuffixStart)
            {
                final = values;
                return false; // inside the edit window — undefined shift
            }
            (mapped ??= (int[])values.Clone())[i] = v + _windowDelta;
        }
        final = mapped ?? values;
        return true;
    }

    /// <summary>Re-assembles a recorded fragment into the page buffer: the stored text
    /// with each slot's (possibly shifted) number re-emitted at its insert position.</summary>
    private static void AppendFragment(StringBuilder sb, string text, int[] insertAt, int[] final)
    {
        int at = 0;
        for (int i = 0; i < final.Length; i++)
        {
            sb.Append(text, at, insertAt[i] - at);
            sb.Append(final[i]);
            at = insertAt[i];
        }
        sb.Append(text, at, text.Length - at);
    }

    /// <summary>Opens a capture scope around a live DrawSystem: records the emitted
    /// text and source values, and stores the entry when the capture verifies. Returns
    /// null when the system is ineligible this pass (the draw simply runs unrecorded).</summary>
    public IDisposable? BeginCapture(MultiStaffScore score, SystemLayout system,
        SvgDocumentContext host, double pageHeight)
    {
        LastPass = (LastPass.Replayed, LastPass.Drawn + 1);
        if (!_enabled || _declinedSystems?.Contains(system.SystemIndex) == true)
            return null;
        if (system.Measures.IsDefaultOrEmpty || system.Measures.Length == 0)
            return null;
        var page = host.CurrentPage;
        var sb = host.CurrentContent;
        if (page == null || sb == null)
            return null;
        return new CaptureScope(page, sb, (fragment, log, designs) =>
            Store(score, system, host, pageHeight, fragment, log, designs));
    }

    /// <summary>
    /// Replays one overlay drawer's recorded page output when the caller's value fold
    /// matches and the live anchors follow the edit window (see the class remarks'
    /// OVERLAY ENTRIES paragraph). Returns false when the drawer must run live for this
    /// page (then <see cref="BeginOverlayCapture"/> records it).
    /// </summary>
    public bool TryReplayOverlay(OverlayDrawerId drawer, int pageIndex, long valueHash,
        int[] liveAnchors, SvgDocumentContext host)
    {
        if (!_enabled)
            return false;
        if (!_overlays.TryGetValue((drawer, pageIndex), out var e)
            || e.Generation != _generation - 1
            || !_windowValid
            || e.ValueHash != valueHash)
            return false;
        if (!AnchorsFollowWindow(e.Anchors, liveAnchors))
            return false;
        if (!TryMapSlots(e.Values, out var final))
            return false;

        AppendFragment(host.CurrentContent!, e.Text, e.InsertAt, final);

        if (e.Designs != null)
            foreach (var d in e.Designs)
                host.UsedDesigns.Add(d);

        // Values and anchors advance together, same as the system replay (the stale-
        // basis hazard is identical: both are offsets in the previous render's text).
        e.Values = final;
        e.Anchors = liveAnchors;
        e.Generation = _generation;
        LastOverlayPass = (LastOverlayPass.Replayed + 1, LastOverlayPass.Drawn);
        return true;
    }

    /// <summary>Opens a capture scope around one overlay drawer's live page run, storing
    /// the entry under the caller's value fold when the capture verifies. Returns null
    /// when overlay fragments are ineligible this pass (the draw runs unrecorded).</summary>
    public IDisposable? BeginOverlayCapture(OverlayDrawerId drawer, int pageIndex,
        long valueHash, int[] liveAnchors, SvgDocumentContext host)
    {
        LastOverlayPass = (LastOverlayPass.Replayed, LastOverlayPass.Drawn + 1);
        if (!_enabled)
            return null;
        var page = host.CurrentPage;
        var sb = host.CurrentContent;
        if (page == null || sb == null)
            return null;
        return new CaptureScope(page, sb, (fragment, log, designs) =>
            StoreOverlay(drawer, pageIndex, valueHash, liveAnchors, fragment, log, designs));
    }

    private void StoreOverlay(OverlayDrawerId drawer, int pageIndex, long valueHash,
        int[] anchors, string fragment, List<int> log, HashSet<int> designs)
    {
        if (!TrySplit(fragment, log, out var text, out var insertAt, out var values))
            return; // decline: the next edit runs this drawer live on this page

        _overlays[(drawer, pageIndex)] = new OverlayEntry
        {
            ValueHash = valueHash,
            Anchors = anchors,
            Text = text,
            InsertAt = insertAt,
            Values = values,
            Designs = designs.Count > 0 ? [.. designs] : null,
            Generation = _generation,
        };
    }

    private sealed class CaptureScope : IDisposable
    {
        private readonly Action<string, List<int>, HashSet<int>> _store;
        private readonly SvgDrawingContext _page;
        private readonly StringBuilder _sb;
        private readonly int _start;
        private readonly List<int> _log = new();
        private readonly HashSet<int> _designs = new();
        private readonly List<int>? _prevLog;
        private readonly HashSet<int>? _prevDesigns;

        public CaptureScope(SvgDrawingContext page, StringBuilder sb,
            Action<string, List<int>, HashSet<int>> store)
        {
            _store = store;
            _page = page;
            _sb = sb;
            _start = sb.Length;
            _prevLog = page.SourceLog;
            _prevDesigns = page.DesignLog;
            page.SourceLog = _log;
            page.DesignLog = _designs;
        }

        public void Dispose()
        {
            _page.SourceLog = _prevLog;
            _page.DesignLog = _prevDesigns;
            _store(_sb.ToString(_start, _sb.Length - _start), _log, _designs);
        }
    }

    private void Store(MultiStaffScore score, SystemLayout system, SvgDocumentContext host,
        double pageHeight, string fragment, List<int> log, HashSet<int> designs)
    {
        // Split the fragment into segments around the data-pos/data-alt numbers and
        // verify the scan against the emission log — an exact match certifies every
        // number the replay will rewrite is one the drawing context emitted (and
        // nothing else, e.g. a lyric containing the literal attribute text).
        if (!TrySplit(fragment, log, out var text, out var insertAt, out var values))
            return; // decline: the next edit draws this system live

        _current[system.SystemIndex] = new Entry
        {
            Slice = SliceFor(system, out bool hasLeft, out bool hasRight),
            HasLeft = hasLeft,
            HasRight = hasRight,
            MeasureCount = system.Measures.Length,
            SystemIndex = system.SystemIndex,
            GeometryHash = HashGeometry(system, pageHeight),
            GeometryScalars = GeometryScalars(system, pageHeight),
            Anchors = PositionFingerprint(score, system),
            Text = text,
            InsertAt = insertAt,
            Values = values,
            Designs = designs.Count > 0 ? [.. designs] : null,
        };
    }

    /// <summary>
    /// The system's source ANCHORS in the live score: every position field DrawSystem
    /// can emit a data-pos/data-alt from within this system's scope — every staff's
    /// every voice's measure spans AND item positions (chord members' own positions
    /// included) over [first..last], plus — on the first system, where DrawSystem tags
    /// the prefix from staff/header declarations — each staff's ClefPosition and the
    /// header's key/time offsets. ONE spelling for capture and replay; replay compares
    /// the live vector against the recorded one mapped through the edit window, which
    /// is what certifies the recorded slot values ARE the live offsets (the content
    /// keys cannot: they exclude positions by design; and the measure SPANS alone
    /// cannot either — the session-151 fuzz found error-recovery measures whose spans
    /// collapse to a point while their items carry real, drifting positions).
    /// </summary>
    private static int[] PositionFingerprint(MultiStaffScore score, SystemLayout system)
    {
        int first = system.Measures[0].MeasureIndex;
        int last = system.Measures[^1].MeasureIndex;
        var anchors = new List<int>();
        bool isFirstSystem = system.SystemIndex == 0;
        if (isFirstSystem)
        {
            anchors.Add(score.Header.Key);
            anchors.Add(score.Header.Time);
        }
        foreach (var (_, staff, _) in score.EnumerateStaves())
        {
            if (isFirstSystem)
                anchors.Add(staff.ClefPosition);
            foreach (var voice in staff.Voices)
            {
                var measures = voice.Measures;
                for (int i = first; i <= last && i < measures.Length; i++)
                {
                    anchors.Add(measures[i].SourceStart);
                    anchors.Add(measures[i].SourceEnd);
                    foreach (var item in measures[i].Items)
                    {
                        anchors.Add(item.SourcePosition);
                        if (item is ChordItem chord)
                            foreach (var note in chord.Notes)
                                anchors.Add(note.SourcePosition);
                    }
                }
            }
        }
        return [.. anchors];
    }

    // ⚠️ NOT the first measure's NUMBER. It used to be compared here (and every
    // measure's number was folded into the geometry hash), and that made a bar
    // inserted or deleted before the system a guaranteed miss for every later system
    // — the same trap SystemLayoutCache fell into (session 330). The number is a
    // stamp: nothing DrawSystem emits is a function of it — the content it selects is
    // certified by the slice, the offsets by the anchors, the picture by the geometry
    // fold — so a system found under other numbers is the same text. The count
    // stays (it bounds the slice). The SYSTEM index is a stamp too, since the second
    // half of session 330: DrawSystem reads it only as "is this the first system"
    // (the prefix, the instrument names — SharedRenderer.Prefix / Noteheads), so
    // that one bit is compared and the number is not; TryReplay looks one or two
    // indices either side for the entry a `break` inserted before this system moved.
    // The mismatch names the first check that failed, for LastPassDeclines.
    private bool KeyMatches(Entry e, SystemLayout system, double pageHeight, out string mismatch)
    {
        mismatch = "";
        if (e.MeasureCount != system.Measures.Length) { mismatch = "measure count"; return false; }
        if ((e.SystemIndex == 0) != (system.SystemIndex == 0)) { mismatch = "first-system bit"; return false; }
        var slice = SliceFor(system, out bool hasLeft, out bool hasRight);
        if (e.HasLeft != hasLeft || e.HasRight != hasRight || e.Slice.Length != slice.Length)
        { mismatch = "slice shape"; return false; }
        for (int i = 0; i < slice.Length; i++)
            if (e.Slice[i] != slice[i]) { mismatch = "slice key " + i; return false; }
        if (e.GeometryHash != HashGeometry(system, pageHeight))
        {
            mismatch = "geometry: entry " + e.GeometryScalars + " vs live " + GeometryScalars(system, pageHeight);
            return false;
        }
        return true;
    }

    // The scalar half of the geometry fold spelled out, for a mismatch report.
    private static string GeometryScalars(SystemLayout system, double pageHeight)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"[first={system.SystemIndex == 0} Y={system.Y} W={system.Width} P={system.PrefixWidth} I={system.Indent} H={pageHeight} measures=");
        foreach (var m in system.Measures)
            sb.Append(CultureInfo.InvariantCulture, $"({m.X},{m.Width},{m.Items.Length}/{m.Columns.Length})");
        sb.Append(" groups=");
        if (!system.StaffGroups.IsDefaultOrEmpty)
            foreach (var g in system.StaffGroups)
            {
                sb.Append(CultureInfo.InvariantCulture, $"[{g.Type} Y={g.Y} H={g.Height}");
                foreach (var st in g.Staves)
                    sb.Append(CultureInfo.InvariantCulture, $" s{st.StaffIndex}:Y={st.Y},H={st.Height},hid={st.IsHidden}");
                sb.Append(']');
            }
        sb.Append(']');
        return sb.ToString();
    }

    private ImmutableArray<MeasureContentKey> SliceFor(
        SystemLayout system, out bool hasLeft, out bool hasRight)
    {
        int first = system.Measures[0].MeasureIndex;
        int last = system.Measures[^1].MeasureIndex;
        hasLeft = first > 0;
        hasRight = last + 1 < _keys.Length;
        int from = hasLeft ? first - 1 : first;
        int to = hasRight ? last + 1 : last;
        var builder = ImmutableArray.CreateBuilder<MeasureContentKey>(to - from + 1);
        for (int i = from; i <= to; i++)
            builder.Add(i < _keys.Length ? _keys[i] : default);
        return builder.MoveToImmutable();
    }

    // Folds every geometry value DrawSystem reads (see the class remarks' inventory).
    // Not the measure NUMBERS (see KeyMatches). system.Y is folded because in page
    // coordinates every emitted Y carries it; in a system-local frame
    // (IDocumentContext.SystemLocalFrames) the renderer hands every system the same Y,
    // and the fold sees a constant — which is exactly what lets a moved system replay.
    private static long HashGeometry(SystemLayout system, double pageHeight)
    {
        var hc = new MeasureContentKey.Hash64();
        hc.Add(system.SystemIndex == 0);   // the one bit of the index DrawSystem reads
        hc.Add(system.Y);
        hc.Add(system.Width);
        hc.Add(system.PrefixWidth);
        hc.Add(system.Indent);
        hc.Add(pageHeight);
        foreach (var m in system.Measures)
        {
            hc.Add(m.X);
            hc.Add(m.Width);
            foreach (var item in m.Items)
            {
                hc.Add(item.ItemIndex);
                hc.Add(item.X);
                hc.Add(item.Width);
            }
            if (!m.Columns.IsDefaultOrEmpty)
            {
                foreach (var c in m.Columns)
                {
                    hc.Add(c.Timing);
                    hc.Add(c.X);
                    hc.Add(c.Width);
                }
            }
            if (m.LooseChangeHangs != null)
            {
                hc.Add(m.LooseChangeHangs.Count);
                // Ordered: ImmutableDictionary enumeration order is not stable across
                // instances holding the same pairs, and the hash must be.
                foreach (var kv in m.LooseChangeHangs.OrderBy(kv => kv.Key))
                {
                    hc.Add(kv.Key);
                    hc.Add(kv.Value);
                }
            }
        }
        if (!system.StaffGroups.IsDefaultOrEmpty)
        {
            foreach (var g in system.StaffGroups)
            {
                hc.Add((int)g.Type);
                hc.Add(g.Y);
                hc.Add(g.Height);
                foreach (var st in g.Staves)
                    AddStaffLayout(ref hc, st);
                if (g.GrandStaffLayout is { } gsl)
                {
                    hc.Add(gsl.BraceX);
                    hc.Add(gsl.BraceTop);
                    hc.Add(gsl.BraceBottom);
                    hc.Add((int)gsl.DelimiterType);
                    foreach (var st in gsl.Staves)
                        AddStaffLayout(ref hc, st);
                }
            }
        }
        return hc.ToHashCode();
    }

    private static void AddStaffLayout(ref MeasureContentKey.Hash64 hc, StaffLayout st)
    {
        hc.Add(st.StaffIndex);
        hc.Add((int)st.Clef);
        hc.Add(st.Y);
        hc.Add(st.Height);
        hc.Add(st.Tuning.HasValue ? (int)st.Tuning.Value + 1 : 0);
        hc.Add(st.InstrumentName);
        hc.Add(st.IsOssia);
        hc.Add(st.IsHidden);
        hc.Add(st.StaffAffinity ?? int.MinValue);
    }

    private const string PosToken = " data-pos=\"";
    private const string AltToken = " data-alt=\"";

    // Splits `fragment` into text-with-numbers-removed + (insert position, value)
    // pairs for every data-pos / data-alt number, verifying the values reproduce the
    // emission log exactly (count AND values, in order).
    private static bool TrySplit(string fragment, List<int> log,
        out string text, out int[] insertAt, out int[] values)
    {
        var sb = new StringBuilder(fragment.Length);
        var offsets = new List<int>(log.Count);
        var found = new List<int>(log.Count);
        int i = 0;
        // Each token's NEXT occurrence at/after i, advanced lazily: −1 = none remain
        // (never searched again). Re-running both IndexOf calls from i on EVERY hit
        // made the scan O(hits × fragment) — measured (session 160): a fragment with
        // ~1500 data-pos and NO data-alt re-scanned to the end 1500 times, ~48 ms for
        // one page's capture, dwarfing the 3 ms draw it recorded.
        int p = -2, a = -2; // −2 = not yet searched
        while (i < fragment.Length)
        {
            if (p != -1 && p < i)
                p = fragment.IndexOf(PosToken, i, StringComparison.Ordinal);
            if (a != -1 && a < i)
                a = fragment.IndexOf(AltToken, i, StringComparison.Ordinal);
            int next = p < 0 ? a : a < 0 ? p : Math.Min(p, a);
            if (next < 0)
            {
                sb.Append(fragment, i, fragment.Length - i);
                break;
            }
            bool isAlt = next == a && (p < 0 || a <= p);
            int tokenLen = PosToken.Length; // both tokens are the same length
            int copyTo = next + tokenLen;
            sb.Append(fragment, i, copyTo - i);
            i = copyTo;
            // Parse one int (data-pos) or a space-separated list (data-alt), removing
            // the digits from the text and recording each number's insert position.
            bool first = true;
            while (i < fragment.Length)
            {
                if (!first)
                {
                    if (fragment[i] != ' ')
                        break;
                    sb.Append(' ');
                    i++;
                }
                int numStart = i;
                if (i < fragment.Length && fragment[i] == '-')
                    i++;
                while (i < fragment.Length && fragment[i] is >= '0' and <= '9')
                    i++;
                if (i == numStart)
                {
                    // No digits where a number belongs — not our emission; decline.
                    text = "";
                    insertAt = values = [];
                    return false;
                }
                offsets.Add(sb.Length);
                found.Add(int.Parse(fragment.AsSpan(numStart, i - numStart),
                    provider: System.Globalization.CultureInfo.InvariantCulture));
                first = false;
                if (!isAlt)
                    break;
            }
        }

        if (found.Count != log.Count)
        {
            text = "";
            insertAt = values = [];
            return false;
        }
        for (int k = 0; k < found.Count; k++)
        {
            if (found[k] != log[k])
            {
                text = "";
                insertAt = values = [];
                return false;
            }
        }
        text = sb.ToString();
        insertAt = [.. offsets];
        values = [.. found];
        return true;
    }
}
