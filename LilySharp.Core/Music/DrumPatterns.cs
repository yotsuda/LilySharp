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

namespace LilySharp.Core.Music;

/// <summary>One stock drum pattern: a one-bar 4/4 phrase a drum chart is built from.</summary>
/// <param name="Name">The phrase's name, as it is declared and played.</param>
/// <param name="Family">What it is played on, in <see cref="PhraseSummary.FamilyOrder"/>'s words
/// (a fill is "toms").</param>
/// <param name="Body">The phrase's music, as written between its braces.</param>
/// <param name="Description">A few words for the completion list.</param>
public sealed record DrumPattern(string Name, string Family, string Body, string Description)
{
    /// <summary>The declaration the editor writes into the file: <c>phrase NAME { BODY }</c>.</summary>
    public string Declaration => $"phrase {Name} {{ {Body} }}";
}

/// <summary>
/// The stock drum patterns the editor offers in a drum part (owner's request 2026-10-11): the
/// hi-hat and ride figures, backbeats, kicks and fills a groove is assembled from, each a
/// one-bar phrase in 4/4 so they combine line by line in <c>drums { }</c>.
/// </summary>
/// <remarks>
/// LILYSHARP-OWN (owner's request 2026-10-11): LilyPond ships no pattern library. Every body
/// merges with every other in a chart (DrumPatternTests): the shuffle is a triplet a beat, not
/// one over the bar, so a backbeat's quarter can join it.
/// </remarks>
public static class DrumPatterns
{
    /// <summary>All of them, in the order the list shows them: by kit piece, then as written here.</summary>
    public static readonly IReadOnlyList<DrumPattern> All =
    [
        // Hi-hat and ride
        new("hats8", "hi-hat", "hh8 8 8 8 8 8 8 8 |", "eighth hi-hats"),
        new("hats16", "hi-hat", "hh16 16 16 16 16 16 16 16 16 16 16 16 16 16 16 16 |", "sixteenth hi-hats"),
        new("hats4", "hi-hat", "hh4 4 4 4 |", "quarter hi-hats"),
        new("hatsOpen", "hi-hat", "hh8 hho hh hho hh hho hh hho |", "open on the off-beats"),
        new("shuffle", "ride", "tuplet 3/2 { cymr4 8 } tuplet 3/2 { cymr4 8 } tuplet 3/2 { cymr4 8 } tuplet 3/2 { cymr4 8 } |", "triplet shuffle on the ride"),
        // Snare
        new("back", "snare", "r4 sn r sn |", "backbeat on 2 and 4"),
        new("halfTime", "snare", "r2 sn |", "half time, on 3"),
        new("ghost", "snare", "r4 sn r8. sn16@parenthesize sn4 |", "backbeat with a ghost note"),
        // Bass drum
        new("fourFloor", "bass drum", "bd4 4 4 4 |", "four on the floor"),
        new("kick8", "bass drum", "bd4 r8 bd bd4 r |", "eighth-beat kick"),
        new("kickSync", "bass drum", "bd4 r8 bd r bd r4 |", "syncopated kick"),
        // Fills
        new("fillToms", "toms", "sn16 16 16 16 tomh16 16 16 16 tomml16 16 16 16 tomfh16 16 16 16 |", "sixteenths down the toms"),
        new("fillTriplet", "toms", "tuplet 3/2 { sn8 8 8 } tuplet 3/2 { tomh8 8 8 } tuplet 3/2 { tomml8 8 8 } tuplet 3/2 { tomfh8 8 8 } |", "eighth triplets down the toms"),
        new("flam", "snare", "r2. acciaccatura { sn16 } sn4 |", "a flam on 4"),
    ];

    /// <summary>The stock pattern of that name, or null.</summary>
    public static DrumPattern? Named(string name) => All.FirstOrDefault(p => p.Name == name);
}
