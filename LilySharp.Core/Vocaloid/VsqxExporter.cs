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

using System.Xml.Linq;
using LilySharp.Core.MusicXml;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Vocaloid;

/// <summary>
/// Exports the piece as a VOCALOID4 sequence (.vsqx) — the format Piapro
/// Studio (初音ミク V4X) and the VOCALOID4/5 editors import directly.
/// </summary>
/// <remarks>
/// Built on top of the MusicXML export: the first part carrying lyrics is
/// the vocal line. Notes become vsq4 &lt;note&gt; events (resolution 480),
/// rests become gaps, tied notes merge (VSQX has no ties), and each note
/// gets its syllable with a VOCALOID Japanese phoneme transcription (kana →
/// phoneme table; a "-" lyric continues the previous vowel on melisma
/// notes; non-kana lyrics keep their text with a neutral phoneme, which the
/// editor re-analyzes when the lyric is touched).
/// The singer entry is the neutral VY1V4 bank; Piapro Studio remaps it to
/// the installed voice (e.g. Miku) on import.
/// </remarks>
public sealed class VsqxExporter
{
    private const int Resolution = 480;                 // vsq4 ticks per quarter
    private const int DivisionsPerQuarter = 24;         // MusicXML divisions (MusicXmlExporter)
    private const int TickFactor = Resolution / DivisionsPerQuarter;

    /// <summary>Exports the piece's vocal line as a VOCALOID4 (.vsqx) document.</summary>
    public XDocument Export(SyntaxTree tree)
    {
        var xml = new MusicXmlExporter().Export(tree);

        // The vocal line: first part with lyrics, else the first part.
        var part = xml.Parts.FirstOrDefault(p =>
                       p.Measures.Any(m => m.Notes.Any(n => n.Lyrics.Count > 0)))
                   ?? xml.Parts.FirstOrDefault();

        // Tempo / meter from the source (the MusicXML model keeps them per
        // measure; the master track wants the opening values).
        int bpm = 120, timeNu = 4, timeDe = 4;
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            if (node is TempoDeclarationSyntax tempo && tempo.Bpm is int b) { bpm = b; break; }
        }
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            if (node is TimeSignatureSyntax ts)
            {
                timeNu = ts.Beats;
                timeDe = ts.BeatType;
                break;
            }
        }

        var notes = new List<(int Tick, int Dur, int Pitch, string Lyric)>();
        if (part != null)
        {
            int tick = 0;
            foreach (var measure in part.Measures)
            {
                foreach (var n in measure.Notes)
                {
                    if (n.IsGrace || n.IsChord)
                        continue; // no metric time / stacked pitch — not a vocal event
                    int dur = n.Duration * TickFactor;
                    if (n.IsRest)
                    {
                        tick += dur;
                        continue;
                    }
                    if (n.TieStop && notes.Count > 0)
                    {
                        // VSQX has no ties: extend the previous note.
                        var prev = notes[^1];
                        notes[^1] = (prev.Tick, prev.Dur + dur, prev.Pitch, prev.Lyric);
                        tick += dur;
                        continue;
                    }
                    string lyric = n.Lyrics.FirstOrDefault(l => l.Verse == 1).Text ?? "-";
                    if (string.IsNullOrEmpty(lyric))
                        lyric = "-"; // melisma: continue the previous vowel
                    int pitch = MidiPitch(n.Step, (int)Math.Round(n.Alter ?? 0), n.Octave ?? 4);
                    notes.Add((tick, dur, pitch, lyric));
                    tick += dur;
                }
            }
        }

        // A part whose extra voices were serialized after the melody would
        // append a lyric-less tail: keep nothing after the last sung syllable
        // (melismas BETWEEN syllables survive; the alto line does not).
        int lastLyric = -1;
        for (int i = 0; i < notes.Count; i++)
            if (notes[i].Lyric != "-")
                lastLyric = i;
        if (lastLyric >= 0 && lastLyric < notes.Count - 1)
            notes.RemoveRange(lastLyric + 1, notes.Count - lastLyric - 1);

        // Pre-measure (1 empty bar, Piapro convention) shifts the part start.
        int preMeasureTicks = timeNu * Resolution * 4 / timeDe;
        int playTime = notes.Count > 0 ? notes[^1].Tick + notes[^1].Dur : Resolution * 4;

        XNamespace ns = "http://www.yamaha.co.jp/vocaloid/schema/vsq4/";

        var vsPart = new XElement(ns + "vsPart",
            new XElement(ns + "t", preMeasureTicks),
            new XElement(ns + "playTime", playTime),
            new XElement(ns + "name", new XCData(xml.Title ?? "part")),
            new XElement(ns + "comment", new XCData("")),
            new XElement(ns + "sPlug",
                new XElement(ns + "id", new XCData("ACA9C502-A04B-42b5-B2EB-5CEA36D16FCE")),
                new XElement(ns + "name", new XCData("VOCALOID2 Compatible Style")),
                new XElement(ns + "version", new XCData("3.0.0.1"))),
            PStyle(ns),
            new XElement(ns + "singer",
                new XElement(ns + "t", 0),
                new XElement(ns + "bs", 0),
                new XElement(ns + "pc", 0)));

        foreach (var (t, dur, pitch, lyric) in notes)
        {
            vsPart.Add(new XElement(ns + "note",
                new XElement(ns + "t", t),
                new XElement(ns + "dur", dur),
                new XElement(ns + "n", pitch),
                new XElement(ns + "v", 64),
                new XElement(ns + "y", new XCData(lyric)),
                new XElement(ns + "p", new XCData(Phoneme(lyric))),
                NStyle(ns)));
        }

        var root = new XElement(ns + "vsq4",
            new XElement(ns + "vender", new XCData("Yamaha corporation")),
            new XElement(ns + "version", new XCData("4.0.0.3")),
            new XElement(ns + "vVoiceTable",
                new XElement(ns + "vVoice",
                    new XElement(ns + "bs", 0),
                    new XElement(ns + "pc", 0),
                    new XElement(ns + "id", new XCData("BETDB8W6KWZPYEB9")),
                    new XElement(ns + "name", new XCData("VY1V4")),
                    new XElement(ns + "vPrm",
                        new XElement(ns + "bre", 0),
                        new XElement(ns + "bri", 0),
                        new XElement(ns + "cle", 0),
                        new XElement(ns + "gen", 0),
                        new XElement(ns + "ope", 0)))),
            new XElement(ns + "mixer",
                new XElement(ns + "masterUnit",
                    new XElement(ns + "oDev", 0),
                    new XElement(ns + "rLvl", 0),
                    new XElement(ns + "vol", 0)),
                new XElement(ns + "vsUnit",
                    new XElement(ns + "tNo", 0),
                    new XElement(ns + "iGin", 0),
                    new XElement(ns + "sLvl", -898),
                    new XElement(ns + "sEnable", 0),
                    new XElement(ns + "m", 0),
                    new XElement(ns + "s", 0),
                    new XElement(ns + "pan", 64),
                    new XElement(ns + "vol", 0)),
                new XElement(ns + "monoUnit",
                    new XElement(ns + "iGin", 0),
                    new XElement(ns + "sLvl", -898),
                    new XElement(ns + "sEnable", 0),
                    new XElement(ns + "m", 0),
                    new XElement(ns + "s", 0),
                    new XElement(ns + "pan", 64),
                    new XElement(ns + "vol", 0)),
                new XElement(ns + "stUnit",
                    new XElement(ns + "iGin", 0),
                    new XElement(ns + "m", 0),
                    new XElement(ns + "s", 0),
                    new XElement(ns + "vol", -129))),
            new XElement(ns + "masterTrack",
                new XElement(ns + "seqName", new XCData(xml.Title ?? "score")),
                new XElement(ns + "comment", new XCData("")),
                new XElement(ns + "resolution", Resolution),
                new XElement(ns + "preMeasure", 1),
                new XElement(ns + "timeSig",
                    new XElement(ns + "m", 0),
                    new XElement(ns + "nu", timeNu),
                    new XElement(ns + "de", timeDe)),
                new XElement(ns + "tempo",
                    new XElement(ns + "t", 0),
                    new XElement(ns + "v", bpm * 100))),
            new XElement(ns + "vsTrack",
                new XElement(ns + "tNo", 0),
                new XElement(ns + "name", new XCData(part?.Name ?? "Track")),
                new XElement(ns + "comment", new XCData("")),
                vsPart),
            new XElement(ns + "monoTrack"),
            new XElement(ns + "stTrack"),
            new XElement(ns + "aux",
                new XElement(ns + "id", new XCData("AUX_VST_HOST_CHUNK_INFO")),
                new XElement(ns + "content", new XCData("VlNDSw=="))));

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    private static XElement PStyle(XNamespace ns) =>
        new(ns + "pStyle",
            StyleV(ns, "accent", 50), StyleV(ns, "bendDep", 8), StyleV(ns, "bendLen", 0),
            StyleV(ns, "decay", 50), StyleV(ns, "fallPort", 0), StyleV(ns, "opening", 127),
            StyleV(ns, "risePort", 0));

    private static XElement NStyle(XNamespace ns) =>
        new(ns + "nStyle",
            StyleV(ns, "accent", 50), StyleV(ns, "bendDep", 8), StyleV(ns, "bendLen", 0),
            StyleV(ns, "decay", 50), StyleV(ns, "fallPort", 0), StyleV(ns, "opening", 127),
            StyleV(ns, "risePort", 0), StyleV(ns, "vibLen", 0), StyleV(ns, "vibType", 0));

    private static XElement StyleV(XNamespace ns, string id, int v) =>
        new(ns + "v", new XAttribute("id", id), v);

    private static int MidiPitch(string? step, int alter, int octave)
    {
        int baseSemitone = step switch
        {
            "C" => 0, "D" => 2, "E" => 4, "F" => 5, "G" => 7, "A" => 9, "B" => 11, _ => 0
        };
        return Math.Clamp((octave + 1) * 12 + baseSemitone + alter, 0, 127);
    }

    /// <summary>
    /// VOCALOID Japanese phoneme for a syllable: kana (hira/katakana, 拗音
    /// digraphs included) transcribe via the table; "-" continues the
    /// previous vowel; anything else gets a neutral "a" the editor replaces
    /// when the lyric is edited.
    /// </summary>
    internal static string Phoneme(string lyric)
    {
        var s = lyric.Trim().TrimEnd('。', '、', ',', '.', '!', '?', '！', '？');
        if (s.Length == 0 || s == "-" || s == "ー" || s == "−")
            return "-";

        // Katakana → hiragana (same phonemes).
        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (chars[i] >= 'ァ' && chars[i] <= 'ヶ')
                chars[i] = (char)(chars[i] - 0x60);
        s = new string(chars);

        // Digraphs (きゃ …) first, then single kana.
        if (s.Length >= 2 && KanaPhonemes.TryGetValue(s[..2], out var ph2))
            return ph2;
        if (KanaPhonemes.TryGetValue(s[..1], out var ph1))
            return ph1;
        return "a";
    }

    /// <remarks>VOCALOID Japanese phoneme set (X-SAMPA-like).</remarks>
    private static readonly Dictionary<string, string> KanaPhonemes = new()
    {
        ["あ"] = "a", ["い"] = "i", ["う"] = "M", ["え"] = "e", ["お"] = "o",
        ["か"] = "k a", ["き"] = "k' i", ["く"] = "k M", ["け"] = "k e", ["こ"] = "k o",
        ["が"] = "g a", ["ぎ"] = "g' i", ["ぐ"] = "g M", ["げ"] = "g e", ["ご"] = "g o",
        ["さ"] = "s a", ["し"] = "S i", ["す"] = "s M", ["せ"] = "s e", ["そ"] = "s o",
        ["ざ"] = "dz a", ["じ"] = "dZ i", ["ず"] = "dz M", ["ぜ"] = "dz e", ["ぞ"] = "dz o",
        ["た"] = "t a", ["ち"] = "tS i", ["つ"] = "ts M", ["て"] = "t e", ["と"] = "t o",
        ["だ"] = "d a", ["ぢ"] = "dZ i", ["づ"] = "dz M", ["で"] = "d e", ["ど"] = "d o",
        ["な"] = "n a", ["に"] = "J i", ["ぬ"] = "n M", ["ね"] = "n e", ["の"] = "n o",
        ["は"] = "h a", ["ひ"] = "C i", ["ふ"] = @"p\ M", ["へ"] = "h e", ["ほ"] = "h o",
        ["ば"] = "b a", ["び"] = "b' i", ["ぶ"] = "b M", ["べ"] = "b e", ["ぼ"] = "b o",
        ["ぱ"] = "p a", ["ぴ"] = "p' i", ["ぷ"] = "p M", ["ぺ"] = "p e", ["ぽ"] = "p o",
        ["ま"] = "m a", ["み"] = "m' i", ["む"] = "m M", ["め"] = "m e", ["も"] = "m o",
        ["や"] = "j a", ["ゆ"] = "j M", ["よ"] = "j o",
        ["ら"] = "4 a", ["り"] = "4' i", ["る"] = "4 M", ["れ"] = "4 e", ["ろ"] = "4 o",
        ["わ"] = "w a", ["ゐ"] = "i", ["ゑ"] = "e", ["を"] = "o", ["ん"] = @"N\",
        ["ゔ"] = "v M",
        ["きゃ"] = "k' a", ["きゅ"] = "k' M", ["きょ"] = "k' o",
        ["ぎゃ"] = "g' a", ["ぎゅ"] = "g' M", ["ぎょ"] = "g' o",
        ["しゃ"] = "S a", ["しゅ"] = "S M", ["しょ"] = "S o",
        ["じゃ"] = "dZ a", ["じゅ"] = "dZ M", ["じょ"] = "dZ o",
        ["ちゃ"] = "tS a", ["ちゅ"] = "tS M", ["ちょ"] = "tS o",
        ["にゃ"] = "J a", ["にゅ"] = "J M", ["にょ"] = "J o",
        ["ひゃ"] = "C a", ["ひゅ"] = "C M", ["ひょ"] = "C o",
        ["びゃ"] = "b' a", ["びゅ"] = "b' M", ["びょ"] = "b' o",
        ["ぴゃ"] = "p' a", ["ぴゅ"] = "p' M", ["ぴょ"] = "p' o",
        ["みゃ"] = "m' a", ["みゅ"] = "m' M", ["みょ"] = "m' o",
        ["りゃ"] = "4' a", ["りゅ"] = "4' M", ["りょ"] = "4' o",
        ["ふぁ"] = @"p\ a", ["ふぃ"] = @"p\' i", ["ふぇ"] = @"p\ e", ["ふぉ"] = @"p\ o",
        ["てぃ"] = "t' i", ["でぃ"] = "d' i", ["とぅ"] = "t M", ["どぅ"] = "d M",
        ["うぃ"] = "w i", ["うぇ"] = "w e", ["うぉ"] = "w o",
        ["ゔぁ"] = "v a", ["ゔぃ"] = "v' i", ["ゔぇ"] = "v e", ["ゔぉ"] = "v o",
    };
}
