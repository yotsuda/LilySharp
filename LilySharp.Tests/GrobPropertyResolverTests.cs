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
using LilySharp.Core.Syntax;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The override resolver is a replayable timeline (LILYPOND-REF:
/// lily/context-property.cc): overrides take effect from their timewise
/// position onward, a rewind (each new voice/staff pass restarts at its first
/// measure) replays from the top instead of leaking the previous pass's state
/// backward, and a <c>\once</c> pops back to the value it displaced rather
/// than erasing an outer persistent override.
/// </summary>
[Trait("Category", "Unit")]
public class GrobPropertyResolverTests
{
    private static GrobPropertyResolver Resolver(
        GrobOverride[]? overrides = null, GrobRevert[]? reverts = null)
        => new(
            (overrides ?? Array.Empty<GrobOverride>()).ToImmutableArray(),
            (reverts ?? Array.Empty<GrobRevert>()).ToImmutableArray());

    [Fact]
    public void Rewind_DoesNotLeakALaterOverrideIntoTheNextPassesEarlierMeasures()
    {
        // Voice-1 pass activates the override at measure 2; the voice-2 pass
        // then restarts at measure 0 — the override must NOT be active there
        // (it would render the override BEFORE its source position).
        var r = Resolver(new[] { new GrobOverride("NoteHead", "color", new LysValue.Symbol("red"), 2, 0) });

        r.AdvanceTo(2, 0);
        Assert.Equal("red", r.GetString("NoteHead", "color")); // pass 1, at the override

        r.AdvanceTo(0, 0); // pass 2 starts over
        Assert.Null(r.GetString("NoteHead", "color"));

        r.AdvanceTo(2, 0); // ...and still sees it at its own position
        Assert.Equal("red", r.GetString("NoteHead", "color"));
    }

    [Fact]
    public void Once_RestoresTheDisplacedPersistentOverride()
    {
        // red c | \once blue d | e ... — e and everything after must be red
        // again, not default (the once must pop, not erase).
        var r = Resolver(new[]
        {
            new GrobOverride("NoteHead", "color", new LysValue.Symbol("red"), 0, 0),
            new GrobOverride("NoteHead", "color", new LysValue.Symbol("blue"), 0, 1, IsOnce: true),
        });

        r.AdvanceTo(0, 0);
        Assert.Equal("red", r.GetString("NoteHead", "color"));
        r.AdvanceTo(0, 1);
        Assert.Equal("blue", r.GetString("NoteHead", "color"));
        r.AdvanceTo(0, 2);
        Assert.Equal("red", r.GetString("NoteHead", "color"));
    }

    [Fact]
    public void Once_WithoutAnUnderlyingOverride_ExpiresToUnset()
    {
        var r = Resolver(new[]
        {
            new GrobOverride("NoteHead", "color", new LysValue.Symbol("blue"), 0, 1, IsOnce: true),
        });

        r.AdvanceTo(0, 1);
        Assert.Equal("blue", r.GetString("NoteHead", "color"));
        r.AdvanceTo(0, 2);
        Assert.Null(r.GetString("NoteHead", "color"));
    }

    [Fact]
    public void SkippedPositions_StillApplyEarlierOverrides()
    {
        // A consumer that advances only to selected item indices (the
        // collision columns) must still see an override recorded at an index
        // it skipped — overrides apply from their position ONWARD.
        var r = Resolver(new[] { new GrobOverride("NoteColumn", "force-hshift", new LysValue.Real(0.5), 0, 1) });

        r.AdvanceTo(0, 3); // never visits (0,1)
        Assert.Equal(0.5, r.GetDouble("NoteColumn", "force-hshift"));
    }

    [Fact]
    public void ReadvancingToTheSamePosition_KeepsAPendingOnceVisible()
    {
        // A second staff/voice re-visiting the same (measure, item) — e.g. the
        // multi-staff re-advance — must still see the \once that applies there.
        var r = Resolver(new[]
        {
            new GrobOverride("NoteHead", "color", new LysValue.Symbol("blue"), 1, 0, IsOnce: true),
        });

        r.AdvanceTo(1, 0);
        Assert.Equal("blue", r.GetString("NoteHead", "color"));
        r.AdvanceTo(1, 0);
        Assert.Equal("blue", r.GetString("NoteHead", "color"));
    }

    [Fact]
    public void Revert_RemovesFromItsPositionOnward_AndRewindReplays()
    {
        var r = Resolver(
            new[] { new GrobOverride("Stem", "length", new LysValue.Int(10), 0, 0) },
            new[] { new GrobRevert("Stem", "length", 1, 0) });

        r.AdvanceTo(0, 0);
        Assert.Equal(10.0, r.GetDouble("Stem", "length"));
        r.AdvanceTo(1, 0);
        Assert.Null(r.GetDouble("Stem", "length"));

        // Rewind (next pass): the override is live again at its own range.
        r.AdvanceTo(0, 0);
        Assert.Equal(10.0, r.GetDouble("Stem", "length"));
    }

    [Fact]
    public void AnExpiredOnce_DoesNotActivateWhenSkippedOver()
    {
        // Advancing from before the \once to after it in one step: the once
        // applied only AT its position, so it must not be active at (0,3).
        var r = Resolver(new[]
        {
            new GrobOverride("NoteHead", "color", new LysValue.Symbol("blue"), 0, 1, IsOnce: true),
        });

        r.AdvanceTo(0, 0);
        r.AdvanceTo(0, 3);
        Assert.Null(r.GetString("NoteHead", "color"));
    }
}
