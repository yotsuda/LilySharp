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

using LilySharp.Core.Svg.Model;

namespace LilySharp.Core.Syntax;

/// <summary>
/// One drum instrument: staff placement, notehead and General MIDI key.
/// </summary>
/// <param name="FullName">The canonical LilyPond drum name (e.g. "bassdrum").</param>
/// <param name="StaffPosition">Staff position on the 5-line percussion staff
/// (0 = middle line, positive = up), from LilyPond's drums-style table.</param>
/// <param name="Notehead">Notehead style from the same table.</param>
/// <param name="GmKey">General MIDI percussion key (channel 10).</param>
public readonly record struct DrumInfo(
    string FullName, int StaffPosition, NoteheadStyle Notehead, int GmKey,
    // Auto articulation from the drums-style table: "stopped" (+) on the
    // closed hi-hat, "open" (○) on the open hi-hat.
    string? Mark = null);

/// <summary>
/// The drum-note vocabulary: bare identifiers in a music stream that name
/// drum-kit instruments (<c>bd4 sn8 hh</c>). Names and aliases are LilyPond's
/// (drumPitchNames); placement and noteheads follow the default drums-style
/// table; MIDI keys come from midiDrumPitches.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/drumpitch-init.ly — drumPitchNames (aliases),
/// midiDrumPitches (GM keys) and the drums-style hash table
/// (name → (notehead-style articulation staff-position)).
/// Bare identifiers are otherwise invalid in a music stream (variables are
/// referenced with '$'), so this vocabulary claims free grammar space.
/// </remarks>
public static class DrumNameRegistry
{
    // FullName → info. Positions/heads: drums-style; instruments absent from
    // that table (tambourine) sit on the middle line with the default head,
    // matching LilyPond's fallback.
    private static readonly Dictionary<string, DrumInfo> Canonical = new(StringComparer.Ordinal)
    {
        ["acousticbassdrum"] = new("acousticbassdrum", -3, NoteheadStyle.Default, 35),
        ["bassdrum"] = new("bassdrum", -3, NoteheadStyle.Default, 36),
        ["sidestick"] = new("sidestick", 1, NoteheadStyle.Cross, 37),
        ["acousticsnare"] = new("acousticsnare", 1, NoteheadStyle.Default, 38),
        ["snare"] = new("snare", 1, NoteheadStyle.Default, 38),
        ["handclap"] = new("handclap", 1, NoteheadStyle.Triangle, 39),
        ["electricsnare"] = new("electricsnare", 1, NoteheadStyle.Default, 40),
        ["lowfloortom"] = new("lowfloortom", -4, NoteheadStyle.Default, 41),
        ["closedhihat"] = new("closedhihat", 3, NoteheadStyle.Cross, 42, "stopped"),
        ["hihat"] = new("hihat", 3, NoteheadStyle.Cross, 42),
        ["highfloortom"] = new("highfloortom", -2, NoteheadStyle.Default, 43),
        ["pedalhihat"] = new("pedalhihat", -5, NoteheadStyle.Cross, 44),
        ["splashhihat"] = new("splashhihat", -5, NoteheadStyle.Cross, 44),
        ["lowtom"] = new("lowtom", -1, NoteheadStyle.Default, 45),
        ["openhihat"] = new("openhihat", 3, NoteheadStyle.Cross, 46, "open"),
        ["halfopenhihat"] = new("halfopenhihat", 3, NoteheadStyle.XCircle, 46),
        ["lowmidtom"] = new("lowmidtom", 0, NoteheadStyle.Default, 47),
        ["himidtom"] = new("himidtom", 2, NoteheadStyle.Default, 48),
        ["crashcymbala"] = new("crashcymbala", 5, NoteheadStyle.XCircle, 49),
        ["crashcymbal"] = new("crashcymbal", 5, NoteheadStyle.XCircle, 49),
        ["hightom"] = new("hightom", 4, NoteheadStyle.Default, 50),
        ["ridecymbala"] = new("ridecymbala", 5, NoteheadStyle.Cross, 51),
        ["ridecymbal"] = new("ridecymbal", 5, NoteheadStyle.Cross, 51),
        // LP uses a mensural head here; the closest style in the font set.
        ["chinesecymbal"] = new("chinesecymbal", 5, NoteheadStyle.Cross, 52),
        ["ridebell"] = new("ridebell", 5, NoteheadStyle.Default, 53),
        ["tambourine"] = new("tambourine", 0, NoteheadStyle.Default, 54),
        ["splashcymbal"] = new("splashcymbal", 5, NoteheadStyle.Diamond, 55),
        ["cowbell"] = new("cowbell", 5, NoteheadStyle.Triangle, 56),
        ["crashcymbalb"] = new("crashcymbalb", 5, NoteheadStyle.Cross, 57),
        ["vibraslap"] = new("vibraslap", 4, NoteheadStyle.Diamond, 58),
        ["ridecymbalb"] = new("ridecymbalb", 5, NoteheadStyle.Cross, 59),
    };

    // Alias → canonical (LP drumPitchNames short forms).
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["bda"] = "acousticbassdrum",
        ["bd"] = "bassdrum",
        ["ss"] = "sidestick",
        ["sna"] = "acousticsnare",
        ["sn"] = "snare",
        ["hc"] = "handclap",
        ["sne"] = "electricsnare",
        ["tomfl"] = "lowfloortom",
        ["hhc"] = "closedhihat",
        ["hh"] = "hihat",
        ["tomfh"] = "highfloortom",
        ["hhp"] = "pedalhihat",
        ["hhs"] = "splashhihat",
        ["toml"] = "lowtom",
        ["hho"] = "openhihat",
        ["hhho"] = "halfopenhihat",
        ["tomml"] = "lowmidtom",
        ["tommh"] = "himidtom",
        ["cymca"] = "crashcymbala",
        ["cymc"] = "crashcymbal",
        ["tomh"] = "hightom",
        ["cymra"] = "ridecymbala",
        ["cymr"] = "ridecymbal",
        ["cymch"] = "chinesecymbal",
        ["rb"] = "ridebell",
        ["tamb"] = "tambourine",
        ["cyms"] = "splashcymbal",
        ["cb"] = "cowbell",
        ["cymcb"] = "crashcymbalb",
        ["vibs"] = "vibraslap",
        ["cymrb"] = "ridecymbalb",
    };

    /// <summary>All alias → canonical-name pairs (for completion lists).</summary>
    public static IEnumerable<KeyValuePair<string, string>> AliasEntries => Aliases;

    /// <summary>All canonical entries (for completion lists).</summary>
    public static IEnumerable<KeyValuePair<string, DrumInfo>> CanonicalEntries => Canonical;

    /// <summary>Whether the identifier names a drum (alias or full name).</summary>
    public static bool Contains(string name) =>
        Canonical.ContainsKey(name) || Aliases.ContainsKey(name);

    /// <summary>Resolves a drum name (alias or full) to its info.</summary>
    public static bool TryGet(string name, out DrumInfo info)
    {
        if (Aliases.TryGetValue(name, out var full))
            name = full;
        return Canonical.TryGetValue(name, out info);
    }
}
