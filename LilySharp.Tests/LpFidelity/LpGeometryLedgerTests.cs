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

using System.Linq;
using System.Text;
using System.Text.Json;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using Xunit;

namespace LilySharp.Tests.LpFidelity;

/// <summary>
/// Holds Lily#'s geometry against measurements taken from real LilyPond.
/// </summary>
/// <remarks>
/// <para>
/// This is NOT a snapshot test. A snapshot pins Lily# to its own previous output, so a wrong
/// value that has been blessed once stays wrong and looks green forever. This pins Lily# to
/// LilyPond, and states the remaining difference as a number with a named cause.
/// </para>
/// <para>
/// Why it exists: LilyPond measurements used to be taken ad hoc into a scratch directory and
/// thrown away, so every session re-derived the same numbers — and one of them
/// (a bar-line optical correction attributed to a clef rather than to a stem direction) was
/// carried forward wrong through several handoffs because nothing held it. Committing the
/// probe sources and the measured values makes the corpus grow instead of evaporate.
/// </para>
/// <para>
/// To add an entry: write the .ly probe under audit/lp-geometry/probes/, add the matching
/// Lily# probe to <see cref="LpGeometryProbes"/>, run the probe script (see that directory's
/// README) and paste the LilyPond number into lp-geometry.json. Run this test; it will tell
/// you the residual to record.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class LpGeometryLedgerTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public LpGeometryLedgerTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    /// <param name="Unit">
    /// What the numbers are measured in. Defaults to <c>"ss"</c> (staff spaces); an entry
    /// that counts things says so, and is kept OUT of the staff-space headline total.
    /// Adding a count of 1 to a total of 0.023777 staff spaces would not be a worse number,
    /// it would be a meaningless one — the same reason page.height was left out entirely
    /// (see the remarks in LpGeometryProbes).
    /// </param>
    private sealed record LedgerEntry(
        double LilyPond, double? Residual, string Why, string Probe, string Score,
        string Unit = "ss");

    private static readonly Lazy<(IReadOnlyDictionary<string, LedgerEntry> Entries, double Tolerance)> Ledger =
        new(LoadLedger);

    private static string LedgerPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "audit", "lp-geometry", "lp-geometry.json");
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException(
            "audit/lp-geometry/lp-geometry.json not found above " + AppContext.BaseDirectory);
    }

    private static (IReadOnlyDictionary<string, LedgerEntry>, double) LoadLedger()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(LedgerPath()));
        var root = doc.RootElement;
        double tolerance = root.GetProperty("tolerance").GetDouble();
        var entries = new Dictionary<string, LedgerEntry>();
        foreach (var e in root.GetProperty("entries").EnumerateObject())
        {
            var v = e.Value;
            double? residual = v.TryGetProperty("residual", out var r) && r.ValueKind != JsonValueKind.Null
                ? r.GetDouble()
                : null;
            entries[e.Name] = new LedgerEntry(
                v.GetProperty("lilypond").GetDouble(),
                residual,
                v.TryGetProperty("why", out var w) ? (w.GetString() ?? "") : "",
                v.TryGetProperty("probe", out var p) ? (p.GetString() ?? "") : "",
                v.TryGetProperty("score", out var s) ? (s.GetString() ?? "") : "",
                v.TryGetProperty("unit", out var u) ? (u.GetString() ?? "ss") : "ss");
        }
        return (entries, tolerance);
    }

    public static TheoryData<string> ProbeIds()
    {
        var data = new TheoryData<string>();
        foreach (var probe in LpGeometryProbes.All)
            data.Add(probe.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(ProbeIds))]
    public void Geometry_MatchesLilyPondWithinTheRecordedResidual(string id)
    {
        var probe = LpGeometryProbes.All.Single(p => p.Id == id);
        var (entries, tolerance) = Ledger.Value;

        Assert.True(entries.ContainsKey(id),
            $"'{id}' has no entry in audit/lp-geometry/lp-geometry.json. "
            + "Every probe must carry a LilyPond measurement — add one rather than "
            + "letting the probe assert nothing.");
        var entry = entries[id];

        var geometry = RenderedGeometry.Render(probe.Source, probe.Options);
        double actual = probe.Measure(geometry);
        double actualResidual = actual - entry.LilyPond;

        if (entry.Residual is null)
        {
            Assert.Fail(
                $"'{id}' has no recorded residual yet.\n"
                + $"  LilyPond  {entry.LilyPond:F6}\n"
                + $"  Lily#     {actual:F9}\n"
                + $"  residual  {actualResidual:F9}\n"
                + "Record that residual in lp-geometry.json. If it is not 0, the 'why' must "
                + "name the defect that accounts for it.\n"
                + "Drawn geometry:\n" + geometry.Describe());
            return;
        }

        double expected = entry.Residual.Value;

        if (Math.Abs(expected) > tolerance && string.IsNullOrWhiteSpace(entry.Why))
        {
            Assert.Fail(
                $"'{id}' records a non-zero residual ({expected:F6}) with no 'why'.\n"
                + "An unexplained gap from LilyPond is an open bug, not a baseline. Name the "
                + "cause, or fix it and set the residual to 0.");
        }

        double drift = actualResidual - expected;
        if (Math.Abs(drift) <= tolerance)
            return;

        string verdict = Math.Abs(actualResidual) > Math.Abs(expected)
            ? "MOVED AWAY FROM LilyPond (regression)"
            : "MOVED TOWARD LilyPond — good, but record it so the improvement is in the diff";

        Assert.Fail(
            $"'{id}' {verdict}.\n"
            + $"  LilyPond           {entry.LilyPond:F6}   (probe {entry.Probe}, score {entry.Score})\n"
            + $"  Lily#              {actual:F9}\n"
            + $"  residual now       {actualResidual:F9}\n"
            + $"  residual recorded  {expected:F9}\n"
            + $"  drift              {drift:F9}\n"
            + (string.IsNullOrWhiteSpace(entry.Why) ? "" : $"  recorded cause     {entry.Why}\n")
            + "Drawn geometry:\n" + geometry.Describe());
    }

    /// <summary>
    /// A chord symbol is drawn anchored at its ink LEFT, standing ON its column — LilyPond's
    /// ChordName declares no <c>X-offset</c> and no <c>self-alignment-interface</c> at all
    /// (scm/define-grobs.scm:837-855), so the grob's reference point IS its ink left. MEASURED
    /// (audit/lp-geometry/probes/staffless-system.ly): the ChordName anchor equals its
    /// column's X to six digits in every score of that probe.
    /// </summary>
    /// <remarks>
    /// ⚠️ This exists because NO ledger point can see this. Every <c>staffless.*</c> point is a
    /// DIFFERENCE of two anchors read the same way on each side — built that way on purpose,
    /// so the convention cancels out of them. Centring the symbol again would leave all four
    /// of them exact and only <c>chords-vs-staff</c> would drift, and then only because the
    /// keep-inside-line rod happens to make a staff-LESS line's first column visible. On a
    /// staff, the corpus would not notice at all. So the convention is asserted directly.
    /// </remarks>
    [Fact]
    public void ChordSymbolsAreAnchoredAtTheirInkLeft()
    {
        // A chord symbol over the first note of an ordinary staff: the note's column is
        // where LilyPond stands both grobs (probe CS dumps ChordName and NoteHead at
        // 8.585000 alike).
        var geometry = RenderedGeometry.Render("""
            octave absolute
            time 4/4
            key c major

            part melody { clef treble }

            section Main {
              melody { c2 a | f2 g | c1 | }
              chords prog { c2 a:m | f2 g:7 | c1 | }
            }

            form main { ~Main }

            score main "anchor" {
              staff melody with chords prog
            }
            """);

        var symbols = geometry.ChordSymbols;
        Assert.NotEmpty(symbols);
        Assert.All(symbols, t => Assert.Equal(
            LilySharp.Core.Rendering.TextAnchor.Start, t.Anchor));

        // …and the anchor really is the note's column, not merely a left-anchored point
        // somewhere near it.
        Assert.Equal(geometry.NoteheadAnchor(0), symbols[0].X, 6);
    }

    /// <summary>
    /// Every grob that <c>self-alignment-X = CENTER</c>s on a note column stands on ONE
    /// centre — the head's own INK centre — so a script and the dynamic beneath it are drawn
    /// at the same X to twelve digits.
    /// </summary>
    /// <remarks>
    /// MEASURED (audit/lp-geometry/probes/dynamic-support.ly, book DSK, with NoteHead added
    /// to the dump). LilyPond puts all three on one number:
    /// <code>
    ///   NoteHead     x=(8.7034 . 10.6654)                            ink centre 9.6844
    ///   Script       x=(9.4844 . 9.8844)                             ink centre 9.6844
    ///   DynamicText  x=(9.052748818897637 . 10.316051181102361)      ink centre 9.6844
    /// </code>
    /// The head's extent is its INK (width 1.9620) and not its advance (1.960) — six digits,
    /// and confirmed on DSQ's black head too (1.3042 against 1.304).
    /// <para>
    /// ⚠️ THIS EXISTS BECAUSE NO LEDGER POINT CAN SEE IT. Every point on this island is a
    /// vertical reading, and a horizontal disagreement between two grobs only surfaces there
    /// after being multiplied by whatever slope the label's outline happens to have under
    /// that particular obstacle — which is why the same 0.001 showed up as +0.000903 under a
    /// staccato and +0.001396 under a marcato and was read for two sessions as "the two
    /// scripts' profiles". Asserted directly, in the axis it lives in.
    /// </para>
    /// ⚠️ THE POSITIVE CONTROL IS THE SECOND ASSERT: the shared centre must ALSO not be the
    /// advance's. Without it this test passes on the defect it was written for — both grobs
    /// reading <c>advance/2</c> agree with each other perfectly.
    /// </remarks>
    [Fact]
    public void AScriptAndTheDynamicBeneathItStandOnTheHeadsInkCentre()
    {
        // DSK: a whole note carrying both a below staccato and a \f — the book where the ink
        // and the advance differ by the most (1.962 against 1.960).
        var geometry = RenderedGeometry.Render("""
            octave absolute
            time 4/4
            key c major

            part upper { clef treble }

            section Main {
              upper { a,1@staccato.down@f | }
            }

            form main { ~Main }

            score main "DSK-anchor" {
              staff upper
            }
            """);

        var dot = Assert.Single(geometry.Glyphs.Where(
            g => g.Glyph == EmmentalerGlyphs.ArticStaccatoAbove));
        var label = Assert.Single(geometry.Texts.Where(t => t.Text == "f"));

        // One head, one centre — the invariant LilyPond's dump shows as an identity.
        Assert.Equal(dot.X, label.X, 12);

        // …and that shared centre is the head's INK centre, not half its advance. The two
        // candidates are 0.981 and 0.980; a test that only compared the two grobs to each
        // other would accept either.
        double column = geometry.NoteheadAnchor(0);
        Assert.Equal(column + GlyphMetrics.NoteheadWhole.CenterX, label.X, 12);
        Assert.NotEqual(column + GlyphMetrics.NoteheadWholeAdvance / 2.0, label.X, 6);
    }

    /// <summary>
    /// An independent lyrics ROW is spaced like the Lyrics context it is — the SAME distance
    /// from its staff as the note-bound spelling of the same line.
    /// </summary>
    /// <remarks>
    /// MEASURED (audit/lp-geometry/probes/page-vertical.ly, books LYRC and LYRR): LilyPond
    /// reads the two spellings IDENTICALLY — every figure the probe prints agrees, the
    /// staff-to-loose-line distance included at 5.500000 — because a Lyrics context is a
    /// Lyrics context and <c>\lyricsto</c> decides which column a syllable stands on, not
    /// what spring holds the line (ly/engraver-init.ly:648-652). So LilyPond's side of the
    /// comparison is an identity, and the whole difference is Lily#'s.
    /// <para>
    /// ★ THIS TEST USED TO ASSERT THE OPPOSITE, and the history is the point. Lily# placed an
    /// independent row as a staff-like BAND — a lead sheet's word TRACK rather than a staff's
    /// lyrics — which put it 4.100000 further from the staff than LilyPond puts a Lyrics line
    /// (9.600000 against 5.500000, derived as <c>2.0 + (9.0 - 4.0) + 2.6</c>). That was a
    /// DECIDED divergence, recorded in HANDOFF 3 and asserted here rather than carried in the
    /// ledger so a decision would not enter the headline total. It was revisited on
    /// 2026-07-27 and the band is gone: the row is spaced by
    /// <c>nonstaff-relatedstaff-spacing</c> off its own ink, like the Lyrics context it is.
    /// </para>
    /// <para>
    /// ⚠️ WHAT MAKES THE CLAIM STRONG IS THE IDENTITY, not the number. LilyPond reads the two
    /// spellings identically — measured, and not by eye: books LYRC/LYRR and LYRV/LYRRV dump
    /// line for line the same figures, because <c>\lyricsto</c> decides which COLUMN a
    /// syllable stands on and not what spring holds the line. So the two Lily# readings
    /// agreeing is LilyPond's own answer reproduced, and a test that pinned only the row
    /// would still pass if both spellings drifted together.
    /// </para>
    /// <para>
    /// The distance itself is a ledger point now that it is no longer a decision
    /// (<c>lyrics.row.staff-to-lyric</c>), which is why 5.500000 is asserted here only as the
    /// shared value — this test is about the two spellings agreeing.
    /// </para>
    /// </remarks>
    [Fact]
    public void LyricRowIsSpacedLikeTheLyricsContextItIs()
    {
        double noteBound = RenderedGeometry
            .Render(LpGeometryProbes.LyricNoteBoundScore, LpGeometryProbes.LyricRowOptions)
            .FirstStaffToLyricBaseline();
        double row = RenderedGeometry
            .Render(LpGeometryProbes.LYRR, LpGeometryProbes.LyricRowOptions)
            .FirstStaffToLyricBaseline();

        Assert.Equal(5.5, noteBound, 6);
        Assert.Equal(noteBound, row, 6);
    }

    /// <summary>
    /// An independent lyrics ROW reserves its ink against the NEXT system, so its last verse
    /// cannot be drawn over that system's staff.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS FAILED BEFORE 2026-07-27 AND THE FAILURE WAS VISIBLE ON THE PAGE: a two-verse
    /// row put verse 2's baseline 12.800000 below its staff refpoint while the next system's
    /// refpoint sat 12.000000 below, so every row of this shape printed straight across the
    /// following system's staff and noteheads. Nothing caught it. No committed fixture reaches
    /// the regime (rows-song-sheet is one verse and staffless), and the corpus entry that
    /// covers the quantity — the system gap — read 12.000000, EXACT against LilyPond, because
    /// the row's ink reached no figure the inter-system spring is floored by. HANDOFF 5.2.1
    /// (4): a reading can be exact and blind at the same time.
    /// <para>
    /// ★ ASSERTED AS A RULE, NOT A VALUE (HANDOFF 5.4). The distance itself is not a number
    /// worth pinning: it carries HANDOFF 3's decided band placement, so it would move with any
    /// revision of that decision and would fail for a reason that is not a defect. What must
    /// hold under every such revision is the CLEARANCE — the row's deepest ink stays above the
    /// next system's staff — and that is font-free and decision-free.
    /// </para>
    /// <para>
    /// ⚠️ THE READINGS ARE TAKEN FROM PAGE 1'S INTERIOR, where a system really does have a
    /// next one. The last system on a page closes on the page edge instead, which is a
    /// different spring and would pass this trivially.
    /// </para>
    /// </remarks>
    [Fact]
    public void LyricRowReservesItsInkAgainstTheNextSystem()
    {
        var geometry = RenderedGeometry
            .Render(LpGeometryProbes.LYRRV, LpGeometryProbes.LyricRowOptions);

        // The regime this reads in: four systems on page 1, so system 0 has a successor.
        Assert.Equal(4, geometry.StavesOnPage(0));

        double staffToNextStaff = geometry.StaffGapAt(0, 0);
        double staffToVerse1 = geometry.LyricBaselineBelowStaff(0, 0);
        double verseStep = geometry.LyricVerseStep();

        // Verse 2's BASELINE, and then its descenders, have to finish above the next staff.
        // Any positive slack proves the reservation happened; the size of it is the band
        // decision's and is deliberately not asserted.
        double lastVerseBaseline = staffToVerse1 + verseStep;
        Assert.True(lastVerseBaseline < staffToNextStaff,
            $"the row's last verse sits {lastVerseBaseline:F6} below its staff refpoint while "
            + $"the next system's is only {staffToNextStaff:F6} below — it is drawn over that "
            + "system's staff.");
    }

    /// <summary>
    /// An independent lyrics ROW is spaced as the Lyrics contexts it is: Lily# reads the row
    /// spelling and the note-bound spelling of the SAME music identically, on every system of
    /// a page whose loose chain is critically compressed.
    /// </summary>
    /// <remarks>
    /// ⚠️ THIS IS THE PORT'S TEST, NOT THE LEDGER ENTRY. <c>lyrics.row.two-verse.verse-step</c>
    /// pins one number (2.800000), and a port that wrote that number as a constant would pass
    /// it — which is what the entry spent a session warning about. What cannot be faked is the
    /// IDENTITY: books LYRV and LYRRV differ only in whether the two verses are two Lyrics
    /// contexts or one unassociated row, LilyPond reads them line for line the same (measured
    /// as whole dumps, all 59 lines), so its side of the comparison is a constant and any
    /// difference Lily# shows between its two spellings is entirely Lily#'s.
    /// <para>
    /// ⚠️ AND IT IS FONT-FREE, which the ledger entries on this book are not: both sides are
    /// Lily#'s own engraver, so the ~27% lyric-face difference that leaves +0.271310 in the
    /// ledger cancels identically here. This test therefore keeps working if the lyric face is
    /// ever changed.
    /// </para>
    /// <para>
    /// ⚠️ THE REGIME IS ASSERTED FIRST (HANDOFF 5.0, trap 8): four systems on page 1 and a
    /// compressed chain. With room to spare every spring sits at its ideal and the two
    /// spellings would agree at 5.500000 whatever the row model was — the same trap
    /// <c>lyrics.row.staff-to-lyric</c> (book LYRR, exact since before the port) sits in.
    /// </para>
    /// </remarks>
    [Fact]
    public void LyricRowIsSolvedLikeTheLyricsContextsItIs()
    {
        var options = LpGeometryProbes.LyricRowOptions;
        var row = RenderedGeometry.Render(LpGeometryProbes.LYRRV, options);
        var noteBound = RenderedGeometry.Render(LpGeometryProbes.LyricVerseScore, options);

        // The regime: page 1 holds four systems, so systems 0..2 have a successor and their
        // chains close on it rather than on the page edge.
        Assert.Equal(4, noteBound.StavesOnPage(0));
        Assert.Equal(4, row.StavesOnPage(0));

        // ...and it really is compressed: a chain with slack leaves the first spring at its
        // ideal, and this one is pulled below it.
        const double idealAtForceZero = 5.5;   // LyricParameters.RelatedStaffBasicDistance
        Assert.True(noteBound.LyricBaselineBelowStaff(0, 0) < idealAtForceZero,
            "the note-bound chain is not compressed, so this pair measures nothing.");

        for (int system = 0; system < 3; system++)
        {
            Assert.Equal(
                noteBound.LyricBaselineBelowStaff(system, 0),
                row.LyricBaselineBelowStaff(system, 0), 6);
            Assert.Equal(
                noteBound.StaffGapAt(system, 0), row.StaffGapAt(system, 0), 6);
        }
        Assert.Equal(noteBound.LyricVerseStep(), row.LyricVerseStep(), 6);
    }

    /// <summary>
    /// A note-bound line and an independent ROW under the SAME staff are ONE run of the
    /// alignment: the row is stepped off the line above it exactly as a second verse would be.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:919-925 — everything non-spaceable between
    /// two spaceable lines goes into ONE <c>loose_lines</c> vector and is distributed by ONE
    /// solve. A Lyrics context is non-spaceable whether or not it was <c>\lyricsto</c>
    /// anything, so it cannot end a run; the two spellings are the same alignment.
    /// <para>
    /// ⚠️ THIS IS A REGRESSION TEST WITH A SHIPPED FAILURE BEHIND IT. Lily# makes an
    /// independent row a staff GROUP of its own, so the staff carrying the note-bound line
    /// stopped being in the last group, its syllables went to the INTER-GROUP chain, and the
    /// row was solved below the system — two chains sharing one room. Both landed on the same
    /// basic-distance and the two lines were drawn ON TOP OF EACH OTHER. Nothing caught it:
    /// no fixture pairs the two spellings, and every ledger book has one or the other.
    /// </para>
    /// <para>
    /// ★ ASSERTED AS AN IDENTITY rather than as the 2.800000 step (HANDOFF 5.4). The twin
    /// spells the second line note-bound, so LilyPond reads the two books the same and every
    /// difference Lily# shows is its own; and because both sides are Lily#'s own engraver the
    /// lyric face cancels. A port that produced the right STEP by some other route would still
    /// have to produce the right FIRST distance to pass this.
    /// </para>
    /// </remarks>
    [Fact]
    public void RowUnderANoteBoundLine_IsOneRunWithIt_NotASecondChain()
    {
        var options = LpGeometryProbes.LyricRowOptions;
        var row = RenderedGeometry.Render(LpGeometryProbes.RowUnderNoteBoundScore, options);
        var bound = RenderedGeometry.Render(LpGeometryProbes.NoteBoundVersesScore, options);

        // ⚠️ AN INTERIOR SYSTEM, and the regime is asserted rather than assumed (HANDOFF 5.0,
        // trap 8). The LAST system of a page closes its chain on the PAGE EDGE, and on a
        // content-sized sheet that edge has no slack, so the block compresses to its floor —
        // a named divergence of its own (HANDOFF 1's next-steps list) that has nothing to do
        // with this one. On an interior system the chain closes on the next system's staff and
        // both books get the same room.
        Assert.True(bound.StavesOnPage(0) >= 2,
            $"the twin put {bound.StavesOnPage(0)} system(s) on page 1, so there is no "
            + "interior system to read and the page-edge chain would be measured instead.");
        Assert.True(row.StavesOnPage(0) >= 2,
            $"the row book put {row.StavesOnPage(0)} system(s) on page 1.");

        double boundFirst = bound.LyricBaselineBelowStaff(0, 0);
        double rowFirst = row.LyricBaselineBelowStaff(0, 0);
        double boundStep = bound.LyricVerseStep();
        double rowStep = row.LyricVerseStep();

        // The failure that shipped: two chains solved into one room put both lines on the
        // same baseline, so the step collapsed to zero and they printed on top of each other.
        Assert.True(rowStep > 1e-6,
            $"the row and the note-bound line are on the same baseline ({rowFirst:F6} below "
            + "the staff) — they are being solved as two chains into one room.");

        // The step is the run's own spring — the UPPER line's nonstaff-nonstaff-spacing
        // (page-layout-problem.cc:1315-1332) — and it must be the step the twin gets, because
        // to LilyPond the two books ARE the same two Lyrics contexts. Compared against the
        // twin rather than against 2.800000 so that no literal can satisfy it and so that the
        // lyric face cancels.
        Assert.Equal(boundStep, rowStep, 6);
        Assert.True(rowStep >= 2.8 - 1e-9,
            $"the step is {rowStep:F6}, under the nonstaff-nonstaff minimum 2.800000.");

        // ⚠️ THE FIRST DISTANCE IS *NOT* ASSERTED EQUAL, and the reason is a divergence this
        // test must not be made to hide: Lily# still gives the row a BAND in the system's
        // height (MultiStaffLayouter.GetStaffHeight — HANDOFF 1's next-steps list), and here
        // that band is placed BELOW the note-bound block, so the row book's system is taller,
        // its inter-system gap larger, and its chain less compressed. MEASURED: gap 15.823600
        // against the twin's 12.207200, first distance 5.500000 (the ideal) against 4.009200
        // (the floor). ⚠️ WHEN THE BAND GOES, THIS BECOMES Assert.Equal AND MUST BE MADE ONE —
        // the two spellings are one alignment in LilyPond and the whole point of the pair is
        // that Lily# should not be able to tell them apart.
        Assert.True(rowFirst >= boundFirst - 1e-9,
            $"the row book put its first line {rowFirst:F6} below the staff, ABOVE the twin's "
            + $"{boundFirst:F6} — the band can only ever give the chain more room, so this is "
            + "a different defect from the one this test documents.");
    }

    /// <summary>
    /// A lyric block BETWEEN two staves of one system is SOLVED into the room those two
    /// staves leave, not laid out at force 0 — asserted as a rule, by perturbing the block
    /// and requiring the answer to follow.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:923-925 + :936-939 — the block closes on the
    /// next spaceable staff of the same system, with no null line, and the room is the same
    /// reference-point-to-reference-point span every other block is solved into.
    /// <para>
    /// ⚠️ WHY A RULE AND NOT A VALUE (HANDOFF 5.4): the two positions ARE ledger points, and
    /// a ledger point pins a number. What it cannot pin is that the number came from a solve
    /// — a port that reverted to force 0 would put both readings at
    /// <c>RelatedStaffBasicDistance</c> and each entry would fail with a plausible residual
    /// of its own, giving no reason to look at the mechanism. The perturbation here is the
    /// SECOND VERSE: it changes the block's own floor, so a solved chain answers differently
    /// for the first spring and a force-0 one cannot, since every spring there is
    /// <c>max(min, ideal)</c> and the ideal 5.5 is above both floors.
    /// </para>
    /// <para>
    /// MEASURED, and both directions matter: one verse leaves the block's floor under the
    /// staff spring's ideal so the chain compresses but does not bottom out (4.452000, above
    /// its own floor), two verses raise the floor past it so every spring sits on its minimum
    /// (4.009200, exactly the floor). ⚠️ Those two facts BOUND THE ROOM FROM BOTH SIDES —
    /// too small a room would pin the one-verse reading to the floor as well, too large a one
    /// would leave the two-verse reading above it — which is why the frame conversion at the
    /// far end of the chain is checked here and not only in the ledger.
    /// </para>
    /// </remarks>
    [Fact]
    public void BetweenStavesLyricBlock_IsSolvedIntoTheRoom_NotLeftAtForceZero()
    {
        var options = LpGeometryProbes.LyricRowOptions;
        double oneVerse = RenderedGeometry
            .Render(LpGeometryProbes.BetweenStavesOneVerseScore, options)
            .LyricBaselineBelowStaff(0);
        double twoVerse = RenderedGeometry
            .Render(LpGeometryProbes.BetweenStavesTwoVerseScore, options)
            .LyricBaselineBelowStaff(0);

        // A chain at force 0 puts spring 1 at max(min, ideal) and the ideal wins on both
        // books, so a port that does not solve reads exactly this on each of them.
        const double idealAtForceZero = 5.5;   // LyricParameters.RelatedStaffBasicDistance
        Assert.True(oneVerse < idealAtForceZero,
            $"one verse: {oneVerse:F6} — the chain is not being solved at all.");
        Assert.True(twoVerse < idealAtForceZero,
            $"two verses: {twoVerse:F6} — the chain is not being solved at all.");

        // ...and the perturbation itself: a second verse takes room from the first spring.
        Assert.True(twoVerse < oneVerse,
            $"a second verse did not move the first spring ({oneVerse:F6} -> {twoVerse:F6}), "
            + "so the block is not being squeezed into a room it shares.");

        // ⚠️ THE THREE TOGETHER BOUND THE ROOM FROM BOTH SIDES, which no one of them does:
        // a room that is too small pins BOTH readings to the block's floor (the third
        // assertion fails, since the floors differ only by the second verse's own step being
        // absent from the first book — they are the same first spring), and a room that is
        // too large leaves BOTH at the ideal (the first two fail). Neither failure mode is
        // reachable while all three hold.
    }

    /// <summary>
    /// The distance between two staves of a system is decided by the PAGE's spring chain,
    /// not fixed at the alignment minimum — asserted as a mechanism, on the two probes the
    /// ledger measures it with.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:651-720 — <c>append_system</c> pushes one
    /// spring per spaceable staff pair into the same chain as the system springs.
    /// <para>
    /// ⚠️ Why this exists next to the ledger points rather than instead of them: a residual
    /// is a number and can shrink for the wrong reason. This asserts the two halves that
    /// make it the right one — the ragged page sits EXACTLY on the alignment minimum (so
    /// force 0 is unchanged from before the spring existed, which is what keeps every
    /// single-page score byte-identical), and the justified page opens WIDER on the same
    /// music and the same paper. Before the port the two numbers were equal, and this test
    /// fails on that.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheStavesOfASystem_AreSpacedByThePageChain_NotPinnedAtTheAlignmentMinimum()
    {
        var natural = LpGeometryProbes.All.Single(p => p.Id == "page.natural.staff-staff-inside");
        var stretched = LpGeometryProbes.All.Single(p => p.Id == "page.stretched.staff-staff-inside");

        double raggedGap = RenderedGeometry.Render(natural.Source, natural.Options).StaffGapAt(0);
        double justifiedGap = RenderedGeometry.Render(stretched.Source, stretched.Options).StaffGapAt(0);

        // staff-staff-spacing's basic-distance, which is what this music's own skyline loses
        // to (see the probe's remarks): the ragged page must be exactly there.
        Assert.Equal(9.0, raggedGap, 9);
        Assert.True(justifiedGap > raggedGap + 0.4,
            $"the justified page must stretch its staves apart: ragged {raggedGap:F6}, "
            + $"justified {justifiedGap:F6}");
    }

    /// <summary>
    /// An OSSIA pair is spaced by the SPEC, in the page's staff spaces — the ossia's own
    /// scale does not enter the distance. Asserted as a rule by perturbing the spec, not as
    /// the one number the ledger records.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/align-interface.cc:240-267 — the distance between two elements of a
    /// VerticalAlignment is accumulated between their REFERENCE POINTS, and nothing in it
    /// reads an element's own staff-space. <c>magstep</c> scales the STAFF
    /// (ly/music-functions-init.ly <c>magnifyStaff</c>), never the alignment's units.
    /// <para>
    /// ⚠️ WHY THIS EXISTS NEXT TO THE LEDGER POINT RATHER THAN INSTEAD OF IT (HANDOFF 5.4):
    /// <c>staff.ossia-pair.staff-staff-inside</c> pins ONE number, and a literal 9.0 written
    /// into the layouter would pass it. This drives the spec's basic-distance to a second
    /// value and requires the pair to follow with COEFFICIENT 1. The implementation this
    /// replaced — <c>interGroupGap *= OssiaScaleFactor</c> — follows with 0.7071 instead and
    /// fails here at both values (14 would land at 10.9, not 14), which is the point: the
    /// invention cannot come back without the machine saying so.
    /// </para>
    /// <para>
    /// ⚠️ THE CONTROL IS DRIVEN TOO, in the same run and off the same spec. A change that
    /// moved BOTH halves would be changing staff-staff-spacing rather than the ossia, and
    /// this asks the pair to stay an identity under perturbation — which is what LilyPond's
    /// own OSSU/OSSUN pair is.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(9.0)]
    [InlineData(14.0)]
    public void AnOssiaPair_IsSpacedByTheSpec_NotByTheOssiasOwnScale(double basicDistance)
    {
        var sp = LayoutOptions.Default.StaffSpacing;
        var options = LayoutOptions.Default with
        {
            StaffSpacing = sp with
            {
                DefaultStaffStaff = sp.DefaultStaffStaff with { BasicDistance = basicDistance },
                StaffStaff = sp.StaffStaff with { BasicDistance = basicDistance },
            },
        };

        double ossiaGap = RenderedGeometry
            .Render(LpGeometryProbes.OssiaPairScoreOssia, options)
            .ScaledStaffRefpointGap(5, EngravingDefaults.OssiaScale, 5);
        double controlGap = RenderedGeometry
            .Render(LpGeometryProbes.OssiaPairScoreNotation, options)
            .StaffRefpointGap(5, 5);

        Assert.Equal(basicDistance, ossiaGap, 9);
        Assert.Equal(controlGap, ossiaGap, 9);
    }

    /// <summary>
    /// On a page that must squeeze, an ossia pair is SOLVED by the page like any spaceable
    /// pair — the same one-force law its notation control obeys — and the page reserves its
    /// head for the ossia that LEADS a system.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/page-layout-problem.cc:1173-1177 is_spaceable — a line is spaceable
    /// exactly when it declares no <c>staff-affinity</c>, and an ossia is a <c>\new Staff</c>
    /// that declares none. Everything below follows from that one fact.
    /// <para>
    /// ⚠️ WHY A LAW AND NOT THE FOUR NUMBERS (HANDOFF 5.4). Books OSSK/OSSKN already pin four
    /// residuals each in the ledger, and a literal could pass them; what no literal passes is
    /// the RELATION between them. Every gap on a page is its spring's length at ONE force, so
    /// a spring's deficit from its ideal is that force times its own compress strength —
    /// <c>ideal - minimum-distance</c>, which is 1 for the ungrouped staff spring (9 - 8) and
    /// 4 for the system spring (12 - 8). The system gap must therefore fall short by exactly
    /// FOUR times what the staff gap falls short by, on both halves of the pair.
    /// </para>
    /// <para>
    /// ⚠️ THE RATIO CATCHES BOTH HALVES OF THE PORT, which is why it is asserted rather than
    /// the compression alone. Leave the spring out and the pair is RIGID: it prints its ideal
    /// 9.000000 whatever the page does, the deficit is 0 and the ratio is undefined. Put the
    /// spring in but keep the pair on the GROUPER's spec (<c>staff-staff-spacing</c>, minimum
    /// 7) and it compresses at strength 2 instead of 1, so the ratio comes out 2 and not 4.
    /// Both specs declare basic-distance 9, so nothing measured at REST can tell them apart —
    /// this is the reading that can (measured 2026-07-28: the pair 8.785507, the system
    /// 11.142030, against LilyPond's 8.787816 / 11.151264).
    /// </para>
    /// <para>
    /// ⚠️ AND THE HEAD, because a chain is only solved as well as the end it hangs from. A
    /// system LED by an ossia must start BELOW the top margin like any other. Before this
    /// port the ossia was not spaceable, so the page anchored on the staff it decorates and
    /// drew the ossia 2.123312 ABOVE the margin — the defect
    /// <c>page.ossia-pair.compressed.first-staff-refpoint</c> recorded at -6.850208, and the
    /// one that showed as a section mark flush against the paper edge in test/ossia.
    /// ⚠️ THE TWO HEADS ARE NOT ASSERTED EQUAL: LilyPond keeps them 0.049104 apart (it
    /// compresses the top spring past its floor and the ossia's smaller ink lets it go
    /// further), while Lily# currently sits on that floor on both books. That remainder is
    /// the ledger's +0.073104 against the control's +0.024000 and is named there, so this
    /// asks only for the same ORDER of head, not one number.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnOssiaIsSpaceable_SoThePageSolvesItsPairAndReservesItsHead()
    {
        var ossia = LpGeometryProbes.All.First(
            p => p.Id == "staff.ossia-pair.compressed.staff-staff-inside");
        var control = LpGeometryProbes.All.First(
            p => p.Id == "staff.ossia-control.compressed.staff-staff-inside");
        var options = ossia.Options ?? LayoutOptions.Default;
        Assert.Same(options, control.Options);

        var ossiaPage = RenderedGeometry.Render(ossia.Source, options);
        var controlPage = RenderedGeometry.Render(control.Source, options);

        double ossiaInside = ossiaPage.ScaledPairGapAt(0, 5, EngravingDefaults.OssiaScale, 5);
        double ossiaSystem = ossiaPage.ScaledPairGapAt(1, 5, EngravingDefaults.OssiaScale, 5);
        double controlInside = controlPage.ScaledPairGapAt(0, 5, 1.0, 5);
        double controlSystem = controlPage.ScaledPairGapAt(1, 5, 1.0, 5);

        var sp = options.StaffSpacing;
        double staffIdeal = sp.DefaultStaffStaff.BasicDistance;
        double systemIdeal = options.VerticalSpacing.SystemSystem.BasicDistance;

        // ⚠️ ASSERT THE REGIME FIRST (HANDOFF 5.0 trap 7): below its ideal is what "this page
        // squeezes" means, and a book that stopped squeezing would otherwise pass everything
        // below by measuring nothing.
        Assert.True(ossiaInside < staffIdeal - 0.05,
            $"the ossia pair must be COMPRESSED on this page, not printing its ideal: "
            + $"{ossiaInside:F9} against the spec's {staffIdeal:F6}. A rigid spring prints "
            + "the ideal exactly whatever force the page solves.");
        Assert.True(controlInside < staffIdeal - 0.05,
            $"the control must squeeze too or the pair is not like-for-like: {controlInside:F9}");

        // One force per page, read through two springs of strength 1 and 4.
        Assert.Equal(4.0, (systemIdeal - ossiaSystem) / (staffIdeal - ossiaInside), 3);
        Assert.Equal(4.0, (systemIdeal - controlSystem) / (staffIdeal - controlInside), 3);

        // ...and the two pages must solve DIFFERENT forces, the ossia's the smaller. An ossia
        // is engraved small, so its systems are shorter, so its page keeps more slack and
        // compresses less — LilyPond reads 0.212184 against 0.213548. This is the assertion
        // that says the ossia's size reaches the PAGE and not merely the staff it sits on: it
        // is the rule the ledger's numbers are a consequence of, so it survives them moving.
        // ⚠️ MEASURED BEFORE THE PORT, the two were EQUAL TO NINE DIGITS (0.214493 twice),
        // because every notehead, stem and clef of an ossia was seeded at full size while the
        // renderer drew it at OssiaScale — a full staff's room reserved for a small staff.
        // audit/lp-geometry page.ossia-pair.compressed.first-staff-refpoint carries the account.
        double ossiaForce = staffIdeal - ossiaInside;
        double controlForce = staffIdeal - controlInside;
        Assert.True(ossiaForce < controlForce - 1e-6,
            $"the ossia book must squeeze LESS than its full-size control — its ink is "
            + $"smaller: {ossiaForce:F9} against {controlForce:F9}. Equal forces mean the "
            + "ossia's size never reached the page.");

        double ossiaHead = ossiaPage.ScaledPairFirstStaffRefpoint(
            5, EngravingDefaults.OssiaScale, 5);
        double controlHead = controlPage.ScaledPairFirstStaffRefpoint(5, 1.0, 5);

        Assert.True(ossiaHead > options.MarginTop,
            $"a system led by an ossia must start BELOW the top margin {options.MarginTop:F6}; "
            + $"its first refpoint is at {ossiaHead:F6}, i.e. "
            + $"{options.MarginTop - ossiaHead:F6} INTO the margin.");
        Assert.True(Math.Abs(ossiaHead - controlHead) < 0.5,
            $"the two spellings of one book must hang from the same head: ossia "
            + $"{ossiaHead:F6}, control {controlHead:F6}.");
    }

    /// <summary>
    /// The headline fidelity number: the total distance from LilyPond across the corpus.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT an assertion on a threshold — a threshold would either be slack
    /// enough to be meaningless or would fail the build for an unrelated new probe. It
    /// prints, so the number is visible in the run and its trend is reviewable.
    /// </remarks>
    [Fact]
    public void Corpus_ReportsTotalDivergenceFromLilyPond()
    {
        var (entries, tolerance) = Ledger.Value;
        var report = new StringBuilder();
        double total = 0;
        int exact = 0, seeded = 0, counts = 0, countsOff = 0;

        foreach (var probe in LpGeometryProbes.All)
        {
            if (!entries.TryGetValue(probe.Id, out var entry) || entry.Residual is not { } residual)
                continue;
            seeded++;
            bool isDistance = entry.Unit == "ss";
            // Distances sum; counts are tallied separately. Mixing them would put a
            // "1 system" into a staff-space total and make the headline unreadable.
            if (isDistance)
                total += Math.Abs(residual);
            else
                counts++;
            if (Math.Abs(residual) <= tolerance)
                exact++;
            else
            {
                if (!isDistance)
                    countsOff++;
                report.AppendLine($"  {residual,10:F6}   {probe.Id}"
                    + (isDistance ? "" : $"   [{entry.Unit}]"));
            }
        }

        Assert.True(seeded > 0, "the LP fidelity corpus is empty");

        // Written to test output, NOT asserted. Assert.True(true, msg) prints nothing —
        // a headline number nobody can read is not a headline number.
        // Visible with: dotnet test --logger 'console;verbosity=detailed'
        _output.WriteLine($"LP fidelity: {exact}/{seeded} exact, total |residual| = {total:F6} ss"
            + (counts > 0 ? $" over {seeded - counts} distances; {counts - countsOff}/{counts} counts match" : ""));
        if (report.Length > 0)
        {
            _output.WriteLine("divergent entries (residual, id):");
            _output.WriteLine(report.ToString().TrimEnd());
        }
    }
}
