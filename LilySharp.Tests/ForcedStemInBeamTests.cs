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
using System.Linq;
using LilySharp.Core.LilyPond;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Semantics;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// A stem the writer turned (<c>@stemUp</c> / <c>@stemDown</c>) is read even inside a BEAM,
/// and it changes the beam's own direction as well as its own.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/beam.cc:894-924 Beam::get_default_dir — a stem that already carries a
/// <c>direction</c> sets <c>force_dir</c>, which turns OFF the farthest-head rule and hands
/// the group to the per-stem vote; lily/beam.cc:946-956 set_stem_directions — the group's
/// direction is stamped only onto stems that do not already carry one.
/// <para>
/// ⚠️ THIS SHIPPED BROKEN AND NOTHING CAUGHT IT. The annotation was written into
/// <c>StemUpOverride</c>, the same slot <c>MeasureCollector.ResolveBeamStemDirections</c>
/// writes the beam's answer into, so on a beamed note it was overwritten without a word —
/// and <c>lysc ly</c> dropped it too, so the twin was a different piece of music and the
/// pair that would have shown it could not be built. No committed fixture reaches this, so
/// FIXING IT MOVED NO SNAPSHOT; these assertions and the ledger book
/// <c>tie.direction.beam-stem-turned-by-hand</c> are the whole of the net.
/// </para>
/// <para>
/// ⚠️ ASSERTED AS A PAIR, not as one direction (HANDOFF 5.4): the same three notes with and
/// without the annotation. A port that ignored the wish again would give the two runs the
/// same answer, which is exactly the failure this documents, and no single reading can say so.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class ForcedStemInBeamTests
{
    /// <summary>
    /// <c>d</c> on the middle line beamed to a lower <c>a,</c>: unforced the group goes UP
    /// (the farthest head is below), and both stems with it.
    /// </summary>
    private static (bool GroupUp, bool[] StemsUp) BeamOf(bool? forcedOnFirst)
    {
        var first = new NoteItem(0, Fraction.Eighth, 1, null, false, 0, hasBeamStart: true)
        {
            ForcedStemUp = forcedOnFirst,
        };
        var second = new NoteItem(-3, Fraction.Sixteenth, 0, null, false, 1, hasBeamEnd: true);
        var measure = new Measure(
            ImmutableArray.Create<MusicItem>(first, second),
            BarlineType.None, BarlineType.None, null, 0, 0);
        var voice = new Voice("default", ImmutableArray.Create(measure));

        var groups = new BeamDetector().DetectBeamGroups(voice, new TimeSignature(4, 4));
        Assert.Single(groups);
        return (groups[0].StemUp, groups[0].Members.Select(m => m.MemberStemUp).ToArray());
    }

    [Fact]
    public void AForcedStemInsideABeam_KeepsItsOwnSide_AndTurnsTheGroupsVote()
    {
        var plain = BeamOf(null);
        var forced = BeamOf(false);

        // The control: nothing forced, so the head farthest from the middle line decides and
        // every stem takes the group's direction. (MEASURED against LilyPond on the twin of
        // audit/lp-geometry probe TDBEAM: `dir=1 stems=(1 1)`.)
        Assert.True(plain.GroupUp);
        Assert.Equal(new[] { true, true }, plain.StemsUp);

        // ...and with the FIRST stem turned down, LilyPond reads `dir=1 stems=(-1 1)` — the
        // group still UP, because force_dir replaces the farthest-head rule with a vote that
        // this configuration wins upward, and the forced stem alone stays down. A knee.
        Assert.True(forced.GroupUp);
        Assert.Equal(new[] { false, true }, forced.StemsUp);

        // The pair itself: the annotation has to CHANGE something, which is the assertion
        // that failed before this was fixed (the two runs were identical).
        Assert.NotEqual(plain.StemsUp, forced.StemsUp);
    }

    /// <summary>
    /// The twin has to carry the wish too, or the pair above cannot be checked against
    /// LilyPond at all — the .ly would engrave a different piece of music.
    /// </summary>
    /// <remarks>
    /// <c>\once \override Stem.direction</c> rather than <c>\stemUp</c>: Lily#'s annotation
    /// belongs to ONE note, LilyPond's command runs until <c>\stemNeutral</c>.
    /// LILYPOND-REF: ly/property-init.ly stemUp — <c>\override Stem.direction = #UP</c>.
    /// </remarks>
    [Theory]
    [InlineData("stemDown", "#DOWN")]
    [InlineData("stemUp", "#UP")]
    public void TheTwinCarriesTheForcedStem(string annotation, string expectedDirection)
    {
        string source = $$"""
            octave absolute
            time 4/4
            key c major

            part melody { clef bass }

            section Main {
              melody { d,4~ d,8.@{{annotation}} a,,16 d,8 d, b,,4 | }
            }

            form main { ~Main }

            score main { staff melody }
            """;

        var exporter = new LilyPondExporter();
        string ly = exporter.Export(SyntaxTree.Parse(source));

        Assert.Contains($"\\once \\override Stem.direction = {expectedDirection}", ly);
        // ...and it is no longer thrown away with a warning, which is how it read before.
        Assert.DoesNotContain(exporter.Warnings, w => w.Contains("not mapped"));
    }
}
