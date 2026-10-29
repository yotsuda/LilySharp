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

using System.Collections.Generic;
using System.Linq;
using LilySharp.Core.Midi;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Syntax;

namespace LilySharp.Core.Editing;

/// <summary>
/// What a new piece's notation leaves flat, said for the AI that wrote it to revise (owner's
/// report 2026-10-26: a "dramatic string quartet" came back as four piano staves of whole notes
/// and scales, every part in the same rhythm). Not errors — a piece may mean any of it — but
/// the commonest ways a composed piece falls short of sounding like music for its players:
/// parts that play a piano for want of an <c>instrument</c>, an ensemble braced as one piano,
/// a part of one note value, and parts that all move together.
/// </summary>
public static class CompositionChecks
{
    /// <summary>The findings for <paramref name="source"/>, each one sentence; empty when none.</summary>
    public static IReadOnlyList<string> Of(string source)
    {
        var tree = SyntaxTree.Parse(source);
        if (tree.HasErrors)
            return [];
        var root = tree.GetRoot();
        var findings = new List<string>();
        var declared = RenderSpecParser.ChooseDeclared(tree, null);

        // A piece has a title of its own (owner's request 2026-10-27).
        if (TitleOf(source) is not { } title)
            findings.Add("The piece has no title: give it a title of its own that fits its music, a `title \"…\"` line at the top.");
        else if (IsPlaceholderTitle(title))
            findings.Add($"\"{title}\" is a placeholder, not a title: give the piece a title of its own that fits its music.");

        // An ensemble braced as one piano.
        var braced = new HashSet<string>(System.StringComparer.Ordinal);
        if (declared is { } d)
            foreach (var grand in d.Declaration.DescendantNodes<GrandStaffRenderSyntax>())
            {
                // One node for the three groups; the keyword says which (staffGroup and
                // choirStaff are an ensemble's brackets).
                if (grand.GrandStaffKeyword.Text != "grandStaff")
                    continue;
                var staves = grand.DescendantNodes<StaffRenderSyntax>().Select(s => s.PartToken?.Text).OfType<string>().ToList();
                // A piano's two hands need no instrument; four players braced as one still do.
                if (staves.Count <= 2)
                    braced.UnionWith(staves);
                if (staves.Count > 2)
                    findings.Add($"The score puts {staves.Count} staves ({string.Join(", ", staves)}) in one grandStaff, the brace of ONE "
                        + "instrument on two staves (a piano): an ensemble is a staffGroup { … }.");
            }

        // A part with no instrument plays a piano — fine for a piano's two hands, not for a player of something else.
        var parts = root.DescendantNodes<PartDeclarationSyntax>().GroupBy(p => p.Name.Text).Select(g => g.First()).ToList();
        if (parts.Count >= 2)
        {
            // A drum part is written with `clef percussion` and plays the kit: it needs no instrument.
            var bare = parts.Where(p => !braced.Contains(p.Name.Text) && !RoleOf(p).Drums && !p.Properties.Any(prop =>
                prop.NameToken.Text is "instrument" or "midiInstrument")).Select(p => p.Name.Text).ToList();
            if (bare.Count > 0)
                findings.Add($"No instrument for {string.Join(", ", bare)}: a part without `instrument` sounds as a piano "
                    + "(write `instrument violin`, `viola`, `cello`, … in its part declaration).");
        }

        // Every player reads only their own staff, so a level written in one part says nothing to the
        // others (owner's trial 2026-10-29: a string quartet's dynamics were all in the first violin,
        // the three below it with an @sf at most). A drum part, and a piano hand whose other hand
        // carries them (a piano's dynamics stand between its staves), need none of their own.
        if (parts.Count >= 2 && DynamicLevelsByPart(root) is var levelled && levelled.Count > 0)
        {
            var silent = parts.Where(p => !levelled.Contains(p.Name.Text) && !RoleOf(p).Drums
                    && !(InstrumentOf(p) is "piano-left" or "piano-right" && parts.Any(o => o != p
                        && InstrumentOf(o) is "piano-left" or "piano-right" && levelled.Contains(o.Name.Text))))
                .Select(p => p.Name.Text).ToList();
            if (silent.Count > 0)
                findings.Add($"No dynamics in {string.Join(", ", silent)}: each player reads only their own staff — write the levels "
                    + "(`@p`, `@mf`, `@f`, …) and the hairpins in every part, where its own music asks for them, not in one part alone.");
        }
        findings.AddRange(DynamicDrops(root));
        findings.AddRange(CopiedSections(root));

        MidiFile midi;
        try
        {
            midi = new MidiExporter().Export(tree);
        }
        catch
        {
            return findings;
        }

        // A band — a rhythm section's instrument in it — has the whole rhythm section, at one tempo
        // (owner's rules 2026-10-27: "a song with electric guitar and drums" came back with no bass;
        // guitar and bass ask for drums as well; a band does not change tempo in the middle).
        var roles = parts.Select(p => RoleOf(p)).ToList();
        // Drums alone do not make one: strings with drums are an orchestra's (owner's trial
        // 2026-10-29: a boss-battle piece for strings and drums was asked for a bass and a guitar).
        bool band = roles.Any(r => r.Electric) || (roles.Any(r => r.Bass) && roles.Any(r => r.Chords))
            || (roles.Any(r => r.Drums) && roles.Any(r => r.Bass || r.Chords));
        if (band && parts.Count >= 2)
        {
            if (!roles.Any(r => r.Drums))
                findings.Add("A band piece has drums: add a drum part (`clef percussion`) that plays a groove under the whole song, with fills at the section ends.");
            if (!roles.Any(r => r.Bass))
                findings.Add("A band piece has an electric bass: add a part with `instrument bass` and `midiInstrument \"electric bass (finger)\"` that locks with the kick drum.");
            if (!roles.Any(r => r.Chords))
                findings.Add("A band piece has a guitar or keyboards for the chords: add one (`instrument guitar` with an electric `midiInstrument`, or keyboards).");
            int tempos = midi.Tracks.SelectMany(t => t.TempoChanges).Select(t => t.MicrosecondsPerBeat).Distinct().Count();
            if (tempos > 1)
                findings.Add("A band piece keeps one tempo from start to end: write one `tempo` at the top and none in the sections.");
        }
        int tpq = midi.TicksPerQuarterNote;
        // Each part's onsets (a chord one onset) with its duration.
        var byPart = midi.Tracks.SelectMany(t => t.Notes.Select(n => (Part: n.Part ?? t.Name, n.StartTick, n.DurationTicks, n.Channel)))
            .Where(n => n.Channel != 9 && !n.Part.EndsWith(" (chords)", System.StringComparison.Ordinal))
            .GroupBy(n => n.Part)
            .ToDictionary(g => g.Key, g => g.GroupBy(n => n.StartTick).Select(c => (Start: c.Key, Length: c.Max(n => n.DurationTicks))).OrderBy(o => o.Start).ToList());

        // A piano's hand far from where a pianist's hand plays (owner's report 2026-10-27: "かっこいいピアノの
        // 曲" came back with its left hand between E1 and G2 — the AI wrote `e,,` as if a bare `c`
        // were C4, and in `instrument piano-left` it is C3). The middle of the hand's sounding time
        // is weighed against the hand's usual register; the range check (LYS1052) passes all of it.
        var hands = new Dictionary<string, bool>(System.StringComparer.Ordinal);   // part → left hand
        foreach (var p in parts)
            if (InstrumentOf(p) is "piano-left" or "piano-right")
                hands[p.Name.Text] = InstrumentOf(p) == "piano-left";
        if (declared is { } piano)
            foreach (var grand in piano.Declaration.DescendantNodes<GrandStaffRenderSyntax>())
            {
                var staves = grand.DescendantNodes<StaffRenderSyntax>().Select(s => s.PartToken?.Text).OfType<string>().ToList();
                if (grand.GrandStaffKeyword.Text == "grandStaff" && staves.Count == 2
                    && staves.All(s => parts.Any(p => p.Name.Text == s && InstrumentOf(p).Length == 0)))
                {
                    hands.TryAdd(staves[0], false);
                    hands.TryAdd(staves[1], true);
                }
            }

        // A part of one note value — not a band's bass, whose driving eighths are the idiom, nor a
        // piano's left hand, whose figure (broken chords, a root-and-fifth beat, an ostinato) keeps one
        // value by nature (owner's decision 2026-10-27: told so, the AI broke two good left hands into
        // dotted notes and rests that meant nothing).
        var bandBasses = band
            ? parts.Where(p => RoleOf(p).Bass).Select(p => p.Name.Text).ToHashSet(System.StringComparer.Ordinal)
            : [];
        var melodies = MelodiesOf(source, parts, hands, band);
        var barList = midi.BarTicks.Where(b => b > 0).Distinct().OrderBy(b => b).Prepend(0).ToList();
        // A band's instrumental: the bars where each part over the bass plays a line (said below when
        // there are few), and the part playing them its melody, held to a melody's rhythm (owner's
        // report 2026-10-29: told it had no tune, a rock trio's guitar ran six eighths after a chord
        // in every bar of every section — single notes, no tune to remember).
        var instrumentalLines = band && !System.Text.RegularExpressions.Regex.IsMatch(source, @"\blyrics\s+\w+\s+sings\b")
            ? LineBars(midi, parts, barList) : ((Dictionary<string, HashSet<int>>, int)?)null;
        if (instrumentalLines is { } il && melodies.Count == 0)
            melodies.UnionWith(il.Item1.Where(l => l.Value.Count >= System.Math.Max(4, il.Item2 / 4)).Select(l => l.Key));
        // Every part in the same rhythm, bar after bar (said at the end). Moving as one, the parts
        // share one rhythm, said of the top one alone — the melody, else the first part (owner's
        // trials 2026-10-29: a hymn sung in every voice heard the same two findings four times over,
        // and came back stumbling in every voice, its "keep it" drowned; sung by the soprano alone,
        // its lower voices were told one value each, and were pulled apart).
        var bars = midi.BarTicks.ToList();
        int together = 0, counted = 0;
        if (byPart.Count >= 3 && bars.Count >= 4)
            for (int b = 0; b + 1 < bars.Count; b++)
            {
                var rhythms = byPart.Values
                    .Select(o => string.Join(",", o.Where(x => x.Start >= bars[b] && x.Start < bars[b + 1]).Select(x => x.Start - bars[b])))
                    .Where(r => r.Length > 0).ToList();
                if (rhythms.Count < 3)
                    continue;
                counted++;
                if (rhythms.Distinct().Count() == 1)
                    together++;
            }
        bool asOne = counted >= 4 && together >= counted * 0.6;
        string? top = parts.Select(p => p.Name.Text).FirstOrDefault(n => melodies.Contains(n) && byPart.ContainsKey(n))
            ?? parts.Select(p => p.Name.Text).FirstOrDefault(byPart.ContainsKey);
        foreach (var (part, onsets) in byPart)
        {
            if (onsets.Count < 16 || bandBasses.Contains(part) || hands.GetValueOrDefault(part))
                continue;
            // A note's value as it reads: the distance to the next onset (a staccato quarter sounds
            // shorter than it is written), the last note its own length.
            var values = onsets.Select((o, i) => Value(i + 1 < onsets.Count ? onsets[i + 1].Start - o.Start : o.Length, tpq));
            var common = values.GroupBy(v => v).OrderByDescending(g => g.Count()).First();
            // The melody is held to more (owner's trials 2026-10-29: a pop song's tune two thirds
            // quarters, a piano piece's right hand two thirds eighths in one figure bar after bar,
            // a violin's quarters and halves — each under the 80% an accompaniment is allowed).
            bool melody = melodies.Contains(part);
            if (asOne && part != top)
                continue;
            if (common.Count() >= onsets.Count * (melody ? MelodyOneValue : PartOneValue))
                findings.Add(melody
                    ? $"{part}, the melody: {common.Count() * 100 / onsets.Count}% of its notes are {common.Key}s — a tune lives on its "
                        + "rhythm: long notes at its peaks, shorter ones leading to them, dotted rhythms, a note tied over the beat, a "
                        + "rest to breathe; give its theme a rhythm of its own and vary it from phrase to phrase" + EvenPaceKept
                    : $"{part}: {common.Count() * 100 / onsets.Count}% of its notes are {common.Key}s — mix long and short notes, "
                        + "rests, dotted rhythms; give the line a rhythm of its own.");
            // One bar's rhythm on most of the melody's bars (owner's trial 2026-10-29: a pop song sang
            // `r8 x8 x8 x8~ x4 x4` in every bar, four syllables poured into each and a breath at every
            // bar line, in the middle of words) — a mix of values the count above passes.
            if (melody && SameRhythmBars(onsets, midi.BarTicks) is var (alike, inBars) && inBars >= SameRhythmFewestBars
                && alike >= inBars * SameRhythmShare)
                findings.Add($"{part}, the melody: {alike} of its {inBars} bars have the same rhythm — a pattern poured full, not a line: "
                    + "let the rhythm follow the words and the phrase — a longer note at a phrase's end and a breath only there, "
                    + "a phrase that runs on over the bar line, the rhythm changing from section to section" + EvenPaceKept);
        }

        // A compound meter's tune that does not keep its beats: a note starting within a beat and running
        // across the next — `8 4. 8 8`, `8 8 4 4` in 6/8 — heard as another meter against an
        // accompaniment in threes (owner's trial 2026-10-29). Syncopation is a 4/4 song's own, so
        // only 6/8, 9/8 and 12/8 are read.
        var meters = midi.Tracks.SelectMany(t => t.TimeSignatures).OrderBy(t => t.Tick).ToList();
        foreach (var part in melodies.Where(byPart.ContainsKey))
        {
            var across = new List<int>();
            int compoundBars = 0;
            for (int b = 0; b < barList.Count; b++)
            {
                int start = barList[b], end = b + 1 < barList.Count ? barList[b + 1] : int.MaxValue;
                var meter = meters.LastOrDefault(m => m.Tick <= start);
                if (meter.Denominator != 8 || meter.Numerator is not (6 or 9 or 12))
                    continue;
                var inBar = byPart[part].Where(o => o.Start >= start && o.Start < end).ToList();
                if (inBar.Count == 0)
                    continue;
                compoundBars++;
                int beat = tpq * 3 / 2;
                if (inBar.Any(o => (o.Start - start) % beat != 0 && o.Start + o.Length > start + ((o.Start - start) / beat + 1) * beat))
                    across.Add(b + 1);
            }
            if (across.Count >= AcrossBeatFewestBars && across.Count >= compoundBars * AcrossBeatShare)
                findings.Add($"{part}, the melody: in {across.Count} of its {compoundBars} bars a note starts within a beat and runs "
                    + $"across the next (bars {string.Join(", ", across.Take(6))}{(across.Count > 6 ? ", …" : "")}, as the form plays them) — "
                    + "in a compound meter a beat is three eighths: group the tune by its beats (`4.`, `4 8`, `8 8 8`), and cross "
                    + "a beat only where a syncopation is meant.");
        }

        // A band's instrumental with no tune (owner's report 2026-10-29: a rock piece for guitar, bass
        // and drums had power chords and arpeggios through its verse and chorus, a tune only in a
        // four-bar solo). A bar has a tune where a part other than the drums and the bass plays a line:
        // three onsets or more, most of them single notes. Words sung are a tune of their own.
        if (instrumentalLines is { } instrumental)
        {
            var (lineBars, playedBars) = instrumental;
            int withTune = lineBars.Values.SelectMany(b => b).Distinct().Count();
            if (playedBars >= 8 && withTune < playedBars * TunedBarsShare)
                findings.Add($"A tune is played in only {withTune} of the {playedBars} bars: the parts over the bass play chords and figures, and "
                    + "no words are sung. An instrumental's tune is its lead — let the guitar (or a lead instrument added for it) play "
                    + "the melody of the verse and the chorus, single notes with a hook to remember; a riff and chords alone are the "
                    + "groove under a tune, not the tune.");
        }

        // A band's bass leaping about (owner's trial 2026-10-30: a rock trio's bass at ♩=176 jumped
        // E1–E2–E1–B2–E2–D3 eighth by eighth, two octaves in a bar). A bass drives the root with the
        // kick; an octave is its own, more than one from note to note is not.
        foreach (var bass in bandBasses)
        {
            var lows = midi.Tracks.SelectMany(t => t.Notes.Where(n => (n.Part ?? t.Name) == bass && n.Channel != 9))
                .GroupBy(n => n.StartTick).OrderBy(g => g.Key).Select(g => g.Min(n => n.Pitch)).ToList();
            if (lows.Count < 16)
                continue;
            int wide = lows.Zip(lows.Skip(1), (a, b) => System.Math.Abs(a - b)).Count(d => d > 12);
            if (wide >= (lows.Count - 1) * BassLeapShare)
                findings.Add($"{bass}: {wide * 100 / (lows.Count - 1)}% of its steps leap more than an octave — a band's bass drives "
                    + "the chord's root in eighths with the kick drum (an octave or a fifth now and then, a run at a fill) and "
                    + "doubles the riff; it does not jump octaves from note to note.");
        }

        // A staff labelled by the hand that plays it (owner's report 2026-10-27: a piano piece printed
        // 右手 and 左手 beside its staves) — a score names the instrument, and a solo piano none.
        var handNamed = parts.Where(p => new[] { p.DisplayName, p.ShortName }.Any(n => n != null && HandLabel.IsMatch(n)))
            .Select(p => p.Name.Text).ToList();
        if (handNamed.Count > 0)
            findings.Add($"{string.Join(", ", handNamed)}: a staff is not labelled by the hand that plays it. A piece for piano alone "
                + "names no part (`part rh { instrument piano-right }`); a piano in an ensemble is named once, on its upper part "
                + "(`\"Piano\" \"Pno.\"`).");
        var sounding = midi.Tracks.SelectMany(t => t.Notes.Select(n => (Part: n.Part ?? t.Name, n.Pitch, n.DurationTicks, n.Channel)))
            .Where(n => n.Channel != 9)
            .GroupBy(n => n.Part)
            .ToDictionary(g => g.Key, g => g.ToList());
        // The pitch under which a share of the hand's sounding time lies.
        static int PercentileOf(List<(string Part, int Pitch, int DurationTicks, int Channel)> notes, double p)
        {
            var ordered = notes.OrderBy(n => n.Pitch).ToList();
            double total = ordered.Sum(n => (double)System.Math.Max(1, n.DurationTicks)), sum = 0;
            foreach (var n in ordered)
                if ((sum += System.Math.Max(1, n.DurationTicks)) >= p * total - 1e-9)
                    return n.Pitch;
            return ordered[^1].Pitch;
        }
        foreach (var (part, left) in hands)
        {
            if (!sounding.TryGetValue(part, out var notes) || notes.Count < 8)
                continue;
            int Percentile(double p) => PercentileOf(notes, p);
            int middle = Percentile(0.5);
            string Name(int m) => PartRanges.SpelledFromPitch(m);
            string mostly = $"most of its time is between {Name(Percentile(0.1))} and {Name(Percentile(0.9))}, its middle {Name(middle)}";
            if (left && middle < LeftHandLowestMiddle)
                findings.Add($"{part}: {mostly} — far below a pianist's left hand, which lies mostly between C2 and C4, round C3: "
                    + "move it up an octave or two."
                    + " (Absolute octaves are LilyPond's: C3 is `c`, C2 `c,`, middle C `c'`.)");
            else if (!left && middle > RightHandHighestMiddle)
                findings.Add($"{part}: {mostly} — high above a pianist's right hand, which lies mostly between C4 and C6, round C5: "
                    + "move it down an octave."
                    + " (Absolute octaves are LilyPond's: middle C is `c'`, C5 `c''`, C6 `c'''`.)");
        }
        // The two hands far apart, the middle of the keyboard left empty (owner's report 2026-10-27:
        // a left hand mostly up to D4 under a right hand mostly from D5 — a thin, hollow sound).
        var lefts = hands.Where(h => h.Value && sounding.TryGetValue(h.Key, out var n) && n.Count >= 8).Select(h => h.Key).ToList();
        var rights = hands.Where(h => !h.Value && sounding.TryGetValue(h.Key, out var n) && n.Count >= 8).Select(h => h.Key).ToList();
        if (lefts.Count == 1 && rights.Count == 1)
        {
            int lhTop = PercentileOf(sounding[lefts[0]], 0.9), rhFloor = PercentileOf(sounding[rights[0]], 0.1);
            if (rhFloor - lhTop > WidestGapBetweenHands)
                findings.Add($"{rights[0]}, {lefts[0]}: the hands stand far apart — the left hand mostly up to {PartRanges.SpelledFromPitch(lhTop)}, "
                    + $"the right hand mostly from {PartRanges.SpelledFromPitch(rhFloor)} — and the middle of the keyboard is empty: let the left "
                    + "hand's figure reach up toward middle C, and give the right hand's melody its harmony under it (thirds, sixths, "
                    + "chords on the strong beats).");
        }
        // The left hand's figure into the melody, and spread wider than a hand's figure (owner's review
        // 2026-10-29: a wedding piece's broken chords ran D2 to A4 every bar — on the melody's own note,
        // and over it where the tune dipped; the guide said so, and was not heeded).
        if (lefts.Count == 1 && rights.Count == 1)
        {
            var notesOf = (string part) => midi.Tracks.SelectMany(t => t.Notes.Where(n => (n.Part ?? t.Name) == part && n.Channel != 9)).ToList();
            var (lh, rh) = (notesOf(lefts[0]), notesOf(rights[0]));
            var barTicks = midi.BarTicks.Where(b => b > 0).Distinct().OrderBy(b => b).ToList();
            int BarOf(int tick) => 1 + barTicks.Count(b => b <= tick);   // as the form plays
            // A left-hand note at or above the lowest right-hand note sounding with it.
            var into = lh.Where(n =>
                {
                    var with = rh.Where(r => r.StartTick < n.StartTick + n.DurationTicks && n.StartTick < r.StartTick + r.DurationTicks).ToList();
                    return with.Count > 0 && n.Pitch >= with.Min(r => r.Pitch);
                })
                .Select(n => BarOf(n.StartTick)).Distinct().OrderBy(b => b).ToList();
            int played = lh.Concat(rh).Select(n => BarOf(n.StartTick)).Distinct().Count();
            if (into.Count >= HandsMeetFewestBars && into.Count >= played * HandsMeetShare)
                findings.Add($"{lefts[0]}, {rights[0]}: in {into.Count} of {played} bars the left hand reaches the melody — the right hand's own note "
                    + $"or above it (bars {string.Join(", ", into.Take(6))}{(into.Count > 6 ? ", …" : "")}, as the form plays them): the melody is "
                    + "lost in its accompaniment. Keep the left hand's figure below the tune, round C3 up toward middle C.");
            // A bar of the left hand's broken chords (single notes, four or more) wider than a twelfth.
            var wide = lh.GroupBy(n => BarOf(n.StartTick))
                .Where(g => g.GroupBy(n => n.StartTick).Count() >= 4 && g.GroupBy(n => n.StartTick).All(c => c.Count() == 1))
                .Select(g => (Bar: g.Key, Low: g.Min(n => n.Pitch), High: g.Max(n => n.Pitch))).ToList();
            var over = wide.Where(w => w.High - w.Low > Twelfth).ToList();
            if (over.Count >= HandsMeetFewestBars && over.Count >= wide.Count * HandsMeetShare)
                findings.Add($"{lefts[0]}: in {over.Count} of its {wide.Count} bars of broken chords the figure spans more than a twelfth "
                    + $"({PartRanges.SpelledFromPitch(over[0].Low)} to {PartRanges.SpelledFromPitch(over[0].High)} in bar {over[0].Bar}): a hand's "
                    + "figure stays within an octave and a half, climbing from its bass by the chord's tones and turning back.");
        }

        // The length and the ending (owner's report 2026-10-27: the composed piano piece was 8 bars, two
        // sections of four, and its end "unnatural" — it began in E minor and stopped in G major).
        var barStarts = midi.BarTicks.ToList();
        // A chords row's voicings are the player's, not the piece's bass: left out, as above.
        var pitched = midi.Tracks.SelectMany(t => t.Notes.Where(n => n.Channel != 9
            && !(n.Part ?? t.Name).EndsWith(" (chords)", System.StringComparison.Ordinal))).ToList();
        int lastTick = pitched.Count == 0 ? 0 : pitched.Max(n => n.StartTick + n.DurationTicks);
        // The first bar, and every bar that starts before the music ends.
        int barCount = 1 + barStarts.Count(b => b > 0 && b < lastTick);
        // …and as long as it plays (owner's trials 2026-10-29: two 6/8 wedding pieces of 26 and 28 bars
        // lasted under a minute — the guide's minutes, said once, went unheeded). A lead sheet or a
        // melody alone, one staff, is a head played round or a tune to sing: its length is its form's.
        int seconds = (int)System.Math.Round(SecondsAt(midi, lastTick));
        if (pitched.Count > 0 && (barCount < ShortestPiece || (seconds < ShortestSeconds && byPart.Count >= 2)))
            // More music, not a slower tempo (owner's report 2026-10-29: told 70 seconds, the chat took
            // its Andante from ♩ = 72 to 48 and added no bar).
            findings.Add($"The piece is {barCount} bars long, about {seconds} seconds played: unless a short piece was asked for, give it "
                + "the length its form needs — a minute and a half to three minutes (about 24 to 32 bars of 4/4; a shorter bar "
                + "or a quicker beat takes more), its phrases long enough to say something, each closing on a cadence. Lengthen it "
                + "with music — a section more, a varied return, an introduction — never by slowing its tempo (keep the tempo its "
                + "character asks for), nor by playing one section over and over in a row.");
        if (PlayedKeys(root) is { } inKeys && pitched.Count > 0)
        {
            var (home, end) = inKeys;
            int homeTonic = PitchClass(home.Tonic), endTonic = PitchClass(end.Tonic);
            string KeyName((string Tonic, string Mode) k) => $"{PitchClassName(PitchClass(k.Tonic))} {k.Mode}";
            if (homeTonic != endTonic || home.Mode != end.Mode)
                findings.Add($"The piece begins in {KeyName(home)} and ends in {KeyName(end)}: come back to the home key for the last "
                    + "section, and close there.");
            // The lowest note sounding at the last onset: the final chord's bass.
            int lastOnset = pitched.Max(n => n.StartTick);
            int bass = pitched.Where(n => n.StartTick <= lastOnset && n.StartTick + n.DurationTicks > lastOnset).Min(n => n.Pitch);
            if ((bass % 12 + 12) % 12 != endTonic)
                findings.Add($"The piece ends over {PitchClassName(bass)}, not its tonic {PitchClassName(endTonic)}: close with a cadence "
                    + $"(V–I) on the tonic chord, held through the last bar, the bass on {PitchClassName(endTonic)}.");
        }

        // Every part in the same rhythm, bar after bar (counted above).
        {
            if (asOne)
                findings.Add($"In {together} of {counted} bars every part moves in the same rhythm: let one voice carry the melody "
                    + "while the others accompany (broken chords, repeated notes, a moving bass), and pass the roles between them"
                    // A hymn's four voices move as one (owner's trial 2026-10-29: told this, the chat
                    // turned a hymn into a tune over held notes — the alto on whole notes for 16 bars).
                    + " — unless the voices moving together is the music's own character, as in a hymn or a chorale: then keep it.");
        }
        return findings;
    }

    // The middle of a piano hand's sounding time (MIDI) past which the hand is out of its usual
    // register: a left hand whose middle is under F2, a right hand whose middle is over G5 (owner's
    // reports 2026-10-27: a right hand between D5 and G6, written by LilyPond's octaves, was "too
    // high"; one planned for D5–E6 still sat high).
    private const int LeftHandLowestMiddle = 41, RightHandHighestMiddle = 79;

    // Semitones between the top of the left hand's tessitura and the bottom of the right hand's
    // past which the middle of the keyboard is empty: an octave.
    private const int WidestGapBetweenHands = 12;

    // The left hand meeting the melody, or a figure spread past a twelfth, in this many bars and this
    // share of them: a touch in passing is a pianist's, a habit is the figure's.
    private const int HandsMeetFewestBars = 3;
    private const double HandsMeetShare = 0.15;
    private const int Twelfth = 19;

    // A label that names a hand, not an instrument.
    private static readonly System.Text.RegularExpressions.Regex HandLabel = new(
        @"^\s*((right|left)\s*hand|[RL]\.\s*H\.?|[RL]H|右手|左手|みぎて|ひだりて)\s*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // Under this many bars a piece is a sketch, not a piece (16: two 8-bar phrases). Kept a floor, not
    // the length asked for (owner's wish 2026-10-29: rules held too tight make every piece the same):
    // the guide suggests about 24 to 32 bars, and the AI may choose otherwise.
    private const int ShortestPiece = 16;

    // …nor under this many seconds played, a floor under the guide's minute and a half (owner's trials
    // 2026-10-29: 6/8 pieces of 26 and 28 bars played in under a minute).
    private const int ShortestSeconds = 75;

    // A compound meter's melody notes that start within a beat and run across the next, in this many
    // bars and this share of them (owner's trial 2026-10-29: `b'8 d''4. b'8 a'8` in 8 of a 6/8 tune's bars).
    private const int AcrossBeatFewestBars = 3;

    // A band's line in a bar: most of its onsets single notes; and the share of the bars a tune is
    // heard in, under which an instrumental has none to speak of (owner's report 2026-10-29).
    private const double TuneSingleShare = 0.7, TunedBarsShare = 0.5, BassLeapShare = 0.2;
    private const double AcrossBeatShare = 0.2;

    /// <summary>The seconds from the start to <paramref name="tick"/>, by the tempo changes of every
    /// track (MIDI's 120 to the quarter before the first).</summary>
    private static double SecondsAt(MidiFile midi, int tick)
    {
        var changes = midi.Tracks.SelectMany(t => t.TempoChanges).OrderBy(c => c.Tick).ToList();
        double seconds = 0;
        int at = 0, perBeat = 500_000;
        foreach (var c in changes.Where(c => c.Tick < tick))
        {
            seconds += (c.Tick - at) * (double)perBeat / midi.TicksPerQuarterNote / 1e6;
            (at, perBeat) = (c.Tick, c.MicrosecondsPerBeat);
        }
        return seconds + (tick - at) * (double)perBeat / midi.TicksPerQuarterNote / 1e6;
    }

    // The dynamic levels — not an accent like `@sf`, which says nothing of how loud the bars after it are.
    private static readonly HashSet<string> Levels = ["pppp", "ppp", "pp", "p", "mp", "mf", "f", "ff", "fff", "ffff", "fp"];

    /// <summary>
    /// Sections written twice, note for note — their bodies alike but for spacing (owner's report
    /// 2026-10-29: a rock trio's Chorus2 was its Chorus again, every part). The guide says a section
    /// that returns is named again in the form, not copied.
    /// </summary>
    private static IEnumerable<string> CopiedSections(SyntaxNode root)
    {
        static string Body(SectionDeclarationSyntax s)
        {
            string text = s.ToFullString();
            int open = text.IndexOf('{');
            return open < 0 ? "" : System.Text.RegularExpressions.Regex.Replace(text[open..], @"\s+", " ").Trim();
        }
        return root.DescendantNodes<SectionDeclarationSyntax>().Where(s => s.Parent is { Parent: null })
            .GroupBy(Body).Where(g => g.Key.Length > 0 && g.Count() > 1)
            .Select(g => g.Select(s => s.SectionName).ToList())
            .Select(names => $"{string.Join(" and ", names.Skip(1))} {(names.Count > 2 ? "are" : "is")} {names[0]} again, note for note: "
                + $"write {names[0]} once and name it again in the form where it returns, not a second section with the same notes.");
    }

    /// <summary>
    /// A section that ends loud and the one played after it opening soft, two levels down or more, with
    /// no <c>@decresc</c> (<c>@dim</c>) after the last level to lead there and no <c>@p.subito</c> to
    /// say it is meant (owner's trials 2026-10-29: three wedding pieces in a row went from B's @f
    /// straight to the returning A's @p). Read part by part, each part's marks in the order written.
    /// </summary>
    private static IEnumerable<string> DynamicDrops(SyntaxNode root)
    {
        string[] order = ["pppp", "ppp", "pp", "p", "mp", "mf", "f", "ff", "fff", "ffff"];
        var sections = root.DescendantNodes<SectionDeclarationSyntax>().Where(s => s.Parent is { Parent: null })
            .GroupBy(s => s.SectionName).ToDictionary(g => g.Key, g => g.First());
        var form = root.DescendantNodes<FormDeclarationSyntax>().Where(f => !f.InScore).OrderBy(f => f.Name != null).FirstOrDefault();
        var played = form?.DescendantNodes<SectionReferenceSyntax>().Select(r => r.SectionName).Where(sections.ContainsKey).ToList() ?? [];
        List<DynamicSyntax> MarksOf(string section, string part) => sections[section].DescendantNodes<PartBlockSyntax>()
            .Where(b => b.PartName.Text == part).SelectMany(b => b.DescendantNodes<DynamicSyntax>()).ToList();
        var said = new HashSet<(string, string)>();
        for (int i = 0; i + 1 < played.Count; i++)
        {
            var (from, to) = (played[i], played[i + 1]);
            if (from == to || said.Contains((from, to)))
                continue;
            foreach (var part in sections[from].DescendantNodes<PartBlockSyntax>().Select(b => b.PartName.Text).Distinct())
            {
                var before = MarksOf(from, part);
                var after = MarksOf(to, part);
                if (before.Count == 0 || after.Count == 0)
                    continue;
                // The last mark is a level: no hairpin after it leads anywhere.
                int loud = System.Array.IndexOf(order, before[^1].DynamicToken.Text);
                var first = after.FirstOrDefault(d => System.Array.IndexOf(order, d.DynamicToken.Text) >= 0);
                if (loud < 0 || first is null || first.IsSubito || first != after[0])
                    continue;
                int soft = System.Array.IndexOf(order, first.DynamicToken.Text);
                if (loud - soft < 2)
                    continue;
                said.Add((from, to));
                yield return $"{part}: {from} ends at @{order[loud]} and {to}, played after it, opens at @{order[soft]} — a drop of "
                    + $"{loud - soft} levels with nothing leading to it: bring the level down over {from}'s last bars (a @decresc to "
                    + $"@{order[soft]}), or, if the sudden quiet is meant, write @{order[soft]}.subito.";
                break;
            }
        }
    }

    /// <summary>The parts whose music writes a dynamic level, in a section's cell or in a phrase the
    /// cell names.</summary>
    private static HashSet<string> DynamicLevelsByPart(SyntaxNode root)
    {
        static bool HasLevel(SyntaxNode n) => n.DescendantNodes<DynamicSyntax>().Any(d => Levels.Contains(d.DynamicToken.Text));
        var phrases = root.DescendantNodes<PhraseDeclarationSyntax>().Where(HasLevel).Select(p => p.Name.Text)
            .ToHashSet(System.StringComparer.Ordinal);
        var parts = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var cell in root.DescendantNodes<PartBlockSyntax>())
            if (HasLevel(cell) || (phrases.Count > 0 && cell.DescendantNodes<SyntaxTokenNode>().Any(t => phrases.Contains(t.Text))))
                parts.Add(cell.PartName.Text);
        return parts;
    }

    // The share of one note value past which a part reads as one value: an accompaniment's figure
    // may keep one, a melody may not (owner's trials 2026-10-29).
    private const double PartOneValue = 0.8, MelodyOneValue = 0.6;

    // Where an even pace is the music's own (owner's trial 2026-10-29: a hymn's soprano in quarters
    // was sent back, and came again stumbling over eighths and dotted quarters in every voice): the
    // melody's findings say so, and leave the choice to the composer.
    private const string EvenPaceKept = " — unless an even pace is the music's own character, as in a hymn, a chorale "
        + "or a march: then keep it.";

    // A melody whose bars mostly share one rhythm: half of them or more, over 8 bars at least (a
    // theme's rhythm comes back often, but not in every other bar).
    private const double SameRhythmShare = 0.5;
    private const int SameRhythmFewestBars = 8;

    /// <summary>How many of the bars a part sounds in share its commonest rhythm (the onsets' places
    /// in the bar), and how many bars it sounds in.</summary>
    private static (int Same, int Bars) SameRhythmBars(IReadOnlyList<(int Start, int Length)> onsets, IEnumerable<int> barTicks)
    {
        var starts = barTicks.Distinct().OrderBy(t => t).ToList();
        if (starts.Count == 0 || starts[0] > 0)
            starts.Insert(0, 0);
        var byBar = new Dictionary<int, List<int>>();
        foreach (var (start, _) in onsets)
        {
            int i = starts.BinarySearch(start);
            if (i < 0)
                i = ~i - 1;
            (byBar.TryGetValue(i, out var list) ? list : byBar[i] = []).Add(start - starts[i]);
        }
        if (byBar.Count == 0)
            return (0, 0);
        int same = byBar.Values.GroupBy(l => string.Join(",", l)).Max(g => g.Count());
        return (same, byBar.Count);
    }

    /// <summary>
    /// For each part over the bass (not the drums, not a bass), the bars (indices into
    /// <paramref name="barList"/>) where it plays a line — three onsets or more, most of them single
    /// notes — and how many bars anything is played in.
    /// </summary>
    private static (Dictionary<string, HashSet<int>> Lines, int Played) LineBars(MidiFile midi,
        IReadOnlyList<PartDeclarationSyntax> parts, List<int> barList)
    {
        var lineParts = parts.Where(p => RoleOf(p) is { Drums: false, Bass: false }).Select(p => p.Name.Text)
            .ToHashSet(System.StringComparer.Ordinal);
        var onsets = midi.Tracks.SelectMany(t => t.Notes.Where(n => n.Channel != 9).Select(n => (Part: n.Part ?? t.Name, n.StartTick)))
            .Where(n => lineParts.Contains(n.Part))
            .GroupBy(n => (n.Part, n.StartTick)).Select(g => (g.Key.Part, g.Key.StartTick, Single: g.Count() == 1)).ToList();
        var lines = lineParts.ToDictionary(p => p, _ => new HashSet<int>(), System.StringComparer.Ordinal);
        int played = 0;
        for (int b = 0; b < barList.Count; b++)
        {
            int start = barList[b], end = b + 1 < barList.Count ? barList[b + 1] : int.MaxValue;
            var inBar = onsets.Where(o => o.StartTick >= start && o.StartTick < end).ToList();
            if (inBar.Count == 0)
                continue;
            played++;
            foreach (var g in inBar.GroupBy(o => o.Part))
                if (g.Count() >= 3 && g.Count(o => o.Single) >= g.Count() * TuneSingleShare)
                    lines[g.Key].Add(b);
        }
        return (lines, played);
    }

    /// <summary>
    /// The parts that carry the melody: those a lyrics track sings; else, beside a piano, the
    /// parts that are neither the piano nor a band's drums, bass or chords (a violin's, a flute's);
    /// else a piano's right hand.
    /// </summary>
    private static HashSet<string> MelodiesOf(string source, IReadOnlyList<PartDeclarationSyntax> parts,
        IReadOnlyDictionary<string, bool> hands, bool band)
    {
        var sung = System.Text.RegularExpressions.Regex.Matches(source, @"\blyrics\s+\w+\s+sings\s+(\w+)")
            .Select(m => m.Groups[1].Value).ToHashSet(System.StringComparer.Ordinal);
        if (sung.Count > 0)
            return sung;
        // Beside a piano only (owner's trial 2026-10-29: in strings with drums, every string — the
        // viola's figure, the cello's bass — was held to a melody's rhythm).
        var solos = parts.Where(p => !hands.ContainsKey(p.Name.Text)
                && !(RoleOf(p) is var r && (r.Drums || r.Bass || (band && r.Chords))))
            .Select(p => p.Name.Text).ToHashSet(System.StringComparer.Ordinal);
        if (hands.Count > 0 && solos.Count > 0 && solos.Count < parts.Count)
            return solos;
        return hands.Where(h => !h.Value).Select(h => h.Key).ToHashSet(System.StringComparer.Ordinal);
    }

    private static readonly string[] PitchClassNames = ["C", "C♯", "D", "E♭", "E", "F", "F♯", "G", "A♭", "A", "B♭", "B"];

    private static string PitchClassName(int pitch) => PitchClassNames[(pitch % 12 + 12) % 12];

    /// <summary>
    /// The key the piece begins in and the one it ends in, as the form plays them: the first and the
    /// last section played, each in its own <c>key</c>, else in the file's — a section that writes
    /// none is in the file's key whatever was played before it (owner's trial 2026-10-29: B in D
    /// major, the A after it and the coda back in G with no line of their own, was told it "ends in
    /// D major", and the chat wrote a `key g major` the coda did not need). Null with no key written.
    /// </summary>
    private static ((string Tonic, string Mode) Home, (string Tonic, string Mode) End)? PlayedKeys(SyntaxNode root)
    {
        static (string, string)? Of(KeySignatureSyntax? k)
            => k is null || k.IsCustom ? null
                : (k.Pitch.ToFullString().Trim().ToLowerInvariant(), k.Mode.Text.ToLowerInvariant());
        var keys = root.DescendantNodes<KeySignatureSyntax>().ToList();
        static bool InSection(SyntaxNode n)
        {
            for (var p = n.Parent; p != null; p = p.Parent)
                if (p is SectionDeclarationSyntax)
                    return true;
            return false;
        }
        var fileKey = Of(keys.FirstOrDefault(k => !InSection(k)));
        var sections = root.DescendantNodes<SectionDeclarationSyntax>().Where(s => s.Parent is { Parent: null })
            .GroupBy(s => s.SectionName).ToDictionary(g => g.Key, g => g.First());
        (string, string)? KeyOf(string section)
            => sections.TryGetValue(section, out var s) ? Of(s.DescendantNodes<KeySignatureSyntax>().LastOrDefault()) ?? fileKey : fileKey;
        var form = root.DescendantNodes<FormDeclarationSyntax>().Where(f => !f.InScore).OrderBy(f => f.Name != null).FirstOrDefault();
        var played = form?.DescendantNodes<SectionReferenceSyntax>().ToList() ?? [];
        if (played.Count > 0)
            return KeyOf(played[0].SectionName) is { } first && KeyOf(played[^1].SectionName) is { } last ? (first, last) : null;
        // No form: the keys as written.
        var written = keys.Select(Of).OfType<(string, string)>().ToList();
        return written.Count > 0 ? (written[0], written[^1]) : null;
    }

    /// <summary>The pitch class of a note name (<c>fis</c>, <c>bes</c>, <c>es</c>), 0 = C.</summary>
    private static int PitchClass(string name)
    {
        int step = name[0] switch { 'c' => 0, 'd' => 2, 'e' => 4, 'f' => 5, 'g' => 7, 'a' => 9, _ => 11 };
        string suffix = name[1..];
        int alter = suffix switch { "is" => 1, "isis" => 2, "es" or "s" => -1, "eses" => -2, _ => 0 };
        return ((step + alter) % 12 + 12) % 12;
    }

    /// <summary>The part's <c>instrument</c>, lower case; empty when it names none. A hyphenated
    /// preset is three tokens (word, minus, word), joined back into one word.</summary>
    private static string InstrumentOf(PartDeclarationSyntax part)
        => System.Text.RegularExpressions.Regex.Replace(PropOf(part, "instrument"), @"\s*-\s*", "-");

    private static string PropOf(PartDeclarationSyntax part, string name) => string.Join(" ", part.Properties
        .Where(p => p.NameToken.Text.Equals(name, System.StringComparison.OrdinalIgnoreCase))
        .SelectMany(p => Enumerable.Range(2, System.Math.Max(0, p.SlotCount - 2)).Select(p.GetChild))
        .OfType<SyntaxTokenNode>().Select(t => t.Text.Trim('"'))).ToLowerInvariant();

    /// <summary>What a part is in a band: drums, a bass, a chord instrument (guitar, keyboards), and
    /// whether its sound is electric (an electric guitar or bass, an organ, a synth).</summary>
    private readonly record struct Role(bool Drums, bool Bass, bool Chords, bool Electric);

    private static Role RoleOf(PartDeclarationSyntax part)
    {
        string Prop(string name) => PropOf(part, name);
        string instrument = Prop("instrument"), sound = Prop("midiInstrument"), clef = Prop("clef");
        bool drums = clef.Contains("percussion") || sound.Contains("drum");
        bool bass = instrument is "bass" or "bass5" or "bass6" || (sound.Contains("bass") && !sound.Contains("contrabass"));
        bool chords = instrument.StartsWith("guitar") || instrument.StartsWith("piano")
            || System.Text.RegularExpressions.Regex.IsMatch(sound, "guitar|piano|organ|clavi|synth|pad");
        bool electric = System.Text.RegularExpressions.Regex.IsMatch(sound, "electric|overdriven|distortion|organ|synth");
        return new Role(drums, bass && !drums, chords && !drums, electric);
    }

    /// <summary>The file's top-level <c>title "…"</c>, or null when it writes none (or an empty one).</summary>
    public static string? TitleOf(string source)
    {
        var m = System.Text.RegularExpressions.Regex.Match(source, @"^[ \t]*title[ \t]+""((?:[^""\\]|\\.)*)""",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        return m.Success && m.Groups[1].Value.Trim().Length > 0 ? m.Groups[1].Value.Trim() : null;
    }

    /// <summary>A title that names no piece: a template's or a tool's placeholder.</summary>
    public static bool IsPlaceholderTitle(string title)
        => System.Text.RegularExpressions.Regex.IsMatch(title.Trim(),
            @"^(new piece|new score|untitled|title|piece|my piece|song|composition|無題|新しい曲|タイトル)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // A duration's name, for the sentence ("whole", "quarter", …), else its quarters.
    private static string Value(int ticks, int tpq)
    {
        double q = ticks / (double)tpq;
        return q switch
        {
            >= 3.9 and <= 4.1 => "whole note",
            >= 2.9 and <= 3.1 => "dotted half note",
            >= 1.9 and <= 2.1 => "half note",
            >= 1.4 and <= 1.6 => "dotted quarter note",
            >= 0.9 and <= 1.1 => "quarter note",
            >= 0.45 and <= 0.55 => "eighth note",
            >= 0.2 and <= 0.3 => "sixteenth note",
            _ => $"{q:0.##}-quarter note",
        };
    }
}
