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
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Layout;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// WHICH FRAME EACH SEED OF A PER-STAFF SKYLINE ARRIVES IN — one test per path, each
/// naming its own origin, so that moving the skyline's origin fails them BY PATH instead
/// of failing six spacing measurements at once.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ THIS IS STEP ONE OF A FRAME MIGRATION AND NOT A CLEANUP (the procedure the coordinate
/// audit sets out: assert the stored values BEFORE flipping them, move every producer
/// together, reflect once at the island's edge). LilyPond's <c>Align_interface</c> measures
/// between VerticalAxisGroup REFERENCE POINTS — a staff's middle line — and
/// <see cref="SkylineBuilder.BuildStaffSkylines"/> builds about the staff's TOP line, so
/// every consumer that wants LilyPond's number adds a half-staff back at its own call site.
/// </para>
/// <para>
/// ⚠️ AND THE STAFF-LOCAL FRAME IS NOT ONE FRAME, which is the finding these tests exist to
/// pin down and which an attempt at the migration ran into: some seeds are placed about the
/// staff MIDDLE (they take a <c>staffMiddleUp</c>) and others about the staff TOP (they take
/// no offset at all). While the skyline's own origin happens to be the top line the two read
/// alike, so nothing distinguishes them — and an origin moved by a half-staff moves one
/// group and not the other. Each test below records which group its path is in.
/// </para>
/// <para>
/// ⚠️ THE EXPECTED VALUES ARE DERIVED, never pasted from a run: a staff line's ink is
/// <c>StaffHeight/2 + StaffLineThickness/2</c>, a notehead's is
/// <see cref="EngravingDefaults.NoteheadHalfHeight"/>, a bracket's is half
/// <see cref="EngravingDefaults.TupletBracketThickness"/>, and the flat bow's is the
/// bezier's own <c>3/4</c> of the control shift plus half the round pen. A test that
/// compared the implementation with itself would survive the very flip it exists to catch.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class StaffSkylineFrameTests
{
    private static readonly LayoutOptions Options = LayoutOptions.Default;
    private static readonly double StaffHeight = Options.StaffHeight;   // 4.0
    private static double HalfStaff => StaffHeight / 2.0;

    /// <summary>The staff line ink half-span — the seed every other path is read against.</summary>
    private static double StaffLineInk =>
        StaffHeight / 2.0 + EngravingDefaults.StaffLineThickness / 2.0;

    private static Staff OneStaff(params MusicItem[] items)
    {
        var measure = new Measure(
            items.ToImmutableArray(), BarlineType.None, BarlineType.Single, null, 0, 0);
        return Staff.Create(ClefType.Treble, new Voice("v", ImmutableArray.Create(measure)));
    }

    private static ImmutableArray<MeasureLayout> Layouts(int itemCount = 1)
    {
        var items = ImmutableArray.CreateBuilder<ItemLayout>(itemCount);
        for (int i = 0; i < itemCount; i++)
            items.Add(new ItemLayout(i, 5.0 + i * 4.0, 1.0));
        return ImmutableArray.Create(new MeasureLayout(0, 0, 40, items.ToImmutable()));
    }

    private static (VerticalSkyline Up, VerticalSkyline Down) Build(
        Staff staff,
        ImmutableArray<TupletBracketLayout> tuplets = default,
        ImmutableArray<SlurLayout> slurs = default,
        ImmutableArray<TieLayout> ties = default)
        => new SkylineBuilder(StaffHeight).BuildStaffSkylines(
            staff, Layouts(), default, default, tuplets, slurs, ties, default);

    /// <summary>
    /// THE CLEF IS IN THE PER-STAFF SILHOUETTE, and it is the grob that reaches furthest out
    /// of a plain staff — so the alignment, which walks THIS skyline, sees it.
    /// </summary>
    /// <remarks>
    /// LILYPOND-REF: lily/axis-group-interface.cc:914-940 <c>skyline_spacing</c> — the
    /// inside-staff skylines carry every inside-staff grob and a Clef is one.
    /// <para>
    /// ⚠️ THE EXPECTATION IS DERIVED, NOT MEASURED (HANDOFF 5.2.1⑤): the G clef's own ink box
    /// placed on the line it names, not the 3.800000 LilyPond happens to dump. The two agree
    /// to every digit, which is the point — the font metric was always right and only the
    /// seed was missing, so a test written against the dumped number would have passed for
    /// the wrong reason once the box changed.
    /// </para>
    /// <para>
    /// ⚠️ AND THE ABSENCE IS ASSERTED TOO. <c>systemLeft = NaN</c> means "no clef" — the
    /// contract callers that want only the scalar extents rely on — and without that half a
    /// reader could pass the clef in by accident and nothing would say so.
    /// </para>
    /// </remarks>
    [Fact]
    public void Clef_IsInThePerStaffSkyline_AtItsOwnGlyphExtent()
    {
        // A note that stays well inside the staff, so the clef is the only thing that can
        // reach past the staff lines.
        var staff = OneStaff(new NoteItem(0, Fraction.Whole, 0, null, false, 0));

        var withClef = new SkylineBuilder(StaffHeight).BuildStaffSkylines(
            staff, Layouts(), default, default, default, default, default, default,
            systemLeft: 0.0);
        var withoutClef = Build(staff);

        // The treble clef sits on the G line — staff position -2, i.e. one staff space below
        // the middle — and its ink box is the font's own.
        const double gLine = -1.0;
        double clefTop = gLine + GlyphMetrics.ClefG.Top;
        double clefBottom = gLine + GlyphMetrics.ClefG.Bottom;

        Assert.Equal(clefTop, withClef.Up.MaxHeight(), 9);
        Assert.Equal(clefBottom, withClef.Down.MaxHeight(), 9);

        // ...and it really is the clef doing it: without the seed the silhouette stops at
        // the staff lines, so the reading is not something the notes would have given.
        Assert.Equal(StaffLineInk, withoutClef.Up.MaxHeight(), 9);
        Assert.True(clefTop > StaffLineInk + 1.0,
            $"the clef must reach well past the staff lines: {clefTop:F6} against {StaffLineInk:F6}");
    }

    /// <summary>
    /// THE STAFF SYMBOL, which is the seed that names the origin: the outer lines are ink,
    /// so they reach half a line thickness past the outermost line CENTRE, and the two
    /// readings are what say where 0 is. Symmetric about the origin would mean the middle
    /// line; 0.05 above and 4.05 below means the TOP line.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>MaxHeight</c> is signed Y-up on BOTH skylines — a DOWN skyline's is negative —
    /// so the reading below says "the bottom line's ink is 4.05 BELOW the origin", not
    /// "4.05 of depth". Written out because the internal storage IS sign*y and the two are
    /// easy to confuse at the call site.
    /// </remarks>
    [Fact]
    public void StaffSymbol_IsSeededAboutTheStaffReferencePoint()
    {
        var (up, down) = Build(OneStaff(new NoteItem(0, Fraction.Whole, 0, null, false, 0)));

        // SYMMETRIC, which is the whole assertion: the outer lines stand the same distance
        // either side of the origin exactly when the origin is the MIDDLE line. Before the
        // migration this read 0.05 above and 4.05 below — the top line.
        Assert.Equal(StaffLineInk, up.MaxHeight(), 9);
        Assert.Equal(-StaffLineInk, down.MaxHeight(), 9);
    }

    /// <summary>
    /// A TUPLET BRACKET, and it is in the staff-TOP group: <c>TupletBracketEngraver</c> is run
    /// with no staff offset, so its <c>*YUp</c> is already measured from the staff's top line
    /// and the seeder adds nothing at all. Fed a Y this test chooses, the ink comes back at
    /// that Y plus the bracket's own half thickness — a coefficient of one and an offset of
    /// zero, which is the whole claim.
    /// </summary>
    [Fact]
    public void TupletBracket_ArrivesInTheStaffTopFrameAndIsRebasedByAHalfStaff()
    {
        const double y = 6.25;
        double half = EngravingDefaults.TupletBracketThickness / 2.0;
        var (up, _) = Build(
            OneStaff(new NoteItem(0, Fraction.Whole, 0, null, false, 0)),
            tuplets: ImmutableArray.Create(Bracket(y, stemUp: true, number: "")));

        Assert.Equal(y + half + HalfStaff, up.MaxHeight(), 9);
    }

    /// <summary>
    /// THE TUPLET NUMBER IS A SECOND SEED ON THE SAME PATH, and it reaches FURTHER than the
    /// line it straddles — so a conversion applied to the bracket line and not to the number
    /// moves the line and leaves the binding ink where it was, which is a change that looks
    /// like nothing until a staff below it collides.
    /// </summary>
    /// <remarks>
    /// Stated as a relation rather than against a font constant: the number's own height is a
    /// measurement of the text face, and asserting it here would only compare the builder
    /// with the same call it makes. What matters for the frame is that the number RESPONDS to
    /// the bracket's Y with a coefficient of one — the same origin — and that it is the ink
    /// that wins.
    /// </remarks>
    [Fact]
    public void TupletNumber_RidesTheSameFrameAsItsBracketLine()
    {
        const double y = 6.25, step = 3.0;
        var staff = OneStaff(new NoteItem(0, Fraction.Whole, 0, null, false, 0));

        double lineOnly = Build(staff,
            tuplets: ImmutableArray.Create(Bracket(y, stemUp: true, number: ""))).Up.MaxHeight();
        double withNumber = Build(staff,
            tuplets: ImmutableArray.Create(Bracket(y, stemUp: true, number: "3"))).Up.MaxHeight();
        double movedUp = Build(staff,
            tuplets: ImmutableArray.Create(Bracket(y + step, stemUp: true, number: "3")))
            .Up.MaxHeight();

        Assert.True(withNumber > lineOnly,
            $"the number is not the outer ink ({withNumber:F6} vs {lineOnly:F6}), so this "
            + "test is not measuring what it says it is.");
        Assert.Equal(step, movedUp - withNumber, 9);
    }

    /// <summary>
    /// The same bracket pointing DOWN, which reaches the other skyline. Carried separately
    /// because a frame or a sign that is right on one side and wrong on the other is exactly
    /// what a single-direction test cannot see — the same reason the ledger carries
    /// <c>staff.staff.tuplet-bracket-up</c> AND <c>-down</c>.
    /// </summary>
    [Fact]
    public void TupletBracketDown_ArrivesInTheStaffTopFrameAndIsRebasedByAHalfStaff()
    {
        const double y = -9.5;
        double half = EngravingDefaults.TupletBracketThickness / 2.0;
        var (_, down) = Build(
            OneStaff(new NoteItem(0, Fraction.Whole, 0, null, false, 0)),
            tuplets: ImmutableArray.Create(Bracket(y, stemUp: false, number: "")));

        Assert.Equal(y - half + HalfStaff, down.MaxHeight(), 9);
    }

    /// <summary>A flat bracket at <paramref name="y"/> in the frame its engraver produces.</summary>
    private static TupletBracketLayout Bracket(double y, bool stemUp, string number) =>
        new(MeasureIndex: 0, StartX: 5.0, EndX: 30.0, StartYUp: y, EndYUp: y,
            NumberText: number, IsStemUp: stemUp, ShowBracket: true, SourcePosition: 0);

    /// <summary>
    /// A SLUR, also in the staff-TOP group and also unrebased. A FLAT bow, so its ink is
    /// derivable in closed form rather than measured: the interior control points are pushed
    /// out by half the mid thickness (the chord is horizontal, so the whole of it lands on Y),
    /// the cubic reaches three quarters of that at its midpoint, and the round pen adds half
    /// its own width.
    /// </summary>
    [Fact]
    public void Slur_ArrivesInTheStaffTopFrameAndIsRebasedByAHalfStaff()
    {
        const double y = 7.0;
        var slur = new SlurItem(0, 0, true, 0, 0, 0, 0, 0);
        var (up, _) = Build(
            OneStaff(new NoteItem(0, Fraction.Whole, 0, null, false, 0)),
            slurs: ImmutableArray.Create(new SlurLayout(
                slur, startX: 5.0, startY: y, endX: 30.0, endY: y,
                control1: (12.0, y), control2: (23.0, y))));

        double bowInk = 0.75 * (0.5 * EngravingDefaults.SlurMidThickness)
                        + 0.5 * EngravingDefaults.BowEndRounding;
        Assert.Equal(y + bowInk + HalfStaff, up.MaxHeight(), 9);
    }

    /// <summary>
    /// A TIE, the same grob class as the slur and the same frame — carried because the two
    /// reach the skyline through different arrays and a migration that converts one and not
    /// the other would leave nothing failing but the spacing.
    /// </summary>
    [Fact]
    public void Tie_ArrivesInTheStaffTopFrameAndIsRebasedByAHalfStaff()
    {
        // ⚠️ THE CONTROLS MUST REALLY DIP. SeedBowInk reads the direction off the geometry —
        // `curveUp = (c1.Y + c2.Y) >= (p0y + p3y)` — so a FLAT bow is an UP bow and a tie
        // written flat would be seeded into the other skyline and measure nothing. Found by
        // writing it flat first; the assertion then read the staff's own bottom line.
        const double y = -8.0, dip = 1.0;
        var note = new NoteItem(0, Fraction.Whole, 0, null, false, 0);
        var tie = new TieItem(note, note, 0, false, 0, 0, 0, 0, 0);
        var (_, down) = Build(
            OneStaff(note),
            ties: ImmutableArray.Create(new TieLayout(
                tie, startX: 5.0, startY: y, endX: 30.0, endY: y,
                control1: (12.0, y - dip), control2: (23.0, y - dip))));

        // The cubic reaches 3/4 of the way to its interior controls at the midpoint, and
        // those controls are pushed a further half-thickness out before it is sampled.
        double bowInk = 0.75 * (dip + 0.5 * EngravingDefaults.SlurMidThickness)
                        + 0.5 * EngravingDefaults.BowEndRounding;
        Assert.Equal(y - bowInk + HalfStaff, down.MaxHeight(), 9);
    }

    /// <summary>
    /// THE COUNT ITSELF, which is what the migration needs and what no single test above
    /// states: the per-staff skyline is fed from TWO staff-local frames, and the difference
    /// between them is exactly a half-staff. Asserted as a relation between two paths on one
    /// build, so it survives any change to either path's own ink.
    /// </summary>
    /// <remarks>
    /// ⚠️ IT PASSES BECAUSE THE ORIGIN IS THE TOP LINE. Move the origin to the reference
    /// point and this is the test that says what to do: the staff-TOP group needs a
    /// half-staff added at its seeds, the staff-MIDDLE group needs nothing, and the two
    /// answers must still differ by <c>HalfStaff</c> afterwards.
    /// </remarks>
    [Fact]
    public void TheTwoStaffLocalFrames_DifferByExactlyAHalfStaff()
    {
        var staff = OneStaff(new NoteItem(0, Fraction.Whole, 0, null, false, 0));
        double half = EngravingDefaults.TupletBracketThickness / 2.0;

        // Path A, the staff-MIDDLE group: the staff symbol is placed about the middle line,
        // so its top line's ink stands StaffLineInk above THAT origin. Chosen over a
        // notehead because its ink is a spec quantity rather than a font measurement.
        double fromMiddle = Build(staff).Up.MaxHeight();

        // Path B, the staff-TOP group: a bracket asked for the SAME StaffLineInk above ITS
        // origin, the staff's top line.
        double fromTop = Build(staff,
            tuplets: ImmutableArray.Create(Bracket(StaffLineInk - half, stemUp: true, number: "")))
            .Up.MaxHeight();

        Assert.Equal(StaffLineInk, fromMiddle, 9);
        Assert.Equal(StaffLineInk + HalfStaff, fromTop, 9);
        Assert.Equal(HalfStaff, fromTop - fromMiddle, 9);
    }
}
