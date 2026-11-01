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
        // Pitches named as the key spells them (owner's request 2026-10-30: B♭ minor told in C♯).
        int fifths = PartRanges.KeyFifths(root);
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
        findings.AddRange(PhraseDrops(root));
        findings.AddRange(LateLevels(root, parts.Where(p => RoleOf(p).Drums || InstrumentOf(p) is "" || InstrumentOf(p).StartsWith("piano", System.StringComparison.Ordinal))
            .Select(p => p.Name.Text).ToHashSet(System.StringComparer.Ordinal)));
        findings.AddRange(CopiedSections(root));
        findings.AddRange(ReplayedSections(root));
        findings.AddRange(CrashesOffThePhrases(root, parts.Where(p => RoleOf(p).Drums).Select(p => p.Name.Text)
            .ToHashSet(System.StringComparer.Ordinal)));
        findings.AddRange(SpelledAgainstTheKey(root, parts, System.Text.RegularExpressions.Regex.IsMatch(source, @"(?m)^\s*pitch\s+concert\b")));

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
            // A drummer's levels too, when the band's move (owner's trial 2026-10-30: guitar and bass went
            // from @p to @ff, the drums had no level, the same loudness through a quiet bridge).
            var kits = parts.Where(p => RoleOf(p).Drums).Select(p => p.Name.Text).ToList();
            var levelledParts = DynamicLevelsByPart(root);
            string[] order = ["pppp", "ppp", "pp", "p", "mp", "mf", "f", "ff", "fff", "ffff"];
            var levels = root.DescendantNodes<PartBlockSyntax>().Where(b => !kits.Contains(b.PartName.Text))
                .SelectMany(b => b.DescendantNodes<DynamicSyntax>()).Select(d => System.Array.IndexOf(order, d.DynamicToken.Text))
                .Where(i => i >= 0).ToList();
            var unlevelled = kits.Where(k => !levelledParts.Contains(k)).ToList();
            if (unlevelled.Count > 0 && levels.Count > 0 && levels.Max() - levels.Min() >= 2)
                findings.Add($"{string.Join(", ", unlevelled)}: no level, while the others go from @{order[levels.Min()]} to "
                    + $"@{order[levels.Max()]} — write the drummer's levels too (`@p` on the first note of a quiet section, "
                    + "`@f` where the band comes in full), with the hairpins the others play.");
            // The rhythm section at one level while the band moves (owner's trial 2026-10-30: the bass
            // and drums held their opening @mf through every chorus the guitar played @f). A part with
            // no level at all is said above; the levels in its phrases count.
            var phraseBodies = root.DescendantNodes<PhraseDeclarationSyntax>().Where(p => p.Parent is CompilationUnitSyntax)
                .GroupBy(p => p.Name.Text).ToDictionary(g => g.Key, g => g.First());
            List<string> LevelsOf(string part) => root.DescendantNodes<PartBlockSyntax>().Where(b => b.PartName.Text == part)
                .SelectMany(b => DynamicsPlayed(b, phraseBodies)).Select(d => d.DynamicToken.Text)
                .Where(order.Contains).Distinct().ToList();
            var rhythmSection = parts.Where(p => RoleOf(p) is { Drums: true } or { Bass: true }).Select(p => p.Name.Text).ToList();
            var moving = parts.Where(p => !rhythmSection.Contains(p.Name.Text)).Select(p => (Name: p.Name.Text, Levels: LevelsOf(p.Name.Text)))
                .Where(p => p.Levels.Count >= 2).ToList();
            var held = rhythmSection.Select(p => (Name: p, Levels: LevelsOf(p))).Where(p => p.Levels.Count == 1).ToList();
            if (held.Count > 0 && moving.Count > 0)
            {
                var span = moving.SelectMany(p => p.Levels).Select(l => System.Array.IndexOf(order, l)).ToList();
                findings.Add($"{string.Join(", ", held.Select(p => p.Name))}: one level, @{held[0].Levels[0]}, from start to end, while "
                    + $"{string.Join(", ", moving.Select(p => p.Name))} {(moving.Count == 1 ? "goes" : "go")} from @{order[span.Min()]} "
                    + $"to @{order[span.Max()]} — a band rises "
                    + "and falls together: write the rhythm section's levels where the lead's change (the drums @f where the chorus "
                    + "comes in, the bass with them, both down for a quiet bridge), with the hairpins between.");
            }
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
                        + "rest to breathe; give its theme a rhythm of its own and vary it from phrase to phrase" + (band ? BandTuneSings : EvenPaceKept)
                    : $"{part}: {common.Count() * 100 / onsets.Count}% of its notes are {common.Key}s — mix long and short notes, "
                        + "rests, dotted rhythms; give the line a rhythm of its own.");
            // One bar's rhythm on most of the melody's bars (owner's trial 2026-10-29: a pop song sang
            // `r8 x8 x8 x8~ x4 x4` in every bar, four syllables poured into each and a breath at every
            // bar line, in the middle of words) — a mix of values the count above passes.
            if (melody && SameRhythmBars(onsets, midi.BarTicks) is var (alike, inBars) && inBars >= SameRhythmFewestBars
                && alike >= inBars * SameRhythmShare)
                findings.Add($"{part}, the melody: {alike} of its {inBars} bars have the same rhythm — a pattern poured full, not a line: "
                    + "let the rhythm follow the words and the phrase — a longer note at a phrase's end and a breath only there, "
                    + "a phrase that runs on over the bar line, the rhythm changing from section to section" + (band ? BandTuneSings : EvenPaceKept));
            // …or on most of a SECTION's bars, where the whole piece's count passes because other
            // sections vary (owner's review 2026-11-01: a rock instrumental's verse played one rhythm
            // in all 8 of its bars, its chorus two rhythms in turn, its solo free — 40% over the
            // piece, nothing said). Read section by section, the sections named together.
            else if (melody && SameRhythmSections(onsets, midi) is { Count: > 0 } poured)
                findings.Add($"{part}, the melody: {string.Join(", ", poured.Select(s => $"in {s.Name} {s.Same} of its {s.Bars} bars"))} "
                    + "have the same rhythm — one pattern poured into the section, not a line: let the rhythm vary within it — "
                    + "a longer note at a phrase's end, a rest, a note tied over the beat — and change from section to section"
                    + (band ? BandTuneSings : EvenPaceKept));
        }

        // A melody of leaps alone (owner's trial 2026-10-30: told it had no tune, a rock guitar broke
        // each bar's chord up and down after it, `<a, e>4 a8 c'8 e'4 c'8 a8` — counted a line, heard
        // an accompaniment). Its single notes are read, a repeated note no move; parts moving as one
        // are told of it in the top one alone, as of their rhythm.
        foreach (var part in melodies)
        {
            if (asOne && part != top)
                continue;
            var line = midi.Tracks.SelectMany(t => t.Notes.Where(n => (n.Part ?? t.Name) == part && n.Channel != 9))
                .GroupBy(n => n.StartTick).OrderBy(g => g.Key).Where(g => g.Count() == 1).Select(g => g.First().Pitch).ToList();
            var moves = line.Zip(line.Skip(1), (a, b) => System.Math.Abs(a - b)).Where(d => d > 0).ToList();
            int steps = moves.Count(d => d <= 2);
            if (moves.Count >= LeapsFewestMoves && steps < moves.Count * MelodyStepShare)
                findings.Add($"{part}, the melody: only {steps * 100 / moves.Count}% of its moves are steps — it goes up and down "
                    + "its chords, a broken chord, the accompaniment's figure: a tune moves mostly by step (a second up or down), "
                    + "a leap now and then to reach a peak, turning back by step after it — unless its theme is a fanfare, a call "
                    + "on its chord's notes: then keep it.");
        }

        // One bar's figure moved along the chords (owner's trial 2026-10-30: a rock guitar's verse was
        // "up two steps and back" in bar after bar, `g a bes a g` over Gm, `ees f g f ees` over E♭ —
        // a varied rhythm, so no rhythm check saw it).
        foreach (var part in melodies)
        {
            if (asOne && part != top)
                continue;
            var (alike, inBars, shape) = BarContours(midi, part, barList);
            if (inBars >= ContourFewestBars && alike >= inBars * ContourShare)
                findings.Add($"{part}, the melody: {alike} of its {inBars} bars move in the shape of the bar before ({shape}) — "
                    + "one bar's figure moved along the chords, not a line: let a phrase run two or four bars as one line, rising "
                    + "to a peak and falling to its close, its hook a shape of its own, answered by a different one"
                    + (band ? "." : " — unless a sequence is the music's own design, as in a baroque episode: then keep it."));
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
        // One part over the bass and drums plays both the chords and the tune (owner's review
        // 2026-10-30: a rock trio's guitar, told it had no tune, played one in every bar; told then that
        // nobody played the chords, it struck chords in every bar again — each finding pushing it to
        // the other end). Both findings say how the two are shared.
        var overBass = parts.Where(p => RoleOf(p) is { Drums: false, Bass: false }).Select(p => p.Name.Text).ToList();
        string shared = overBass.Count == 1
            ? $" {overBass[0]} is the band's only part over the bass, its chords and its tune both: share them out — the chords or a "
              + "riff under the verse, the tune in the chorus (its hook, a chord struck where the tune rests) — not all one or all "
              + "the other."
            : "";
        if (instrumentalLines is { } instrumental)
        {
            var (lineBars, playedBars) = instrumental;
            int withTune = lineBars.Values.SelectMany(b => b).Distinct().Count();
            if (playedBars >= 8 && withTune < playedBars * TunedBarsShare)
                findings.Add($"A tune is played in only {withTune} of the {playedBars} bars: the parts over the bass play chords and figures, and "
                    + "no words are sung. An instrumental's tune is its lead — let the guitar (or a lead instrument added for it) play "
                    + (shared.Length > 0 ? "a melody" : "the melody of the verse and the chorus")
                    + ", single notes with a hook to remember, moving mostly by step; a riff, "
                    + "chords and their notes broken up and down are the groove under a tune, not the tune." + shared);
            // The same sections played three times over (owner's trial 2026-10-30: told it was short, a
            // rock instrumental played Verse Chorus three times, the same each time). A song's verses
            // change their words; an instrumental's return the same music.
            var thrice = PlayedSections(root).GroupBy(s => s).Where(g => g.Count() >= 3).Select(g => (Name: g.Key, Times: g.Count())).ToList();
            if (thrice.Count > 0)
                findings.Add($"{string.Join(", ", thrice.Select(t => $"{t.Name} ({t.Times} times)"))}: played the same each time, and no "
                    + "words are sung to change them — vary a return as a section of its own (a lead tune over the verse, the chorus "
                    + "an octave up or a key higher, a breakdown), or play it twice.");
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

        // One note out of its line and straight back, an octave mark slipped (owner's trial 2026-10-30: a
        // rock bass ended its A and B bars `… e cis'' | b …`, a C♯4 between E2 and B2, a passing note
        // two octaves up). Not a piano's hand, whose stride leaps so by nature, nor a pedal riff,
        // whose notes leap so one after another: a slip stands alone.
        var spikeBars = midi.BarTicks.Where(b => b > 0).Distinct().OrderBy(b => b).ToList();
        foreach (var line in midi.Tracks.SelectMany(t => t.Notes.Where(n => n.Channel != 9).Select(n => (Part: n.Part ?? t.Name, n.StartTick, n.Pitch)))
            .Where(n => !n.Part.EndsWith(" (chords)", System.StringComparison.Ordinal) && !hands.ContainsKey(n.Part))
            .GroupBy(n => n.Part))
        {
            var lows = line.GroupBy(n => n.StartTick).OrderBy(g => g.Key)
                .Select(g => (Tick: g.Key, Pitch: g.Min(n => n.Pitch), Chord: g.Count() > 1)).ToList();
            bool Spike(int i)
            {
                if (i <= 0 || i + 1 >= lows.Count)
                    return false;
                var (before, after) =(lows[i].Pitch - lows[i - 1].Pitch, lows[i].Pitch - lows[i + 1].Pitch);
                return (before > 12 && after > 12) || (before < -12 && after < -12);
            }
            // A chord is not one: a guitar's chord struck under its own tune (owner's trial 2026-10-30:
            // `<d, a>2 fis'8 e8 d8 fis8` in every bar of a chorus was told of fifteen slips).
            var slips = Enumerable.Range(0, lows.Count).Where(i => Spike(i) && !lows[i].Chord && !Spike(i - 1) && !Spike(i + 1)).ToList();
            if (slips.Count < SlipFewest)
                continue;
            var at = slips.Select(i => 1 + spikeBars.Count(b => b <= lows[i].Tick)).Distinct().ToList();
            int first = slips[0];
            findings.Add($"{line.Key}: {slips.Count} times one note leaps more than an octave away and straight back (bars "
                + $"{string.Join(", ", at.Take(6))}{(at.Count > 6 ? ", …" : "")}, as the form plays them; "
                + $"{PartRanges.SpelledFromPitch(lows[first].Pitch, fifths)} between {PartRanges.SpelledFromPitch(lows[first - 1].Pitch, fifths)} and "
                + $"{PartRanges.SpelledFromPitch(lows[first + 1].Pitch, fifths)} first) — an octave mark slipped? Write it in its line's octave.");
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
            string Name(int m) => PartRanges.SpelledFromPitch(m, fifths);
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
                findings.Add($"{rights[0]}, {lefts[0]}: the hands stand far apart — the left hand mostly up to {PartRanges.SpelledFromPitch(lhTop, fifths)}, "
                    + $"the right hand mostly from {PartRanges.SpelledFromPitch(rhFloor, fifths)} — and the middle of the keyboard is empty: let the left "
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
                    + $"({PartRanges.SpelledFromPitch(over[0].Low, fifths)} to {PartRanges.SpelledFromPitch(over[0].High, fifths)} in bar {over[0].Bar}): a hand's "
                    + "figure stays within an octave and a half, climbing from its bass by the chord's tones and turning back.");
        }

        findings.AddRange(Clashes(midi, melodies, barList, fifths));
        findings.AddRange(LowChords(midi, barList, fifths));
        findings.AddRange(ThickChordsOnADistortedSound(midi, parts, barList, fifths));
        // A band's part crowding its bass (owner's trial 2026-10-30: a rock guitar mostly D2–D3 over a
        // bass mostly E1–E2 — two low lines in one register, a muddy sound).
        foreach (var bass in bandBasses.Where(sounding.ContainsKey))
        {
            int under = PercentileOf(sounding[bass], 0.5);
            foreach (var (part, notes) in sounding)
            {
                if (part == bass || bandBasses.Contains(part) || hands.ContainsKey(part) || notes.Count < 16
                    || part.EndsWith(" (chords)", System.StringComparison.Ordinal))
                    continue;
                int middle = PercentileOf(notes, 0.5);
                if (middle - under < 12)
                    findings.Add($"{part}: most of its time round {PartRanges.SpelledFromPitch(middle, fifths)}, within an octave of the bass's "
                        + $"{PartRanges.SpelledFromPitch(under, fifths)} — two low lines in one register muddy each other: lift it an octave "
                        + "(a guitar's chords round C3 to C5, its tune above them), and leave the low register to the bass.");
            }
        }
        if (band)
        {
            findings.AddRange(OneGroove(midi, barList));
            // Nobody playing the chords (owner's trial 2026-10-30: told it had no tune, a rock trio's
            // guitar played single notes from start to end, the harmony the bass's root alone).
            var strummers = parts.Where(p => RoleOf(p).Chords).Select(p => p.Name.Text).ToHashSet(System.StringComparer.Ordinal);
            // A riff on the root is single notes low down (owner's trial, the same day: `a,8 r a, c e r g e`
            // under the tune is the harmony too): a bar with a chord, or whose notes lie mostly under C4.
            var onsets = midi.Tracks.SelectMany(t => t.Notes.Where(n => n.Channel != 9).Select(n => (Part: n.Part ?? t.Name, n.StartTick, n.Pitch)))
                .Where(n => strummers.Contains(n.Part)).GroupBy(n => (n.Part, n.StartTick))
                .Select(g => (Bar: barList.Count(b => b <= g.Key.StartTick), Chord: g.Count() > 1, Low: g.Min(n => n.Pitch) < 60)).ToList();
            int strumBars = onsets.Select(o => o.Bar).Distinct().Count();
            int chordBars = onsets.GroupBy(o => o.Bar).Count(b => b.Any(o => o.Chord) || b.Count(o => o.Low) * 2 > b.Count());
            bool chordRow = midi.Tracks.Any(t => t.Notes.Any(n => (n.Part ?? t.Name).EndsWith(" (chords)", System.StringComparison.Ordinal)));
            if (!chordRow && strumBars >= 8 && chordBars < strumBars * NoChordsShare)
                findings.Add($"Nobody plays the chords: {string.Join(", ", strummers)} plays a tune of single notes in {strumBars - chordBars} of its "
                    + $"{strumBars} bars, and the harmony is the bass's root alone — a band's guitar (or keyboards) strikes the chords: "
                    + $"power chords or a riff on the root {(shared.Length > 0 ? "under the verse" : "through the verse and the chorus")}, "
                    + "open chords ringing in a quiet part; its "
                    + "tune goes over them in a solo, a fill, or a lead part of its own." + shared);
        }
        // The accompaniments' figures, section by section (the melody is read above, the drums in
        // OneGroove): every pitched part that is not a melody, with enough notes to have a figure.
        findings.AddRange(OneFigure(midi, barList, byPart.Where(p => !melodies.Contains(p.Key) && p.Value.Count >= 16)
            .Select(p => p.Key).ToHashSet(System.StringComparer.Ordinal)));
        if (parts.Select(p => p.Name.Text).FirstOrDefault(n => melodies.Contains(n) && byPart.ContainsKey(n)) is { } tune)
            findings.AddRange(OpenEndings(midi, tune, fifths));
        findings.AddRange(EndsApart(midi, barList));

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
            // The bars it takes, at the piece's own tempo and meter (owner's trial 2026-10-30: told "about
            // 24 to 32 bars of 4/4", a rock piece at ♩ = 168 grew from 32 bars to 44 and stopped, at 63 seconds).
            findings.Add($"The piece is {barCount} bars long, about {seconds} seconds played: unless a short piece was asked for, give it "
                + $"the length its form needs — a minute and a half to three minutes ({BarsFor(seconds, barCount)}), its phrases long "
                + "enough to say something, each closing on a cadence. Lengthen it "
                + "with music — a section more, a varied return, an introduction — never by slowing its tempo (keep the tempo its "
                + "character asks for). A section that returns as it was is named again in the form, and that counts toward the "
                + "length; what is added is new music, or a return that changes in a way the listener hears — not a copy of an "
                + "earlier section under a new name with a few bars altered.");
        if (PlayedKeys(root) is { } inKeys && pitched.Count > 0)
        {
            var (home, end) = inKeys;
            int homeTonic = PitchClass(home.Tonic), endTonic = PitchClass(end.Tonic);
            string KeyName((string Tonic, string Mode) k) => $"{TonicName(k.Tonic)} {k.Mode}";
            if (homeTonic != endTonic || home.Mode != end.Mode)
                findings.Add($"The piece begins in {KeyName(home)} and ends in {KeyName(end)}: come back to the home key for the last "
                    + "section, and close there.");
            // The lowest note sounding at the last onset: the final chord's bass.
            int lastOnset = pitched.Max(n => n.StartTick);
            int bass = pitched.Where(n => n.StartTick <= lastOnset && n.StartTick + n.DurationTicks > lastOnset).Min(n => n.Pitch);
            if ((bass % 12 + 12) % 12 != endTonic)
                findings.Add($"The piece ends over {PitchClassName(bass, fifths)}, not its tonic {PitchClassName(endTonic, fifths)}: close with a cadence "
                    + $"(V–I) on the tonic chord, held through the last bar, the bass on {PitchClassName(endTonic, fifths)}.");
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

    // A note on the beat a semitone from the harmony, this many times and in this share of the
    // melody's long notes on the beat: a wrong note, not a passing one (owner's request 2026-10-30).
    private const int ClashFewest = 3;
    private const double ClashShare = 0.05;

    /// <summary>A melody's notes held on the beat (a quarter or longer) a semitone or a minor ninth over
    /// a note another part sounds with them at their start, or a semitone under one. A suspension is
    /// struck before the harmony moves under it, so it is not one.</summary>
    private static IEnumerable<string> Clashes(MidiFile midi, HashSet<string> melodies, List<int> barList, int fifths)
    {
        int tpq = midi.TicksPerQuarterNote;
        var notes = midi.Tracks.SelectMany(t => t.Notes.Where(n => n.Channel != 9)
            .Select(n => (Part: n.Part ?? t.Name, n.StartTick, n.DurationTicks, n.Pitch))).ToList();
        foreach (var part in melodies)
        {
            var line = notes.Where(n => n.Part == part).GroupBy(n => n.StartTick).Where(g => g.Count() == 1).Select(g => g.First()).ToList();
            var others = notes.Where(n => n.Part != part).ToList();
            var held = line.Where(m => m.DurationTicks >= tpq && (m.StartTick - barList.LastOrDefault(b => b <= m.StartTick)) % tpq == 0).ToList();
            var clashes = new List<(int Bar, int Note, int Against)>();
            foreach (var m in held)
            {
                // A semitone or a minor ninth over a note, or a semitone under one close by — not a major
                // seventh over the bass, a chord's colour (owner's trial 2026-10-30: B♭ over a C♭ bass,
                // ♭VImaj7, was told a wrong note).
                var against = others.FirstOrDefault(n => n.StartTick <= m.StartTick && m.StartTick < n.StartTick + n.DurationTicks
                    && (m.Pitch - n.Pitch) is var gap && (gap > 0 ? gap % 12 == 1 || gap == 11 : -gap % 12 is 1 or 11));
                if (against.Part != null)
                    clashes.Add((barList.Count(b => b <= m.StartTick), m.Pitch, against.Pitch));
            }
            if (clashes.Count < ClashFewest || clashes.Count < held.Count * ClashShare)
                continue;
            var bars = clashes.Select(c => c.Bar).Distinct().ToList();
            yield return $"{part}, the melody: {clashes.Count} times a note held on the beat stands a semitone from the harmony sounding "
                + $"with it (bars {string.Join(", ", bars.Take(6))}{(bars.Count > 6 ? ", …" : "")}, as the form plays them; "
                + $"{PartRanges.SpelledFromPitch(clashes[0].Note, fifths)} against {PartRanges.SpelledFromPitch(clashes[0].Against, fifths)} first) — "
                + "a wrong note? Take the chord's own tone there, or change the chord under it; a note against the chord is a "
                + "passing one, short and moving on by step — unless it is a blue note meant, a minor third over a major chord.";
        }
    }

    // A third or a second stacked under C3 (MIDI 48) muddies — the low interval limit — in this many
    // of a part's chords (owner's trial 2026-10-30: a rock guitar's E chord `<e, gis b>`, E2 G♯2 B2).
    private const int LowThirdCeiling = 48, LowChordsFewest = 4;

    // A band's chord instruments striking a chord in fewer of their bars than this: nobody plays them.
    private const double NoChordsShare = 0.1;

    /// <summary>A part's chords with a third or a second under C3.</summary>
    private static IEnumerable<string> LowChords(MidiFile midi, List<int> barList, int fifths)
    {
        foreach (var part in midi.Tracks.SelectMany(t => t.Notes.Where(n => n.Channel != 9).Select(n => (Part: n.Part ?? t.Name, n.StartTick, n.Pitch)))
            .GroupBy(n => n.Part))
        {
            var muddy = part.GroupBy(n => n.StartTick).Select(g => (Tick: g.Key, Pitches: g.Select(n => n.Pitch).Distinct().OrderBy(p => p).ToList()))
                .Where(c => c.Pitches.Count >= 2 && c.Pitches.Zip(c.Pitches.Skip(1)).Any(p => p.Second - p.First <= 4 && p.First < LowThirdCeiling))
                .ToList();
            if (muddy.Count < LowChordsFewest)
                continue;
            var first = muddy[0];
            yield return $"{part.Key}: {muddy.Count} chords stack a third or a second below C3 ("
                + $"{string.Join(" ", first.Pitches.Select(p => PartRanges.SpelledFromPitch(p, fifths)))} in bar {barList.Count(b => b <= first.Tick)} first, "
                + "as the form plays them) — so low, close notes are mud: voice it open, the root and fifth below (a power chord) "
                + "and the third an octave or more higher.";
        }
    }

    // A chord of this many notes or more on a distorted sound, in half of a part's chords, of this many
    // (owner's report 2026-11-01: `Downloads/Redline.lys`, an overdriven guitar in `<e, e b e' b'>` —
    // the voicing tool's first E5 shape, 07x457 — from start to end).
    private const int ThickChordNotes = 4, ThickChordsFewest = 8;
    private const double ThickChordsShare = 0.5;

    /// <summary>A part on a distorted sound (<see cref="ChordVoicingFinder.IsDistorted"/>) striking
    /// full chords: distortion blurs them; two or three notes — a power chord — ring clear.</summary>
    private static IEnumerable<string> ThickChordsOnADistortedSound(MidiFile midi, IReadOnlyList<PartDeclarationSyntax> parts,
        List<int> barList, int fifths)
    {
        foreach (var part in parts)
        {
            string sound = PropOf(part, "midiInstrument");
            if (!ChordVoicingFinder.IsDistorted(sound))
                continue;
            var chords = midi.Tracks.SelectMany(t => t.Notes.Where(n => n.Channel != 9 && (n.Part ?? t.Name) == part.Name.Text))
                .GroupBy(n => n.StartTick).Select(g => (Tick: g.Key, Pitches: g.Select(n => n.Pitch).Distinct().OrderBy(p => p).ToList()))
                .Where(c => c.Pitches.Count >= 2).OrderBy(c => c.Tick).ToList();
            var thick = chords.Where(c => c.Pitches.Count >= ThickChordNotes).ToList();
            if (chords.Count < ThickChordsFewest || thick.Count < chords.Count * ThickChordsShare)
                continue;
            var first = thick[0];
            yield return $"{part.Name.Text}, on a distorted sound ({sound}): {thick.Count} of its {chords.Count} chords are {ThickChordNotes} notes or more "
                + $"({string.Join(" ", first.Pitches.Select(p => PartRanges.SpelledFromPitch(p, fifths)))} in bar {barList.Count(b => b <= first.Tick)} first, "
                + "as the form plays them) — distortion blurs a full chord into a roar: strike two or three notes, the root and its fifth "
                + "with the octave above (a power chord; the voicing tool, given the sound, answers them first), and leave the full chord "
                + "to a clean sound or to one ringing chord at a close.";
        }
    }

    /// <summary>The sections as played, by name, each once: its first play's start and end.</summary>
    private static List<(string Name, int Start, int End)> PlayedSpans(MidiFile midi)
    {
        var starts = midi.SectionStarts.ToList();
        return starts.Select((s, i) => (Name: s.Value, Start: s.Key, End: i + 1 < starts.Count ? starts[i + 1].Key : midi.EndTick))
            .Where(s => s.End > s.Start).GroupBy(s => s.Name).Select(g => g.First()).ToList();
    }

    /// <summary>How many sections a song has before "one beat in all but one of them" is said
    /// (<see cref="OneGroove"/>) and "one figure in all but one" (<see cref="OneFigure"/>).</summary>
    private const int OneBeatFewestSections = 6;

    /// <summary>
    /// An accompanying part — a bass, a piano's hand, a strummed part, anything that is not the melody
    /// and not the drums — playing one figure in every section (or in all but one of a long piece):
    /// the commonest bar of each section the same, read as the onsets' places and each note's interval
    /// from the bar's first (so the figure moved along the chords is one figure), where that bar owns
    /// at least half the section. The drums' twin is <see cref="OneGroove"/>; the guide's rule is
    /// general ("a contrasting section takes a figure of its own"), and so is this.
    /// </summary>
    private static IEnumerable<string> OneFigure(MidiFile midi, List<int> barList, IReadOnlySet<string> accompaniments)
    {
        var spans = PlayedSpans(midi);
        if (spans.Count < 3)
            yield break;
        foreach (var part in midi.Tracks.SelectMany(t => t.Notes.Where(n => n.Channel != 9)
                     .Select(n => (Part: n.Part ?? t.Name, n.StartTick, n.Pitch))).GroupBy(n => n.Part))
        {
            if (!accompaniments.Contains(part.Key))
                continue;
            string? Commonest(int start, int end)
            {
                var bars = barList.Where(b => b >= start && b < end).ToList();
                var patterns = bars.Select((b, i) =>
                {
                    int next = i + 1 < bars.Count ? bars[i + 1] : end;
                    var inBar = part.Where(n => n.StartTick >= b && n.StartTick < next).GroupBy(n => n.StartTick).OrderBy(g => g.Key)
                        .Select(g => (Offset: g.Key - b, Pitch: g.Min(n => n.Pitch))).ToList();
                    if (inBar.Count == 0)
                        return "";
                    int first = inBar[0].Pitch;
                    return string.Join(";", inBar.Select(o => $"{o.Offset}:{o.Pitch - first}"));
                }).Where(p => p.Length > 0).ToList();
                if (patterns.Count < 2)
                    return null;
                var top = patterns.GroupBy(p => p).OrderByDescending(g => g.Count()).First();
                return top.Count() * 2 >= patterns.Count ? top.Key : null;
            }
            var figures = spans.Select(s => (s.Name, Figure: Commonest(s.Start, s.End))).Where(f => f.Figure != null).ToList();
            if (figures.Count < 3)
                continue;
            var shared = figures.GroupBy(f => f.Figure).OrderByDescending(g => g.Count()).First();
            bool every = shared.Count() == figures.Count;
            if (!every && (figures.Count < OneBeatFewestSections || shared.Count() < figures.Count - 1))
                continue;
            string where = every
                ? $"every section ({string.Join(", ", figures.Select(f => f.Name))})"
                : $"{shared.Count()} of the {figures.Count} sections (all but {string.Join(", ", figures.Where(f => f.Figure != shared.Key).Select(f => f.Name))})";
            yield return $"{part.Key}: the same figure in {where} — a contrasting section takes a figure of its own: another rhythm, "
                + "the chord's notes in another order, longer notes under a quiet part, a run or a fill in the figure's last bar "
                + "leading into the next section.";
        }
    }

    /// <summary>A band's drums playing one groove in every section: the commonest bar of each the same
    /// (owner's trial 2026-10-30: a rock trio's kick and snare `bd4 sn4 bd8 bd8 sn4` in all 52 bars),
    /// or its kick and snare the same while only the cymbals change.</summary>
    private static IEnumerable<string> OneGroove(MidiFile midi, List<int> barList)
    {
        var spans = PlayedSpans(midi);
        foreach (var kit in midi.Tracks.SelectMany(t => t.Notes.Where(n => n.Channel == 9).Select(n => (Part: n.Part ?? t.Name, n.StartTick, n.Pitch)))
            .GroupBy(n => n.Part))
        {
            string? Commonest(int start, int end, bool feet)
            {
                var bars = barList.Where(b => b >= start && b < end).ToList();
                var patterns = bars.Select((b, i) =>
                {
                    int next = i + 1 < bars.Count ? bars[i + 1] : end;
                    return string.Join(";", kit.Where(n => n.StartTick >= b && n.StartTick < next && (!feet || n.Pitch is >= 35 and <= 40))
                        .OrderBy(n => n.StartTick).ThenBy(n => n.Pitch).Select(n => $"{n.StartTick - b}:{n.Pitch}"));
                }).Where(p => p.Length > 0).ToList();
                return patterns.Count < 2 ? null : patterns.GroupBy(p => p).OrderByDescending(g => g.Count()).First().Key;
            }
            var grooves = spans.Select(s => (s.Name, All: Commonest(s.Start, s.End, false), Feet: Commonest(s.Start, s.End, true)))
                .Where(g => g.All != null).ToList();
            string names = string.Join(", ", grooves.Select(g => g.Name));
            if (grooves.Count >= 3 && grooves.Select(g => g.All).Distinct().Count() == 1)
                yield return $"{kit.Key}: the same groove in every section ({names}) — the drums change with the song: the closed hi-hat "
                    + "in the verse, the ride or crashes and the open hi-hat in the chorus, half-time or the toms in a quiet bridge, and a "
                    + "fill into each new section.";
            else if (grooves.Count >= 4 && grooves.Select(g => g.Feet).Distinct().Count() == 1)
                yield return $"{kit.Key}: the kick and the snare play one beat in every section ({names}), only the cymbals change — let "
                    + "a section change the beat itself: half-time in a bridge or a breakdown (the snare on 3), the kick driving harder "
                    + "in the last chorus.";
            // …or in every section but one of a long song (owner's review 2026-11-01: a 72-bar
            // instrumental's kick and snare were one beat in eight of its nine sections, the bridge
            // alone in half-time — the four-section rule above let it by). One section of
            // difference is a contrast; one in six or more is the exception that shows the rule.
            else if (grooves.Count >= OneBeatFewestSections
                && grooves.GroupBy(g => g.Feet).OrderByDescending(g => g.Count()).First() is { } oneBeat
                && oneBeat.Count() >= grooves.Count - 1)
                yield return $"{kit.Key}: the kick and the snare play one beat in {oneBeat.Count()} of the {grooves.Count} sections "
                    + $"(all but {string.Join(", ", grooves.Where(g => g.Feet != oneBeat.Key).Select(g => g.Name))}), only the cymbals "
                    + "change — let more than one section change the beat itself: half-time in a bridge or a breakdown (the snare on 3), "
                    + "the kick driving harder in the last chorus, a lighter beat under a quiet verse.";
            // The same bar over and over, no fill and no crash between (owner's review 2026-10-30: a rock
            // trio's drums played one bar sixteen times running through its verse, and again in its last
            // chorus) — the groove is right, the phrases go unmarked.
            int run = 0, longest = 0, longestEnd = 0;
            string? last = null;
            for (int i = 0; i < barList.Count; i++)
            {
                int next = i + 1 < barList.Count ? barList[i + 1] : midi.EndTick;
                string bar = string.Join(";", kit.Where(n => n.StartTick >= barList[i] && n.StartTick < next)
                    .OrderBy(n => n.StartTick).ThenBy(n => n.Pitch).Select(n => $"{n.StartTick - barList[i]}:{n.Pitch}"));
                run = bar.Length > 0 && bar == last ? run + 1 : bar.Length > 0 ? 1 : 0;
                last = bar;
                if (run > longest)
                    (longest, longestEnd) = (run, i);
            }
            // Bars as the page numbers them: a pickup is bar 0.
            int shift = barList.Count >= 3 && barList[1] - barList[0] < barList[2] - barList[1] ? 0 : 1;
            if (longest >= SameDrumBarsFewest)
                yield return $"{kit.Key}: the same bar {longest} times running (bars {longestEnd - longest + 1 + shift}-{longestEnd + shift}, as the form "
                    + "plays them) — a drummer marks the phrases: a fill in the last bar of each four or eight leading into the next, a crash "
                    + "on its first beat, the groove kept between.";
        }
    }

    /// <summary>The melody's last note in each section open against the bass under it — a second, a
    /// tritone or a seventh: its phrase not closed. Not a fourth: a band's bass passes through the
    /// chord's fifth under the root it ends on (owner's request 2026-10-30; the length
    /// finding asks for phrases "each closing on a cadence", and nothing saw whether they did).</summary>
    private static IEnumerable<string> OpenEndings(MidiFile midi, string tune, int fifths)
    {
        var notes = midi.Tracks.SelectMany(t => t.Notes.Where(n => n.Channel != 9)
            .Select(n => (Part: n.Part ?? t.Name, n.StartTick, n.DurationTicks, n.Pitch))).ToList();
        var spans = PlayedSpans(midi);
        var open = new List<string>();
        int read = 0;
        foreach (var (name, start, end) in spans)
        {
            var mine = notes.Where(n => n.Part == tune && n.StartTick >= start && n.StartTick < end).ToList();
            if (mine.Count == 0)
                continue;
            int last = mine.Max(n => n.StartTick);
            int top = mine.Where(n => n.StartTick == last).Max(n => n.Pitch);
            var under = notes.Where(n => n.Part != tune && n.StartTick <= last && last < n.StartTick + n.DurationTicks).ToList();
            if (under.Count == 0)
                continue;
            read++;
            int bass = under.Min(n => n.Pitch);
            if (((top - bass) % 12 + 12) % 12 is 1 or 2 or 6 or 10 or 11)
                open.Add($"{name}: {PartRanges.SpelledFromPitch(top, fifths)} over {PartRanges.SpelledFromPitch(bass, fifths)}");
        }
        if (read >= 3 && open.Count >= 2 && open.Count * 3 >= read)
            yield return $"{tune}: {open.Count} of {read} sections end their tune on a note open against the bass ({string.Join(", ", open.Take(4))}"
                + $"{(open.Count > 4 ? ", …" : "")}) — a section closes its phrase: end it on the chord's own tone (the root, the third "
                + "or the fifth over the bass), held, a cadence the next section starts from.";
    }

    /// <summary>
    /// The last bar with some parts holding their final note or chord from its first beat and another
    /// still running on through it (owner's review 2026-10-30: a rock trio's guitar held its last chord
    /// and the drums struck a crash and a kick for the whole bar while the bass went on in eighths — the
    /// band did not end together). A part holds when everything it plays there starts on the downbeat;
    /// it runs on with four onsets or more.
    /// </summary>
    private static IEnumerable<string> EndsApart(MidiFile midi, List<int> barList)
    {
        int end = midi.EndTick;
        var starts = barList.Where(b => b < end).ToList();
        if (starts.Count < 2)
            yield break;
        int last = starts[^1];
        var inLast = midi.Tracks.SelectMany(t => t.Notes.Select(n => (Part: n.Part ?? t.Name, n.StartTick)))
            .Where(n => n.StartTick >= last && n.StartTick < end && !n.Part.EndsWith(" (chords)", System.StringComparison.Ordinal))
            .GroupBy(n => n.Part).ToDictionary(g => g.Key, g => g.Select(n => n.StartTick).Distinct().Count());
        var heldOn = midi.Tracks.SelectMany(t => t.Notes.Select(n => (Part: n.Part ?? t.Name, n.StartTick)))
            .Where(n => n.StartTick >= last && n.StartTick < end).GroupBy(n => n.Part)
            .Where(g => g.All(n => n.StartTick == last)).Select(g => g.Key).ToList();
        var running = inLast.Where(p => p.Value >= 4).Select(p => p.Key).ToList();
        if (heldOn.Count == 0 || running.Count == 0)
            yield break;
        yield return $"{string.Join(", ", running)} {(running.Count == 1 ? "runs" : "run")} on through the last bar "
            + $"({inLast[running[0]]} notes) while {string.Join(" and ", heldOn)} {(heldOn.Count == 1 ? "holds its" : "hold their")} "
            + "last note from its first beat — the parts end together: let every part land on the final chord on the same beat "
            + "and hold it (the bass on the root), or all stop together on a hit.";
    }

    /// <summary>The bars a minute and a half to three minutes take at the piece's own pace — its
    /// seconds over its bars; the guide's 4/4 count where it played no time.</summary>
    private static string BarsFor(int seconds, int bars)
    {
        if (seconds <= 0 || bars <= 0)
            return "about 24 to 32 bars of a moderate 4/4";
        double perBar = (double)seconds / bars;
        return $"at this tempo and meter, about {(int)System.Math.Ceiling(90 / perBar)} to {(int)System.Math.Round(180 / perBar)} bars";
    }

    // A compound meter's melody notes that start within a beat and run across the next, in this many
    // bars and this share of them (owner's trial 2026-10-29: `b'8 d''4. b'8 a'8` in 8 of a 6/8 tune's bars).
    private const int AcrossBeatFewestBars = 3;

    // A band's line in a bar: most of its onsets single notes; and the share of the bars a tune is
    // heard in, under which an instrumental has none to speak of (owner's report 2026-10-29).
    private const double TuneSingleShare = 0.7, TunedBarsShare = 0.5, BassLeapShare = 0.2;
    private const int SlipFewest = 3;
    private const double MelodyStepShare = 0.25;

    // A melody whose bars take the shape of the bar before in this share of them, over this many bars.
    // MEASURED (2026-10-30, the owner's 60 composed pieces and two folk tunes): the sequenced rock
    // guitars 0.53–0.61, everything else 0.42 or less, Ode to Joy 0.11.
    private const double ContourShare = 0.5;
    private const int ContourFewestBars = 8;

    /// <summary>
    /// How many of a melody's bars move in the SHAPE of the bar just before — the directions from
    /// single note to single note within the bar, a repeated note level ("↗↗↘↘") — counted over the
    /// bars with three moves or more; the commonest such shape. A theme returning bars apart is not
    /// counted: only a figure moved on bar after bar is.
    /// </summary>
    internal static (int Alike, int Bars, string Shape) BarContours(MidiFile midi, string part, List<int> barList)
    {
        var line = midi.Tracks.SelectMany(t => t.Notes.Where(n => (n.Part ?? t.Name) == part && n.Channel != 9))
            .GroupBy(n => n.StartTick).OrderBy(g => g.Key).Where(g => g.Count() == 1)
            .Select(g => (Tick: g.Key, g.First().Pitch)).ToList();
        var shapes = line.GroupBy(n => barList.Count(b => b <= n.Tick))
            .Select(bar => (Bar: bar.Key, Pitches: bar.Select(n => n.Pitch).ToList()))
            .Where(p => p.Pitches.Count >= 4)
            .Select(p => (p.Bar, Shape: string.Concat(p.Pitches.Zip(p.Pitches.Skip(1), (a, b) => b > a ? '↗' : b < a ? '↘' : '→'))))
            .ToList();
        if (shapes.Count == 0)
            return (0, 0, "");
        // A bar in the shape of the bar just before it: a figure moved on, not a phrase returning
        // (a theme played again stands bars apart, as Ode to Joy's do).
        var repeated = shapes.Skip(1).Where((s, i) => s.Bar == shapes[i].Bar + 1 && s.Shape == shapes[i].Shape).ToList();
        string commonest = repeated.Count > 0 ? repeated.GroupBy(s => s.Shape).OrderByDescending(g => g.Count()).First().Key : "";
        return (repeated.Count, shapes.Count, commonest);
    }
    private const int LeapsFewestMoves = 16;
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
    /// A section that plays an earlier one again, note for note in every part, only its marks — levels,
    /// accents — changed (owner's review 2026-10-30: a rock trio's last chorus was its chorus again at
    /// @ff, written out in phrases of new names, an octave below the solo before it — no lift).
    /// <see cref="CopiedSections"/> reads the written text and misses it: here each part's phrases are
    /// read in place and the marks left out. A section copied as written is said there, not here.
    /// </summary>
    private static IEnumerable<string> ReplayedSections(SyntaxNode root)
    {
        var phrases = SectionBarCounts.PhraseBodies(root);
        string source = root.ToFullString();
        // The node's text, each phrase reference in it replaced by the phrase it plays.
        string Played(SyntaxNode node, int depth)
        {
            var text = new System.Text.StringBuilder();
            int at = node.Span.Start;
            foreach (var r in node.DescendantNodes<VariableReferenceSyntax>().OrderBy(r => r.Span.Start))
            {
                if (r.Span.Start < at || depth >= 8 || !phrases.TryGetValue(r.Name.Text, out var body))
                    continue;
                text.Append(source, at, r.Span.Start - at).Append(' ').Append(Played(body, depth + 1)).Append(' ');
                at = r.Span.Start + r.Span.Length;
            }
            return text.Append(source, at, node.Span.Start + node.Span.Length - at).ToString();
        }
        static string Unmarked(string text) => System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(text, @"@\s*[\w.()<>!]+", ""), @"\s+", " ").Trim();
        var sections = root.DescendantNodes<SectionDeclarationSyntax>().Where(s => s.Parent is CompilationUnitSyntax)
            .GroupBy(s => s.SectionName).Select(g => g.First()).ToList();
        // The written body, as CopiedSections compares it.
        static string Written(SectionDeclarationSyntax s)
        {
            string text = s.ToFullString();
            return System.Text.RegularExpressions.Regex.Replace(text[System.Math.Max(0, text.IndexOf('{'))..], @"\s+", " ").Trim();
        }
        // Each part's bars, as written between its bar lines, its phrases in place and its marks left out.
        static string[] Bars(string played) => [.. played.Split('|').Select(b => b.Trim().Trim('{', '}').Trim())];
        var notes = sections.Select(s => (Section: s, Written: Written(s),
            Parts: s.ChildNodes().OfType<PartBlockSyntax>().ToDictionary(b => b.PartName.Text, b => Bars(Unmarked(Played(b, 0)))))).ToList();
        var said = new HashSet<string>();
        for (int later = 1; later < notes.Count; later++)
            for (int earlier = 0; earlier < later; earlier++)
            {
                var (a, b) = (notes[earlier], notes[later]);
                if (said.Contains(b.Section.SectionName) || b.Parts.Count == 0 || a.Parts.Count != b.Parts.Count
                    || a.Written == b.Written
                    || b.Parts.Any(p => !a.Parts.TryGetValue(p.Key, out var bars) || bars.Length != p.Value.Length))
                    continue;
                // Bar by bar, every part: the same but for its marks, or all but a bar or two
                // (owner's review 2026-10-30: a last chorus was its chorus again but for its final bar).
                int total = b.Parts.Sum(p => p.Value.Length);
                int differ = b.Parts.Sum(p => p.Value.Where((bar, i) => bar != a.Parts[p.Key][i]).Count());
                if (differ > 0 && (total < 8 || differ > total * (1 - NearlyReplayedShare)))
                    continue;
                said.Add(b.Section.SectionName);
                // ⚠️ NOT LOWERED TO "MOST OF ITS BARS" (tried 2026-11-01, on a rock instrumental that
                // grew from 33 bars to 72 by writing its verse, pre-chorus and chorus again under new
                // names): three quarters of the bars alike, every part's counted, named a second verse
                // whose tune was new over the same bass and drums — the ordinary shape of a song — and
                // the owner's 2026-10-30 rule stands: a tune changed in three bars of eight is music of
                // its own. The length finding says instead that a copy under a new name is not length.
                string how = differ == 0 ? "note for note in every part — only its levels and marks differ"
                    : $"note for note in every part but {differ} {(differ == 1 ? "bar" : "bars")} — elsewhere only its levels and marks differ";
                yield return $"{b.Section.SectionName} plays {a.Section.SectionName} again, {how}: a section that returns to end the piece "
                    + $"lifts — the tune an octave up or in harmony, a held note at its top, its bars changed toward its close, the drums "
                    + $"opened up — or, if it is the same music, name {a.Section.SectionName} again in the form instead of a copy.";
            }
    }

    /// <summary>The share of a returning section's bars, every part's together, at or above which it
    /// is the earlier one played again (<see cref="ReplayedSections"/>).</summary>
    private const double NearlyReplayedShare = 0.9;

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
        var phrases = root.DescendantNodes<PhraseDeclarationSyntax>().Where(p => p.Parent is CompilationUnitSyntax)
            .GroupBy(p => p.Name.Text).ToDictionary(g => g.Key, g => g.First());
        List<DynamicSyntax> MarksOf(string section, string part) => sections[section].DescendantNodes<PartBlockSyntax>()
            .Where(b => b.PartName.Text == part).SelectMany(b => DynamicsPlayed(b, phrases)).ToList();
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

    /// <summary>
    /// The same drop between two phrases a part plays one after the other inside a section (owner's
    /// review 2026-10-30: a rock trio's bass reused its intro phrases under the chorus, so it went
    /// from the intro's last @ff straight back to its first @p, one section all through — the section
    /// check never met it). A part's own drops only, at most three told per part.
    /// </summary>
    private static IEnumerable<string> PhraseDrops(SyntaxNode root)
    {
        string[] order = ["pppp", "ppp", "pp", "p", "mp", "mf", "f", "ff", "fff", "ffff"];
        var phrases = root.DescendantNodes<PhraseDeclarationSyntax>().Where(p => p.Parent is CompilationUnitSyntax)
            .GroupBy(p => p.Name.Text).ToDictionary(g => g.Key, g => g.First());
        foreach (var part in root.DescendantNodes<PartBlockSyntax>().Where(b => b.Parent is SectionDeclarationSyntax { Parent: CompilationUnitSyntax })
            .GroupBy(b => b.PartName.Text))
        {
            var drops = new List<string>();
            foreach (var block in part)
            {
                // The references a line plays in turn: those of one music block (a voice's own line).
                var lines = block.DescendantNodes<VariableReferenceSyntax>().Where(r => phrases.ContainsKey(r.Name.Text))
                    .GroupBy(r => r.Parent);
                foreach (var line in lines)
                {
                    var refs = line.ToList();
                    for (int i = 0; i + 1 < refs.Count && drops.Count < 3; i++)
                    {
                        var before = DynamicsPlayed(phrases[refs[i].Name.Text], phrases);
                        var after = DynamicsPlayed(phrases[refs[i + 1].Name.Text], phrases);
                        if (before.Count == 0 || after.Count == 0)
                            continue;
                        int loud = System.Array.IndexOf(order, before[^1].DynamicToken.Text);
                        var first = after[0];
                        int soft = System.Array.IndexOf(order, first.DynamicToken.Text);
                        if (loud < 0 || soft < 0 || first.IsSubito || loud - soft < 2)
                            continue;
                        string said = $"{refs[i].Name.Text} ends at @{order[loud]} and {refs[i + 1].Name.Text} opens at @{order[soft]}";
                        if (!drops.Contains(said))
                            drops.Add(said);
                    }
                }
            }
            if (drops.Count > 0)
                yield return $"{part.Key}: {string.Join("; ", drops)} — played one after the other, a drop of two levels or more "
                    + "with nothing leading to it: bring the level down over the first one's last bars (a @decresc), or, if the "
                    + "sudden quiet is meant, write the second's level .subito. A phrase played again keeps the levels written in "
                    + "it: where it returns in a louder place, give it that place's levels (a phrase of its own).";
        }
    }

    /// <summary>
    /// A drum part whose crashes mostly come in off the bars the other parts' phrases begin on
    /// (owner's review 2026-10-30: a rock trio's drum line, re-cut to end with the song, had its
    /// hands a bar early from then on — crashes on the last bar of each phrase, under the fill).
    /// A crash marks where a phrase begins; held crashes are one, counted from their first bar.
    /// Read section by section, in the section's own bars: the phrase starts are where the other
    /// parts' phrase references begin, so a section written in notes alone says nothing.
    /// </summary>
    private static IEnumerable<string> CrashesOffThePhrases(SyntaxNode root, HashSet<string> drums)
    {
        if (drums.Count == 0)
            return [];
        var bodies = SectionBarCounts.PhraseBodies(root);
        var crashes = root.DescendantNodes<DrumNoteSyntax>()
            .Where(n => DrumNameRegistry.TryGet(n.DrumName, out var info) && info.GmKey is 49 or 57)
            .Select(n => n.Span.Start).ToHashSet();
        if (crashes.Count == 0)
            return [];
        var findings = new List<string>();
        foreach (var section in root.DescendantNodes<SectionDeclarationSyntax>().Where(s => s.Parent is CompilationUnitSyntax))
        {
            var blocks = section.ChildNodes().OfType<PartBlockSyntax>().ToList();
            // The bars the other parts' phrases begin on.
            var starts = new HashSet<int>();
            foreach (var block in blocks.Where(b => !drums.Contains(b.PartName.Text)))
            {
                var index = new Dictionary<(string Chain, int Position), int>();
                Semantics.MeasureModel.Split(block, bodies, index: index);
                foreach (var g in index.Where(e => e.Key.Chain != "").GroupBy(e => e.Key.Chain.Split('/')[0]))
                    starts.Add(g.Min(e => e.Value));
            }
            if (starts.Count < 2)
                continue;
            foreach (var kit in blocks.Where(b => drums.Contains(b.PartName.Text)))
            {
                var bars = new HashSet<int>();
                var index = new Dictionary<(string Chain, int Position), int>();
                var split = Semantics.MeasureModel.Split(kit, bodies, index: index);
                AddCrashBars(index, 0);
                // A span's later voices are split apart, from the bar the span begins in.
                foreach (var span in kit.DescendantNodes<ParallelExpressionSyntax>())
                {
                    var voices = span.Voices.ToList();
                    if (voices.Count < 2)
                        continue;
                    var lead = voices[0].Span;
                    int from = index.Where(e => (e.Key.Chain == "" ? e.Key.Position : int.Parse(e.Key.Chain.Split('/')[0])) is var w
                            && w >= lead.Start && w < lead.Start + lead.Length)
                        .Select(e => e.Value).DefaultIfEmpty(0).Min();
                    foreach (var voice in voices.Skip(1))
                    {
                        var own = new Dictionary<(string Chain, int Position), int>();
                        Semantics.MeasureModel.Split(voice, bodies, index: own);
                        AddCrashBars(own, from);
                    }
                }
                void AddCrashBars(Dictionary<(string Chain, int Position), int> found, int offset)
                {
                    foreach (var e in found)
                        if (crashes.Contains(e.Key.Position))
                            bars.Add(e.Value + offset);
                }
                var entries = bars.Where(b => !bars.Contains(b - 1)).OrderBy(b => b).ToList();
                var off = entries.Where(b => !starts.Contains(b)).ToList();
                if (off.Count < 2 || off.Count * 2 <= entries.Count)
                    continue;
                // Bars as the page numbers them: a pickup is bar 0.
                int shift = split.Count >= 2 && split[0].Duration < split[1].Duration ? 0 : 1;
                string Bars(IEnumerable<int> list, int most)
                {
                    var shown = list.OrderBy(b => b).Select(b => (b + shift).ToString()).ToList();
                    return string.Join(", ", shown.Take(most)) + (shown.Count > most ? ", ..." : "");
                }
                findings.Add($"{kit.PartName.Text}: {off.Count} of its {entries.Count} crashes in {section.SectionName} come in off the bars "
                    + $"the phrases begin on (bars {Bars(off, 6)}; the other parts' phrases begin at bars {Bars(starts, 8)}) — a crash "
                    + "marks where a phrase begins, with the fill in the bar before it: move each crash to the first bar of the phrase "
                    + "it announces, and keep the time (hi-hat or ride) going through the bars between.");
            }
        }
        return findings;
    }

    /// <summary>The dynamics <paramref name="node"/> plays, in order, a phrase's marks in place of its
    /// reference (owner's trial 2026-10-30: a piece written all in phrases went from @ff to @p
    /// unsaid, its levels inside them).</summary>
    private static List<DynamicSyntax> DynamicsPlayed(SyntaxNode node, IReadOnlyDictionary<string, PhraseDeclarationSyntax> phrases,
        int depth = 0)
    {
        var marks = new List<DynamicSyntax>();
        foreach (var n in node.DescendantNodes<SyntaxNode>())
            if (n is DynamicSyntax d)
                marks.Add(d);
            else if (n is VariableReferenceSyntax r && depth < 8 && phrases.TryGetValue(r.Name.Text, out var phrase))
                marks.AddRange(DynamicsPlayed(phrase, phrases, depth + 1));
        return marks;
    }

    /// <summary>The sections as the form plays them; with no form, as written.</summary>
    private static List<string> PlayedSections(SyntaxNode root)
    {
        var sections = root.DescendantNodes<SectionDeclarationSyntax>().Where(s => s.Parent is { Parent: null })
            .Select(s => s.SectionName).ToList();
        var form = root.DescendantNodes<FormDeclarationSyntax>().Where(f => !f.InScore).OrderBy(f => f.Name != null).FirstOrDefault();
        return form?.DescendantNodes<SectionReferenceSyntax>().Select(r => r.SectionName).Where(sections.Contains).ToList() ?? sections;
    }

    /// <summary>
    /// A part whose levels begin after its music does: the first section it plays in, as the form
    /// plays them, writes none (owner's trial 2026-10-30: a rock trio's guitar and bass wrote their
    /// first level in the last bar of the chorus, twenty bars in, and the drums none). Drums need
    /// none, as for a part with no level at all — said apart — nor does a piano's hand, its levels
    /// written between the staves, in the other hand's part.
    /// </summary>
    private static IEnumerable<string> LateLevels(SyntaxNode root, HashSet<string> drums)
    {
        var phrases = root.DescendantNodes<PhraseDeclarationSyntax>()
            .Where(p => p.DescendantNodes<DynamicSyntax>().Any(d => Levels.Contains(d.DynamicToken.Text)))
            .Select(p => p.Name.Text).ToHashSet(System.StringComparer.Ordinal);
        bool HasLevel(PartBlockSyntax cell) => cell.DescendantNodes<DynamicSyntax>().Any(d => Levels.Contains(d.DynamicToken.Text))
            || (phrases.Count > 0 && cell.DescendantNodes<SyntaxTokenNode>().Any(t => phrases.Contains(t.Text)));
        var sections = root.DescendantNodes<SectionDeclarationSyntax>().Where(s => s.Parent is { Parent: null })
            .GroupBy(s => s.SectionName).ToDictionary(g => g.Key, g => g.First());
        var played = PlayedSections(root);
        var late = new List<(string Part, string? From)>();
        foreach (var part in root.DescendantNodes<PartBlockSyntax>().Select(b => b.PartName.Text).Distinct().Where(p => !drums.Contains(p)))
        {
            var cells = played.Select(s => sections[s].DescendantNodes<PartBlockSyntax>().Where(b => b.PartName.Text == part).ToList())
                .Select((c, i) => (Section: played[i], Cells: c)).Where(c => c.Cells.Count > 0).ToList();
            if (cells.Count == 0 || cells[0].Cells.Any(HasLevel))
                continue;
            var from = cells.FirstOrDefault(c => c.Cells.Any(HasLevel));
            if (from.Section != null)
                late.Add((part, from.Section));
        }
        return late.GroupBy(l => l.From).Select(g => $"{string.Join(", ", g.Select(l => l.Part))}: no level until {g.Key} — the "
            + "section a part begins in says none: write one at each part's first note (`@p`, `@mf`, `@f`, …), the level the piece opens at.");
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

    // A band's tune is not let off so (owner's trial 2026-10-30: told of its eighths, a rock guitar
    // kept 95% of them "for the drive", sixty bars without a rest or a long note).
    private const string BandTuneSings = " — a band's drive is its drums' and bass's eighths under the tune: the tune over "
        + "them sings in longer notes, held over the beat and breathing between phrases, a hook in a rhythm of its own; "
        + "only a solo may run on in eighths.";

    // A melody whose bars mostly share one rhythm: half of them or more, over 8 bars at least (a
    // theme's rhythm comes back often, but not in every other bar).
    private const double SameRhythmShare = 0.5;
    private const int SameRhythmFewestBars = 8;

    // A section whose bars mostly share one rhythm: three quarters of them or more, over 4 bars at
    // least — a higher bar than the piece's half, since a section is a theme's own ground and
    // repeats its rhythm by design; what it should not do is repeat it in every bar.
    private const double SectionRhythmShare = 0.75;
    private const int SectionRhythmFewestBars = 4;

    /// <summary>The sections (as the form first plays them) in which <paramref name="onsets"/>'s bars
    /// mostly share one rhythm: each section's name, the bars alike and the bars it sounds in.</summary>
    private static List<(string Name, int Same, int Bars)> SameRhythmSections(
        IReadOnlyList<(int Start, int Length)> onsets, MidiFile midi)
    {
        var poured = new List<(string, int, int)>();
        foreach (var (name, start, end) in PlayedSpans(midi))
        {
            var mine = onsets.Where(o => o.Start >= start && o.Start < end).ToList();
            var (same, bars) = SameRhythmBars(mine, midi.BarTicks.Where(b => b >= start && b < end));
            if (bars >= SectionRhythmFewestBars && same >= bars * SectionRhythmShare)
                poured.Add((name, same, bars));
        }
        return poured;
    }

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
        // Bars of single notes alone, one or two of them: a tune in long notes when they run on.
        var held = lineParts.ToDictionary(p => p, _ => new HashSet<int>(), System.StringComparer.Ordinal);
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
                else if (g.All(o => o.Single))
                    held[g.Key].Add(b);
        }
        // A tune that sings in half and whole notes — as the rhythm finding asks of one over a band's
        // eighths — has bars of one or two notes (owner's review 2026-10-30: a rock trio's tune in
        // halves and quarters was counted in 23 of its 32 bars). Such a bar is the tune's when it
        // runs on from, or into, another bar of the tune or of single notes.
        foreach (var (part, bars) in held)
            foreach (int b in bars)
                if (bars.Contains(b - 1) || bars.Contains(b + 1) || lines[part].Contains(b - 1) || lines[part].Contains(b + 1))
                    lines[part].Add(b);
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

    /// <summary>A pitch class named as the key spells it (<see cref="PartRanges.SpelledFromPitch(int, int)"/>).</summary>
    private static string PitchClassName(int pitch, int fifths) => PartRanges.SpelledFromPitch((pitch % 12 + 12) % 12 + 60, fifths)[..^1];

    /// <summary>A key's tonic as written (<c>ees</c>, <c>fis</c>, <c>as</c>) named as a musician names it: E♭, F♯, A♭.</summary>
    private static string TonicName(string tonic)
        => tonic.Length == 0 ? tonic : char.ToUpperInvariant(tonic[0]) + tonic[1..] switch
        {
            "is" => "♯", "isis" => "𝄪", "es" or "s" => "♭", "eses" or "ses" => "𝄫", _ => "",
        };

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

    /// <summary>
    /// Notes of the key written under another name (owner's trial 2026-10-30: a bass in E♭ minor
    /// wrote <c>b</c> and <c>fis</c> for the key's C♭ and G♭, its staff full of naturals and sharps).
    /// A note the key does not hold — a raised leading tone, a chromatic passing note — is not one.
    /// Each section's notes are read in its own key, a top-level phrase's in the file's; a part that
    /// reads a transposed staff only when the file writes <c>pitch concert</c>.
    /// </summary>
    private static IEnumerable<string> SpelledAgainstTheKey(SyntaxNode root, IReadOnlyList<PartDeclarationSyntax> parts, bool concert)
    {
        static (string Tonic, string Mode)? Of(KeySignatureSyntax? k)
            => k is null || k.IsCustom ? null : (k.Pitch.ToFullString().Trim().ToLowerInvariant(), k.Mode.Text.ToLowerInvariant());
        static bool InSection(SyntaxNode n)
        {
            for (var p = n.Parent; p != null; p = p.Parent)
                if (p is SectionDeclarationSyntax)
                    return true;
            return false;
        }
        var fileKey = Of(root.DescendantNodes<KeySignatureSyntax>().FirstOrDefault(k => !InSection(k)));
        var read = parts.Where(p => !RoleOf(p).Drums && (concert || Svg.Model.InstrumentDefaults.GetTransposition(InstrumentOf(p)) % 12 == 0))
            .Select(p => p.Name.Text).ToHashSet(System.StringComparer.Ordinal);
        // The music read, each with its key: a section's part blocks, and a top-level phrase played by those parts alone.
        var blocks = new List<(string Part, SyntaxNode Music, (string, string)? Key)>();
        foreach (var section in root.DescendantNodes<SectionDeclarationSyntax>().Where(s => s.Parent is { Parent: null }))
        {
            var key = Of(section.DescendantNodes<KeySignatureSyntax>().LastOrDefault()) ?? fileKey;
            foreach (var block in section.DescendantNodes<PartBlockSyntax>().Where(b => read.Contains(b.PartName.Text)))
                blocks.Add((block.PartName.Text, block, key));
        }
        var cells = root.DescendantNodes<PartBlockSyntax>().ToList();
        foreach (var phrase in root.DescendantNodes<PhraseDeclarationSyntax>().Where(p => p.Parent is CompilationUnitSyntax))
        {
            var players = cells.Where(c => c.DescendantNodes<VariableReferenceSyntax>().Any(r => r.Name.Text == phrase.Name.Text))
                .Select(c => c.PartName.Text).Distinct().ToList();
            if (players.Count == 1 && read.Contains(players[0]))
                blocks.Add((players[0], phrase, fileKey));
        }
        int[] major = [0, 2, 4, 5, 7, 9, 11], minor = [0, 2, 3, 5, 7, 8, 10];
        const string letters = "cdefgab";
        int[] natural = [0, 2, 4, 5, 7, 9, 11];
        var wrong = new Dictionary<string, Dictionary<(string Written, string Key), int>>(System.StringComparer.Ordinal);
        var outside = new Dictionary<string, Dictionary<(string Written, string Natural), int>>(System.StringComparer.Ordinal);
        string keyName = "";
        foreach (var (part, music, key) in blocks)
        {
            if (key is not var (tonic, mode) || mode is not ("major" or "minor") || tonic.Length == 0 || !letters.Contains(tonic[0]))
                continue;
            int tonicPc = PitchClass(tonic), tonicLetter = letters.IndexOf(tonic[0]);
            var steps = mode == "major" ? major : minor;
            // The key's own name for each of its pitch classes.
            var spelled = new Dictionary<int, string>();
            for (int i = 0; i < 7; i++)
            {
                int letter = (tonicLetter + i) % 7, pc = (tonicPc + steps[i]) % 12;
                int alter = ((pc - natural[letter]) % 12 + 18) % 12 - 6;
                spelled[pc] = letters[letter] + alter switch { -2 => "eses", -1 => "es", 1 => "is", 2 => "isis", _ => "" };
            }
            foreach (var pitch in music.DescendantNodes<PitchSyntax>().Where(p => p.Parent is NoteSyntax or ChordSyntax or ArpeggioSyntax))
            {
                if (pitch.GetChild(0) is not SyntaxTokenNode token || token.Text.Length == 0)
                    continue;
                string written = token.Text.ToLowerInvariant();
                int pc = PitchClass(written);
                if (!spelled.TryGetValue(pc, out var own))
                {
                    // A note out of the key that is a plain natural, written with an accidental the
                    // key does not use — the leading tone B as `ces` in C minor (owner's review
                    // 2026-10-30: the chat's AI thought the key turned `b` into B-flat, and wrote every
                    // B natural as C-flat). A sharp in a key of sharps is left alone: E-sharp in
                    // F-sharp minor's dominant is the right name.
                    bool flat = written.EndsWith("es", System.StringComparison.Ordinal) || written is "as";
                    bool keyFlats = spelled.Values.Any(v => v.EndsWith("es", System.StringComparison.Ordinal) && v[0] == written[0]);
                    bool keySharps = spelled.Values.Any(v => v.EndsWith("is", System.StringComparison.Ordinal));
                    int at = System.Array.IndexOf(natural, pc);
                    if (written.Length == 1 || at < 0 || (flat ? keyFlats : keySharps))
                        continue;
                    if (!outside.TryGetValue(part, out var odd))
                        outside[part] = odd = [];
                    odd[(written, letters[at].ToString())] = odd.GetValueOrDefault((written, letters[at].ToString())) + 1;
                    keyName = $"{TonicName(tonic)} {mode}";
                    continue;
                }
                if (own[0] == written[0])
                    continue;
                if (!wrong.TryGetValue(part, out var counts))
                    wrong[part] = counts = [];
                counts[(written, own)] = counts.GetValueOrDefault((written, own)) + 1;
                keyName = $"{TonicName(tonic)} {mode}";
            }
        }
        foreach (var (part, counts) in wrong)
        {
            if (counts.Values.Sum() < MisspelledFewest)
                continue;
            var most = counts.OrderByDescending(c => c.Value).Take(3).Select(c => $"`{c.Key.Written}` for its `{c.Key.Key}` ({c.Value} times)");
            yield return $"{part}: notes of the key written under another name, in {keyName} — {string.Join(", ", most)}: write a note of "
                + "the key by the key's own name; a sharp or a natural in a flat key (a flat in a sharp one) reads as a note out of the key.";
        }
        foreach (var (part, counts) in outside)
        {
            if (counts.Values.Sum() < OddlySpelledFewest)
                continue;
            var ordered = counts.OrderByDescending(c => c.Value).ToList();
            var most = ordered.Take(3).Select(c => $"`{c.Key.Written}` for `{c.Key.Natural}` ({c.Value} times)");
            var (w, n) = ordered[0].Key;
            yield return $"{part}: naturals out of the key written with an accidental it does not use, in {keyName} — {string.Join(", ", most)}: "
                + $"a note's name is its pitch and the key never changes it — {char.ToUpperInvariant(n[0])} natural is `{n}` in any key, drawn "
                + $"with its natural sign where the key alters it; `{w}` prints a {TonicName(w)}. Write each by its natural name.";
        }
    }

    // Notes of the key misspelled in a part this many times: a habit, not a slip.
    private const int MisspelledFewest = 4;

    // A drum part's same bar played this many times running, no fill between: its phrases unmarked.
    private const int SameDrumBarsFewest = 16;

    // Naturals out of the key written with a foreign accidental this many times (a leading tone comes
    // seldom, and one `ces` for B in C minor is already a misreading).
    private const int OddlySpelledFewest = 2;

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
