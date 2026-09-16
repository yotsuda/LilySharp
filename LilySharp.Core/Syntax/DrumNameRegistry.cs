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
/// <param name="Mark">Auto articulation from LilyPond's style table — one of
/// <see cref="DrumNameRegistry.MarkWords"/>, or null for no mark.</param>
public readonly record struct DrumInfo(
    string FullName, int StaffPosition, NoteheadStyle Notehead, int GmKey,
    // Auto articulation from LilyPond's style table: "stopped" (+) on the closed hi-hat and
    // the muted hand drums, "open" (○) on the open ones, "staccato"/"tenuto" on the short
    // and long guiro.
    string? Mark = null);

/// <summary>
/// The drum-note vocabulary: bare identifiers in a music stream that name percussion
/// instruments (<c>bd4 sn8 hh</c>). Names and aliases are LilyPond's (drumPitchNames);
/// placement and noteheads follow its style tables; MIDI keys come from midiDrumPitches.
/// </summary>
/// <remarks>
/// LILYPOND-REF: ly/drumpitch-init.ly — drumPitchNames (aliases),
/// midiDrumPitches (GM keys) and the drums-style hash table
/// (name → (notehead-style articulation staff-position)).
/// <para>
/// ⚠️ WHICH STYLE TABLE A ROW COMES FROM. LilyPond keeps several, and the same name sits in
/// more than one with a DIFFERENT position (hihat is 3 in <c>drums-style</c> and 5 in
/// <c>weinberg-drums-style</c>). The rule here, in order: the KIT's own row from
/// <c>drums-style</c>; else the pair table the instrument belongs to
/// (<c>bongos-style</c>, <c>congas-style</c>, <c>timbales-style</c>), which is the only
/// place LilyPond ever places those and is what keeps a hi/lo pair two rows apart; else
/// <c>percussion-style</c> (every row there is position 0); else — for the eight LilyPond
/// gives no row at all, the agogos, the whistles, the woodblocks and the cuicas — the
/// middle line with the default head, which is LilyPond's own fallback and what
/// <c>tambourine</c> has always had here.
/// </para>
/// <para>
/// ⚠️ <c>tt</c> is NOT here, and it is LilyPond's one dangling name: drumPitchNames maps it
/// to <c>tamtam</c>, which appears in no style table, in no midiDrumPitches row, and not
/// even in drumPitchNames' own canonical half. Writing it in LilyPond names an instrument
/// that has no pitch, no notehead and no place. Sixty-three canonical names and sixty-FOUR
/// aliases is how the file says so (2026-09-13).
/// </para>
/// ⚠️ EVERY NAME HERE IS TAKEN OUT OF THE PHRASE NAMESPACE. This said the vocabulary claims
/// FREE grammar space, because bare identifiers were otherwise invalid in a music stream and
/// a phrase was referenced with a '$' sigil; that stopped being true when the bare spelling
/// landed. Parser.Music.cs sends a plain Identifier to ParseDrumNote whenever
/// <see cref="Contains"/> says so, UNCONDITIONALLY — it does not ask whether the part is a
/// kit — so a phrase named bd, sn or hh could never be played. The writer is told, though,
/// and at the right end: LYS1030 refuses the DECLARATION
/// (Parser.Directives.RejectUnreachablePhraseName), reading its set from this registry, so
/// growing this table grows that refusal in step. MEASURED 2026-09-13, when the table went
/// from 30 names to 63 — DrumTableMatchesLilyPondTests
/// .EveryNewDrumNameIsTakenOutOfThePhraseNamespace_Loudly is the net.
/// </remarks>
public static class DrumNameRegistry
{
    // FullName → info, in GM-key order, which is also the order LilyPond's midiDrumPitches
    // walks. See the class remark for WHICH LilyPond style table each row comes from.
    private static readonly Dictionary<string, DrumInfo> Canonical = new(StringComparer.Ordinal)
    {
        ["acousticbassdrum"] = new("acousticbassdrum", -3, NoteheadStyle.Default, 35),
        ["bassdrum"] = new("bassdrum", -3, NoteheadStyle.Default, 36),
        // Three names, one GM key 37 (Side Stick), and LilyPond means that: the hi/lo pair is
        // the timbale player's rim, placed by timbales-style, while the kit's own is placed
        // by drums-style. `hisidestick` lands on the kit's row exactly, which is why the
        // perturbation net's allow-list carries it.
        ["hisidestick"] = new("hisidestick", 1, NoteheadStyle.Cross, 37),
        ["sidestick"] = new("sidestick", 1, NoteheadStyle.Cross, 37),
        ["losidestick"] = new("losidestick", -1, NoteheadStyle.Cross, 37),
        ["acousticsnare"] = new("acousticsnare", 1, NoteheadStyle.Default, 38),
        ["snare"] = new("snare", 1, NoteheadStyle.Default, 38),
        ["handclap"] = new("handclap", 1, NoteheadStyle.Triangle, 39),
        ["electricsnare"] = new("electricsnare", 1, NoteheadStyle.Default, 40),
        ["lowfloortom"] = new("lowfloortom", -4, NoteheadStyle.Default, 41),
        ["closedhihat"] = new("closedhihat", 3, NoteheadStyle.Cross, 42, "stopped"),
        ["hihat"] = new("hihat", 3, NoteheadStyle.Cross, 42),
        ["highfloortom"] = new("highfloortom", -2, NoteheadStyle.Default, 43),
        ["pedalhihat"] = new("pedalhihat", -5, NoteheadStyle.Cross, 44),
        // ⚠️ `splashhihat` / `hhs` stood here until 2026-09-12 — a name LilyPond does not
        // have (ly/drumpitch-init.ly knows `splashcymbal` / `cyms`, a CYMBAL, and no splash
        // hi-hat), carrying a row byte-identical to pedalhihat's: same staff position, same
        // notehead, same GM key 44, which IS Pedal Hi-Hat. So the popup offered `hhs` for a
        // splash and the page drew — and the .mid played — a pedal hi-hat. The real splash
        // is two rows down (`splashcymbal`, position 5, Diamond, GM 55), so nothing was lost
        // by removing it: found by the perturbation net (two names, one signature), and
        // written in 0 of the 27,095 .lys on this machine.
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

        // ── Latin and accessory percussion (added 2026-09-13) ─────────────────────────
        // The hand drums come in hi/lo pairs that LilyPond places two rows apart, each in
        // three states — muted, plain, open — sharing ONE GM key, which is how General MIDI
        // spells them too. The state is the articulation, so the three are never one row.
        ["mutehibongo"] = new("mutehibongo", 1, NoteheadStyle.Default, 60, "stopped"),
        ["hibongo"] = new("hibongo", 1, NoteheadStyle.Default, 60),
        ["openhibongo"] = new("openhibongo", 1, NoteheadStyle.Default, 60, "open"),
        ["mutelobongo"] = new("mutelobongo", -1, NoteheadStyle.Default, 61, "stopped"),
        ["lobongo"] = new("lobongo", -1, NoteheadStyle.Default, 61),
        ["openlobongo"] = new("openlobongo", -1, NoteheadStyle.Default, 61, "open"),
        // ⚠️ Both muted congas are GM 62 — LilyPond's midiDrumPitches gives the hi and the lo
        // the same key, and General MIDI has only one "Mute Hi Conga". The staff keeps them
        // apart; the .mid cannot, and that is not ours to fix.
        ["mutehiconga"] = new("mutehiconga", 1, NoteheadStyle.Default, 62, "stopped"),
        ["muteloconga"] = new("muteloconga", -1, NoteheadStyle.Default, 62, "stopped"),
        ["openhiconga"] = new("openhiconga", 1, NoteheadStyle.Default, 63, "open"),
        ["hiconga"] = new("hiconga", 1, NoteheadStyle.Default, 63),
        ["openloconga"] = new("openloconga", -1, NoteheadStyle.Default, 64, "open"),
        ["loconga"] = new("loconga", -1, NoteheadStyle.Default, 64),
        ["hitimbale"] = new("hitimbale", 1, NoteheadStyle.Default, 65),
        ["lotimbale"] = new("lotimbale", -1, NoteheadStyle.Default, 66),
        // No style row in LilyPond at all, for these eight: the middle line, default head.
        ["hiagogo"] = new("hiagogo", 0, NoteheadStyle.Default, 67),
        ["loagogo"] = new("loagogo", 0, NoteheadStyle.Default, 68),
        ["cabasa"] = new("cabasa", 0, NoteheadStyle.Cross, 69),
        ["maracas"] = new("maracas", 0, NoteheadStyle.Default, 70),
        ["shortwhistle"] = new("shortwhistle", 0, NoteheadStyle.Default, 71),
        ["longwhistle"] = new("longwhistle", 0, NoteheadStyle.Default, 72),
        // The guiro's three names share one row and one head; LilyPond tells them apart by
        // articulation alone (and gives the short one its own GM key, 73).
        ["shortguiro"] = new("shortguiro", 0, NoteheadStyle.Default, 73, "staccato"),
        ["longguiro"] = new("longguiro", 0, NoteheadStyle.Default, 74, "tenuto"),
        ["guiro"] = new("guiro", 0, NoteheadStyle.Default, 74),
        ["claves"] = new("claves", 0, NoteheadStyle.Default, 75),
        ["hiwoodblock"] = new("hiwoodblock", 0, NoteheadStyle.Default, 76),
        ["lowoodblock"] = new("lowoodblock", 0, NoteheadStyle.Default, 77),
        ["mutecuica"] = new("mutecuica", 0, NoteheadStyle.Default, 78),
        ["opencuica"] = new("opencuica", 0, NoteheadStyle.Default, 79),
        ["mutetriangle"] = new("mutetriangle", 0, NoteheadStyle.Cross, 80, "stopped"),
        ["triangle"] = new("triangle", 0, NoteheadStyle.Cross, 81),
        ["opentriangle"] = new("opentriangle", 0, NoteheadStyle.Cross, 81, "open"),
    };

    // Alias → canonical (LP drumPitchNames short forms).
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["bda"] = "acousticbassdrum",
        ["bd"] = "bassdrum",
        ["ssh"] = "hisidestick",
        ["ss"] = "sidestick",
        ["ssl"] = "losidestick",
        ["sna"] = "acousticsnare",
        ["sn"] = "snare",
        ["hc"] = "handclap",
        ["sne"] = "electricsnare",
        ["tomfl"] = "lowfloortom",
        ["hhc"] = "closedhihat",
        ["hh"] = "hihat",
        ["tomfh"] = "highfloortom",
        ["hhp"] = "pedalhihat",
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
        ["bohm"] = "mutehibongo",
        ["boh"] = "hibongo",
        ["boho"] = "openhibongo",
        ["bolm"] = "mutelobongo",
        ["bol"] = "lobongo",
        ["bolo"] = "openlobongo",
        ["cghm"] = "mutehiconga",
        ["cglm"] = "muteloconga",
        ["cgho"] = "openhiconga",
        ["cgh"] = "hiconga",
        ["cglo"] = "openloconga",
        ["cgl"] = "loconga",
        ["timh"] = "hitimbale",
        ["timl"] = "lotimbale",
        ["agh"] = "hiagogo",
        ["agl"] = "loagogo",
        ["cab"] = "cabasa",
        ["mar"] = "maracas",
        ["whs"] = "shortwhistle",
        ["whl"] = "longwhistle",
        ["guis"] = "shortguiro",
        ["guil"] = "longguiro",
        ["gui"] = "guiro",
        ["cl"] = "claves",
        ["wbh"] = "hiwoodblock",
        ["wbl"] = "lowoodblock",
        ["cuim"] = "mutecuica",
        ["cuio"] = "opencuica",
        ["trim"] = "mutetriangle",
        ["tri"] = "triangle",
        ["trio"] = "opentriangle",
        // `tt` (→ tamtam) is LilyPond's, and deliberately NOT ours — see the class remark.
    };

    /// <summary>
    /// Every word a drum's automatic <see cref="DrumInfo.Mark"/> can be — LilyPond's own
    /// articulation words from its style tables, and therefore also the words
    /// <c>drummap { x: mark W }</c> takes.
    /// </summary>
    /// <remarks>
    /// ⚠️ Two of these four landed with the Latin percussion (2026-09-13) and they are not
    /// decoration: <c>shortguiro</c>, <c>longguiro</c> and <c>guiro</c> sit on the SAME line
    /// with the same head, and two of them share GM 74, so the articulation is the only
    /// thing that tells a long scrape from a plain one. Without them the table would have
    /// carried two names for one row — the defect that removed <c>splashhihat</c>.
    /// </remarks>
    public static readonly IReadOnlyList<string> MarkWords = ["open", "staccato", "stopped", "tenuto"];

    /// <summary>The articulation a mark word draws, or <c>None</c> for a word that is not
    /// one. THE reading of these words — the collector and the override reader share it.</summary>
    public static ArticulationType MarkArticulation(string? mark) => mark switch
    {
        "stopped" => ArticulationType.Stopped,
        "open" => ArticulationType.Flageolet,
        "staccato" => ArticulationType.Staccato,
        "tenuto" => ArticulationType.Tenuto,
        _ => ArticulationType.None,
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

/// <summary>
/// Per-score drum-table overrides from <c>drummap { … }</c> blocks: position,
/// notehead, GM key and the auto mark of EXISTING registry names (the parser's
/// drum vocabulary is static, so new instrument names are out of scope).
/// </summary>
public static class DrumOverrides
{
    /// <summary>Builds the override map (canonical name → final info) from
    /// every drummap block in the tree; null when there are none.</summary>
    public static Dictionary<string, DrumInfo>? Build(SyntaxNode root)
        // A drummap is a compilation-unit item only (Parser.ParseTopLevelItem), so the
        // root's children are the whole answer — no walk over every note.
        => Build(root.ChildNodes().OfType<DrummapDeclarationSyntax>());

    /// <summary>Same, from an already-gathered block list (document order).
    /// The collector's definitions walk feeds this so the keystroke path does
    /// not enumerate the whole red tree a second time just to find drummaps;
    /// the tree overload above stays for the exporters, which walk once.</summary>
    public static Dictionary<string, DrumInfo>? Build(IEnumerable<DrummapDeclarationSyntax> drummaps)
    {
        Dictionary<string, DrumInfo>? map = null;
        foreach (var dm in drummaps)
        {
            foreach (var (name, _, s) in dm.Entries)
            {
                if (!DrumNameRegistry.TryGet(name, out var info))
                    continue; // unknown names are ignored (override-only scope)
                map ??= new Dictionary<string, DrumInfo>(StringComparer.Ordinal);
                if (map.TryGetValue(info.FullName, out var cur))
                    info = cur;
                if (s.TryGetValue("position", out var p) && int.TryParse(p.Text, out int pos)
                    && pos is >= -9 and <= 9)
                    info = info with { StaffPosition = pos };
                if (s.TryGetValue("notehead", out var nh))
                    info = info with
                    {
                        Notehead = nh.Text.ToLowerInvariant() switch
                        {
                            "x" or "cross" => NoteheadStyle.Cross,
                            "diamond" => NoteheadStyle.Diamond,
                            "triangle" => NoteheadStyle.Triangle,
                            "slash" => NoteheadStyle.Slash,
                            "xcircle" => NoteheadStyle.XCircle,
                            "default" => NoteheadStyle.Default,
                            _ => info.Notehead,
                        },
                    };
                if (s.TryGetValue("midi", out var m) && int.TryParse(m.Text, out int gm)
                    && gm is >= 0 and <= 127)
                    info = info with { GmKey = gm };
                if (s.TryGetValue("mark", out var mk))
                    info = info with
                    {
                        Mark = DrumNameRegistry.MarkWords.Contains(mk.Text.ToLowerInvariant())
                            ? mk.Text.ToLowerInvariant()
                            : null,
                    };
                map[info.FullName] = info;
            }
        }
        return map;
    }

    /// <summary>Registry lookup with the score's overrides applied.</summary>
    public static DrumInfo Resolve(Dictionary<string, DrumInfo>? overrides, string name)
    {
        DrumNameRegistry.TryGet(name, out var info);
        return overrides != null && overrides.TryGetValue(info.FullName, out var o) ? o : info;
    }
}
