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

/// <summary>
/// Maps a key signature to the diatonic spelling of each note letter — e.g. in
/// G major (one sharp) the letter <c>f</c> spells <c>fis</c>. Shared by the
/// renderer's accidental logic and by the editor's key-aware note completions.
/// </summary>
/// <remarks>
/// LILYPOND-REF: lily/accidental-engraver.cc
/// Sharp order: F C G D A E B (steps 3,0,4,1,5,2,6).
/// Flat order:  B E A D G C F (steps 6,2,5,1,4,0,3).
/// </remarks>
public static class KeySpelling
{
    private static readonly int[] SharpOrder = { 3, 0, 4, 1, 5, 2, 6 };
    private static readonly int[] FlatOrder = { 6, 2, 5, 1, 4, 0, 3 };

    /// <summary>
    /// The steps a signature writes, in PRINT order — sharps F C G D A E B, flats B E A D G
    /// C F. Published so the drawer and the reservation walk the one array instead of each
    /// keeping a copy (HANDOFF §5.2.1②; SharedRenderer held a second spelling of both until
    /// 2026-08-24).
    /// </summary>
    /// <remarks>LILYPOND-REF: scm/define-grobs.scm KeySignature — sharp-positions /
    /// flat-positions are indexed in this order.</remarks>
    public static IReadOnlyList<int> PrintOrder(int sharps) => sharps > 0 ? SharpOrder : FlatOrder;

    /// <summary>
    /// How far each note LETTER sits from C on the circle of fifths: F −1, C 0, G 1, D 2,
    /// A 3, E 4, B 5.
    /// </summary>
    private static readonly Dictionary<char, int> LetterFifths = new()
    {
        ['f'] = -1, ['c'] = 0, ['g'] = 1, ['d'] = 2, ['a'] = 3, ['e'] = 4, ['b'] = 5,
    };

    /// <summary>
    /// Where a tonic SPELLING sits on the circle of fifths — 0 for C, 7 for C sharp, −7 for
    /// C flat — or null if it is not a note name at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LILYPOND-REF: ly/music-functions-init.ly — the <c>key</c> music function is
    /// <c>ly:music-transpose</c> of a C-based <c>pitch-alist</c> by the tonic pitch.
    /// </para>
    /// <para>
    /// ⚠️ THIS IS A RULE AND NOT A TABLE, because LilyPond's is not a table. <c>\key</c> is a
    /// music function that TRANSPOSES a C-based scale —
    /// <c>key = … (ly:music-transpose (make-music 'KeyChangeEvent 'tonic (ly:make-pitch 0 0 0)
    /// 'pitch-alist pitch-alist) tonic)</c> — so every tonic LilyPond can spell has a
    /// signature, however far round the circle it lands. Until 2026-08-24 this was a
    /// fifteen-entry dictionary (c…cis, f…ces) and every tonic outside it returned null,
    /// which all eight callers coerced to 0: <c>key gis major</c> engraved as C major and
    /// said nothing.
    /// </para>
    /// <para>
    /// ⚠️ HANDOFF §7.6 ⒝ — derived from LilyPond, not literal. LilyPond carries the signature
    /// as a seven-pair alteration alist and this carries it as a signed count of fifths;
    /// spelling it literally means giving <c>KeySignature</c> the alist, which is the next
    /// island. The two are equivalent for every tonic LilyPond accepts, VERIFIED against real
    /// LilyPond 2.26.0 over 26 tonics × 4 modes = 104 signatures with 0 mismatches
    /// (audit/lp-geometry/probes/key-signature-wrap.ly holds four of them as ledger points).
    /// </para>
    /// <para>
    /// ⚠️ IT DECODES THE SPELLING RATHER THAN TRUSTING THE CALLER TO NORMALIZE. Of the eight
    /// callers only two passed <c>PitchSyntax.PitchName</c> (the page and the LilyPond twin);
    /// six passed raw token text, so LilyPond's Dutch contractions never reached the table —
    /// measured 2026-08-24: <c>key es major</c> DREW three flats and EXPORTED
    /// <c>&lt;fifths&gt;0&lt;/fifths&gt;</c>, one book with two different keys in two outputs.
    /// Normalizing here rather than at six call sites is the one-home fix (§5.2.1⑤). The
    /// LilyPondExporter's own comment had named this trap and fixed only itself.
    /// </para>
    /// </remarks>
    public static int? TonicFifths(string tonic)
    {
        string t = tonic.ToLowerInvariant().Trim();
        // Octave marks are meaningless on a tonic but writable, and dropping them silently
        // used to zero the key (`key ees, major` exported fifths 0). Cut them, do not ignore
        // the rest of the word.
        t = t.TrimEnd('\'', ',');
        if (t.Length == 0) return null;
        // The two contractions PitchSyntax.PitchName normalizes, applied to the same two
        // letters and no others: `bes` is B flat but `bs` is not a note.
        if (t == "es") t = "ees";
        else if (t == "as") t = "aes";
        if (!LetterFifths.TryGetValue(t[0], out int fifths)) return null;
        // A sharp is seven fifths up and a flat seven down, so a double is fourteen. The
        // suffixes are exactly the ones the language lexes on a NOTE (PitchSyntax.Accidental);
        // the quarter-tone spellings are deliberately absent — a quarter-tone key signature is
        // not a thing either engine engraves, and reading `cih` as C would be the silence this
        // method exists to end.
        int alteration = t[1..] switch
        {
            "" => 0,
            "is" => 1,
            "isis" => 2,
            "es" => -1,
            "eses" => -2,
            _ => int.MinValue,
        };
        return alteration == int.MinValue ? null : fifths + 7 * alteration;
    }

    /// <summary>
    /// Number of sharps (positive) or flats (negative) for the given key, or null
    /// if the tonic is not a note name. Only <c>minor</c> shifts the relative
    /// major down three on the circle of fifths; any other mode is read as major.
    /// </summary>
    /// <remarks>
    /// ⚠️ THE RESULT IS NOT BOUNDED BY SEVEN and must not be clamped by its readers:
    /// C-sharp lydian is eight and C-flat locrian is twelve. See <see cref="Alteration"/>,
    /// which turns a count past seven into the double accidentals LilyPond prints, and
    /// <see cref="TonicFifths"/> for why the tonic is computed rather than looked up.
    /// </remarks>
    public static int? SharpsFor(string tonic, string mode)
    {
        if (TonicFifths(tonic) is not int sharps)
            return null;
        // Church-mode offsets from the major (ionian) signature on the same
        // tonic: each step down the circle of fifths removes one sharp.
        // LILYPOND-REF: ly/scale-definitions-init.ly — major/minor/ionian…locrian.
        sharps += mode.ToLowerInvariant() switch
        {
            "lydian" => 1,
            "mixolydian" => -1,
            "dorian" => -2,
            "minor" or "aeolian" => -3,
            "phrygian" => -4,
            "locrian" => -5,
            _ => 0, // major / ionian
        };
        return sharps;
    }

    /// <summary>
    /// The alteration the key signature gives a diatonic step (c=0, d=1, … b=6):
    /// 0 natural, ±1 single sharp/flat, ±2 double. Keys past 7 accidentals wrap the
    /// order and double the first step(s) — e.g. C-sharp lydian (8 sharps) double-
    /// sharps F (fisis), so this returns 2 for step F. The old loop capped at 7 and
    /// silently dropped the 8th accidental.
    /// </summary>
    public static int Alteration(int step, int sharps)
    {
        if (sharps > 0)
        {
            int pos = System.Array.IndexOf(SharpOrder, step);
            // The step recurs at pos, pos+7, pos+14, … in the repeated sharp order;
            // count how many of those fall within the first `sharps` accidentals.
            return pos >= 0 && pos < sharps ? (sharps - pos - 1) / 7 + 1 : 0;
        }
        if (sharps < 0)
        {
            int flatCount = -sharps;
            int pos = System.Array.IndexOf(FlatOrder, step);
            return pos >= 0 && pos < flatCount ? -((flatCount - pos - 1) / 7 + 1) : 0;
        }
        return 0;
    }

    /// <summary>
    /// The (step, alteration) pairs a key signature writes, in print order — the same list
    /// LilyPond hands KeySignature as <c>alteration-alist</c>, with the zero entries dropped.
    /// At most SEVEN pairs however far round the circle the key sits, because there are seven
    /// letters; past seven accidentals the leading letters double instead of the list growing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LILYPOND-REF: ly/music-functions-init.ly — <c>key</c> is <c>ly:music-transpose</c> of a
    /// seven-pair C-based pitch-alist, so the alist is ALWAYS seven pairs and an entry may hold
    /// a whole tone (a double accidental); scm/output-lib.scm
    /// key-signature-interface::alteration-positions places the non-zero ones.
    /// </para>
    /// <para>
    /// ⚠️ ONE WALK, read by the drawer (SharedRenderer.KeySignatureGlyphs) and by the
    /// reservation (SpacingRules.KeySignatureInkWidth). Both used to spell
    /// <c>min(|sharps|, 7)</c> for themselves and would have had to grow the doubles twice
    /// — HANDOFF §5.0's "placement and reservation are ONE claim".
    /// </para>
    /// <para>
    /// For |sharps| ≤ 7 this yields exactly the first |sharps| steps of the print order with
    /// alteration ±1, which is what the capped loops produced: the port is byte-identical
    /// below the wrap and only differs above it.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<(int Step, int Alter)> SignatureSteps(int sharps)
    {
        var list = new List<(int, int)>(7);
        if (sharps == 0) return list;
        foreach (int step in PrintOrder(sharps))
        {
            int alter = Alteration(step, sharps);
            if (alter != 0) list.Add((step, alter));
        }
        return list;
    }

    /// <summary>The diatonic step index (0–6) of a note letter a–g.</summary>
    public static int StepOf(char letter) => char.ToLowerInvariant(letter) switch
    {
        'c' => 0, 'd' => 1, 'e' => 2, 'f' => 3, 'g' => 4, 'a' => 5, 'b' => 6, _ => -1
    };

    /// <summary>
    /// Spells a note letter a–g as it sounds under the given key signature, using
    /// the parser's accidental suffixes (<c>is</c> sharp, <c>es</c> flat) — e.g.
    /// <c>('f', 1)</c> → <c>"fis"</c>, <c>('b', -1)</c> → <c>"bes"</c>.
    /// </summary>
    public static string SpellLetter(char letter, int sharps)
    {
        char lower = char.ToLowerInvariant(letter);
        int step = StepOf(lower);
        if (step < 0) return letter.ToString();
        return Alteration(step, sharps) switch
        {
            >= 2 => lower + "isis",  // double sharp (keys past 7 sharps)
            1 => lower + "is",
            -1 => lower + "es",
            <= -2 => lower + "eses", // double flat (keys past 7 flats)
            _ => lower.ToString()
        };
    }
}
