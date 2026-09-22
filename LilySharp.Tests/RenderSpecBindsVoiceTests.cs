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
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <see cref="RenderSpec.BindsVoice"/> is the membership half of
/// <see cref="RenderSpec.GetVoiceNames"/>, written as its own switch so the harvest of undrawn
/// parts can ask it without building the bindings. The two must agree name for name: a name
/// the switch missed would be harvested although it is drawn, and one it invented would lose
/// an undrawn part's repeats.
/// </summary>
[Trait("Category", "Unit")]
public class RenderSpecBindsVoiceTests
{
    [Fact]
    public void EveryItemKind_AnswersAsTheBindingsDo()
    {
        static StaffSpec S(string name) => new(ClefType.Treble, name);
        var spec = new RenderSpec("s", "o", ImmutableArray.Create<RenderItemSpec>(
            new SingleStaffSpec(S("single")),
            new GrandStaffRenderSpec(new GrandStaffSpec(ImmutableArray.Create<RenderItemSpec>(
                new SingleStaffSpec(S("upper")),
                new CondensedStaffSpec(ClefType.Bass, ImmutableArray.Create("inCondensed1", "inCondensed2")),
                new GrandStaffRenderSpec(new GrandStaffSpec(ImmutableArray.Create<RenderItemSpec>(
                    new SingleStaffSpec(S("nested")))))))),
            new CondensedStaffSpec(ClefType.Treble, ImmutableArray.Create("condensedA", "condensedB")),
            new CombinedStaffSpec(ClefType.Treble, ImmutableArray.Create("combinedA", "combinedB")),
            new TabStaffSpec(S("tab"), default),
            new OssiaStaffSpec(S("ossia")),
            new ChordRowSpec("chordRow"),
            new LyricsRowSpec("lyricsRow", Sings: "singsIsNotABinding")));

        var names = spec.GetVoiceNames().ToList();
        Assert.Equal(13, names.Count);
        foreach (var name in names.Concat(new[] { "singsIsNotABinding", "absent", "" }))
            Assert.Equal(names.Contains(name), spec.BindsVoice(name));
    }
}
