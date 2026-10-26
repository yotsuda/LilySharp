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

using LilySharp.Core.Midi;
using LilySharp.Core.Music;

namespace LilySharp.Core.Harmony;

/// <summary>A note as it sounds: when (ticks) and how high (MIDI pitch).</summary>
public readonly record struct SoundingNote(int StartTick, int DurationTicks, int Pitch);

/// <summary>
/// One chord read from the music: the ticks it lasts, its root and quality, the lowest note's
/// pitch class (the bass — a slash chord when it is not the root), how well the notes fit it (the
/// share of their weight on its tones), and whether one note at a time sounded there (a melody
/// alone: a reading, not a hearing). <see cref="Root"/> is -1 where nothing sounds.
/// </summary>
public sealed record ChordReading(int StartTick, int EndTick, int Root, ChordQuality Quality, int Bass,
    double Fit, bool Monophonic)
{
    /// <summary>The symbol as a chords row writes it (<c>Am7</c>, <c>G7/B</c>, <c>Bbmaj7</c>),
    /// the roots spelled for a key of <paramref name="keySharps"/> sharps (negative: flats).</summary>
    public string Symbol(int keySharps)
    {
        if (Root < 0)
            return "r";
        string s = HarmonyReader.PitchName(Root, keySharps) + HarmonyReader.SuffixOf(Quality);
        return Bass >= 0 && Bass != Root ? s + "/" + HarmonyReader.PitchName(Bass, keySharps) : s;
    }
}

/// <summary>
/// How rich the chords chosen for a melody are (owner's request 2026-10-26: "how big the
/// variant is"): <see cref="Plain"/> triads only; <see cref="Standard"/> a seventh where it is
/// played, and a major chord resolving a fifth down written as its dominant seventh (G → C as
/// G7 → C, A → Dm as A7 → Dm); <see cref="Rich"/> also a minor chord moving a fifth down as a
/// minor seventh (Dm → G7 as Dm7 → G7: the ii–V). Chords heard in the parts are read as played.
/// </summary>
public enum ChordRichness { Plain, Standard, Rich }

/// <summary>
/// The chords a melody alone is harmonized from — where one note sounds at a time there is no
/// chord to hear, so one is chosen, one a bar at most: the key's triads and seventh chords, the
/// common ones preferred, and any other major or minor triad or dominant seventh where the notes
/// spell it. A seventh chord is chosen only where its seventh sounds (owner's report 2026-10-26:
/// the V was always a V7 and no other seventh was written — a ii with its seventh played was a
/// plain ii).
/// <see cref="Everywhere"/> harmonizes every stretch so, the parts' chords unread (the
/// "from the melody only" choice).
/// </summary>
public sealed record MelodyChords(IReadOnlyList<(int Root, ChordQuality Quality, double Preference)> Chords,
    bool Everywhere = false)
{
    /// <summary>The key's tonic (a pitch class), which starts and ends a piece more often than
    /// not; -1 when unknown.</summary>
    public int Tonic { get; init; } = -1;

    /// <summary>How rich the chords chosen are (<see cref="ChordRichness"/>).</summary>
    public ChordRichness Richness { get; init; } = ChordRichness.Standard;

    /// <summary>The preference of a chord outside the key: behind the key's by 0.15 of the weight
    /// (the preferences weigh 0.05 each), so it is taken only where the notes spell it.</summary>
    private const double OutOfKey = -3;

    /// <summary>The key's seven triads and seven seventh chords, for a tonic letter and its
    /// signature's sharps, then every other major and minor triad and dominant seventh behind them.</summary>
    public static MelodyChords OfKey(char tonic, int sharps, bool everywhere = false,
        ChordRichness richness = ChordRichness.Standard)
    {
        var chords = new List<(int, ChordQuality, double)>();
        var key = DiatonicChords.ForKey(tonic, sharps);
        foreach (var ch in key)
        {
            // I, then IV and V, then vi, ii, iii, vii.
            double preference = ch.Degree switch { 0 => 6, 3 => 5, 4 => 5, 5 => 4, 1 => 3, 2 => 2, _ => 1 } / 6.0;
            ChordQuality? triad = ch.Quality switch
            {
                "" => ChordQuality.Major,
                "m" => ChordQuality.Minor,
                "dim" => ChordQuality.Diminished,
                "aug" => ChordQuality.Augmented,
                _ => null,
            };
            ChordQuality? seventh = ch.SeventhQuality switch
            {
                "maj7" => ChordQuality.Major7,
                "7" => ChordQuality.Dominant7,
                "m7" => ChordQuality.Minor7,
                "m7-5" => ChordQuality.HalfDiminished7,
                "dim7" => ChordQuality.Diminished7,
                _ => null,
            };
            if (triad is { } t)
                chords.Add((ch.PitchClasses[0], t, preference));
            if (seventh is { } s && richness != ChordRichness.Plain)
                chords.Add((ch.PitchClasses[0], s, preference));
        }
        // A line that spells a chord outside the key gets it (owner's report 2026-10-26: a bass
        // walking B♭ D F under C major was given Dm, A C♯ E was given Am) — every major and minor
        // triad and dominant seventh (a secondary dominant), the key's chords ahead of them when
        // the notes fit both alike.
        var inKey = chords.Select(c => (c.Item1, c.Item2)).ToHashSet();
        var outside = richness == ChordRichness.Plain
            ? new[] { ChordQuality.Major, ChordQuality.Minor }
            : new[] { ChordQuality.Major, ChordQuality.Minor, ChordQuality.Dominant7 };
        foreach (var quality in outside)
            for (int root = 0; root < 12; root++)
                if (!inKey.Contains((root, quality)))
                    chords.Add((root, quality, OutOfKey));
        return new MelodyChords(chords, everywhere) { Tonic = key[0].PitchClasses[0], Richness = richness };
    }
}

/// <summary>
/// Reads the chords the notes sound (owner's decisions 2026-10-26, docs/harmony-analysis-design.md):
/// beat by beat, each pitch class weighed by how long it sounds and where it starts (a note struck
/// on the beat counts fully, one struck inside it — a passing or neighbour tone — less), then the
/// quality and root that fit the pooled weight best over stretches of beats, the stretches chosen
/// so that the fit gained by a change outweighs a cost per change. Chords may change on any beat.
/// </summary>
public static class HarmonyReader
{
    private const int DrumChannel = 9;
    private const string ChordRowSuffix = " (chords)";

    /// <summary>The qualities read, plainest first, and how much less likely each is than a triad
    /// when the notes fit both (a share of the weight).</summary>
    private static readonly (ChordQuality Quality, double Prior)[] Qualities =
    [
        (ChordQuality.Major, 0), (ChordQuality.Minor, 0),
        (ChordQuality.Dominant7, -0.12), (ChordQuality.Minor7, -0.12), (ChordQuality.Major7, -0.15),
        (ChordQuality.HalfDiminished7, -0.15), (ChordQuality.Diminished, -0.15),
        (ChordQuality.Diminished7, -0.2), (ChordQuality.Augmented, -0.2),
        (ChordQuality.Sus4, -0.2), (ChordQuality.Dominant7Sus4, -0.25),
    ];

    /// <summary>A line alone starting below this pitch (G3) is a bass line.</summary>
    private const int LowestMelody = 55;

    /// <summary>The longest stretch one chord is read over, in beats (two bars of 4/4).</summary>
    private const int MaxBeats = 8;

    /// <summary>
    /// The pitched notes of <paramref name="midi"/>: the drums left out, and either the chords
    /// rows alone (<paramref name="chordRows"/> true — the chords a file writes, sounded) or every
    /// track but them (false — what the parts play).
    /// </summary>
    public static IReadOnlyList<SoundingNote> NotesOf(MidiFile midi, bool chordRows)
    {
        var notes = new List<SoundingNote>();
        foreach (var track in midi.Tracks)
            foreach (var n in track.Notes)
            {
                if (n.Channel == DrumChannel)
                    continue;
                bool row = (n.Part ?? track.Name).EndsWith(ChordRowSuffix, StringComparison.Ordinal);
                if (row == chordRows)
                    notes.Add(new SoundingNote(n.StartTick, Math.Max(1, n.DurationTicks), n.Pitch));
            }
        return notes;
    }

    /// <summary>
    /// The chords <paramref name="notes"/> sound, over <paramref name="beats"/> (ticks, each
    /// beat to the next, the last to <paramref name="endTick"/>), adjacent readings of one chord
    /// joined. <paramref name="changeCost"/> is what a change must gain, as a share of a beat's
    /// usual weight: higher reads fewer, longer chords. With <paramref name="melody"/>, a stretch
    /// where one note sounds at a time (or every stretch, <see cref="MelodyChords.Everywhere"/>)
    /// takes one of its chords, starting at a bar line.
    /// </summary>
    public static IReadOnlyList<ChordReading> Read(IReadOnlyList<SoundingNote> notes,
        IReadOnlyList<(int Tick, bool Accent)> beats, int endTick, double changeCost = 0.6,
        MelodyChords? melody = null)
    {
        int count = beats.Count;
        if (count == 0)
            return [];
        var spans = new (int Start, int End)[count];
        for (int i = 0; i < count; i++)
            spans[i] = (beats[i].Tick, i + 1 < count ? beats[i + 1].Tick : Math.Max(beats[i].Tick + 1, endTick));

        // Each beat's weight per pitch class, its lowest note, and whether it ever sounds two at once.
        var weight = new double[count, 12];
        var total = new double[count];
        var lowest = new int[count];
        var mono = new bool[count];
        var sorted = notes.OrderBy(n => n.StartTick).ToList();
        var approaches = Approaches(sorted, beats);
        for (int i = 0; i < count; i++)
        {
            var (s, e) = spans[i];
            double length = e - s;
            int low = int.MaxValue, lowAtStart = int.MaxValue;
            var sounding = new List<SoundingNote>();
            foreach (var n in sorted)
            {
                if (n.StartTick >= e)
                    break;
                int ne = n.StartTick + n.DurationTicks;
                if (ne <= s)
                    continue;
                double overlap = Math.Min(ne, e) - Math.Max(n.StartTick, s);
                if (overlap <= 0)
                    continue;
                // Struck inside the beat: a passing or neighbour tone, more often than not; so is one
                // that steps into the next bar line.
                double strike = n.StartTick > s || approaches.Contains(n) ? 0.6 : 1.0;
                double w = overlap / length * strike * (beats[i].Accent ? 1.2 : 1.0);
                weight[i, ((n.Pitch % 12) + 12) % 12] += w;
                total[i] += w;
                sounding.Add(n);
                low = Math.Min(low, n.Pitch);
                if (n.StartTick <= s)
                    lowAtStart = Math.Min(lowAtStart, n.Pitch);
            }
            lowest[i] = lowAtStart != int.MaxValue ? lowAtStart : low;
            mono[i] = !Overlapping(sounding);
        }

        // What a change costs: a share of the median weight of a beat that sounds — once at a
        // bar line, half again at the middle of a bar, more on any other beat: chords change
        // with the bar more often than not, and a walking bass is not a chord a note.
        var weights = total.Where(t => t > 0).OrderBy(t => t).ToList();
        double typical = weights.Count == 0 ? 1 : weights[weights.Count / 2];
        double cost = changeCost * typical;
        var place = new double[count];
        for (int i = 0, bar = 0; i < count; i++)
        {
            if (beats[i].Accent)
                bar = i;
            int next = i + 1;
            while (next < count && !beats[next].Accent)
                next++;
            int length = next - bar, k = i - bar;
            place[i] = k == 0 ? 1.0 : length % 2 == 0 && k == length / 2 ? 1.5 : 2.5;
        }

        // A melody's chord lasts a bar at least, so a stretch reaches over the longest bar.
        int maxBeats = MaxBeats;
        for (int i = 0, bar = 0; i <= count; i++)
            if (i == count || beats[i].Accent)
            {
                maxBeats = Math.Max(maxBeats, i - bar);
                bar = i;
            }

        // How well each beat fits each chord, on its own: a stretch's fit is its beats' summed,
        // so a chord must fit the beats one by one — the C and the G of a bar pooled are not a
        // Cmaj7, since neither beat sounds one.
        var fits = new double[count, Candidates.Length];
        var matches = new double[count, Candidates.Length];
        for (int i = 0; i < count; i++)
            for (int c = 0; c < Candidates.Length; c++)
                (fits[i, c], matches[i, c]) = BeatFit(weight, i, total[i], Candidates[c]);

        // states[j]: the best readings of beats [0, j), one per chord the last stretch ends on (the
        // few best kept) — so the move from each chord to the next is scored with the fit, and a
        // bar the notes leave open takes the chord its neighbours lead to (owner's request
        // 2026-10-26: "the progression around a bar, not its notes alone").
        var states = new List<State>[count + 1];
        states[0] = [new State(0, NoChord, ChordQuality.Major, 1, -1, -1, false)];
        int tonic = melody?.Tonic ?? -1;
        var fitted = new double[Candidates.Length];
        var matched = new double[Candidates.Length];
        for (int j = 1; j <= count; j++)
        {
            var reached = new Dictionary<(int, ChordQuality), State>();
            var pooled = new double[12];
            double sum = 0;
            bool alone = true;
            Array.Clear(fitted);
            Array.Clear(matched);
            for (int i = j - 1; i >= 0 && j - i <= maxBeats; i--)
            {
                for (int pc = 0; pc < 12; pc++)
                    pooled[pc] += weight[i, pc];
                for (int c = 0; c < Candidates.Length; c++)
                {
                    fitted[c] += fits[i, c];
                    matched[c] += matches[i, c];
                }
                sum += total[i];
                alone &= mono[i];
                bool lone = melody != null && (melody.Everywhere || alone);
                if (lone && i > 0 && !beats[i].Accent)
                    continue;   // a melody's chord changes at a bar line
                if (states[i].Count == 0)
                    continue;
                var options = lone
                    ? MelodyOptions(pooled, sum, melody!, lowest[i])
                    : Chords(fitted, matched, sum, lowest[i]);
                foreach (var (root, quality, score, fit) in options)
                    for (int s = 0; s < states[i].Count; s++)
                    {
                        var prev = states[i][s];
                        // A melody's chords are chosen, and the progression chooses among them;
                        // chords heard are read, the progression only tipping a close call.
                        double value = prev.Value + score - cost * place[i]
                            + typical * (lone ? 1.0 : 0.3) * Progression(prev.Root, prev.Quality, root, quality, tonic, start: i == 0, end: j == count);
                        if (!reached.TryGetValue((root, quality), out var had) || value > had.Value)
                            reached[(root, quality)] = new State(value, root, quality, fit, i, s, lone);
                    }
            }
            states[j] = [.. reached.Values.OrderByDescending(v => v.Value).Take(KeptStates)];
        }

        var stretches = new List<(int I, int J, int Root, ChordQuality Quality, double Fit)>();
        if (states[count].Count > 0)
        {
            int at = 0;
            for (int j = count; j > 0;)
            {
                var st = states[j][at];
                stretches.Add((st.From, j, st.Root, st.Quality, st.Fit));
                (j, at) = (st.From, st.FromState);
            }
        }
        stretches.Reverse();

        // A rest the chord holds through is part of it; a whole bar where nothing sounds is no
        // chord (a chords row's N.C.), cut out of the stretch it fell in.
        bool WholeBarSilent(int k, out int end)
        {
            end = k;
            if (!beats[k].Accent || total[k] > 0)
                return false;
            int e = k + 1;
            while (e < count && !beats[e].Accent)
                e++;
            for (int m = k; m < e; m++)
                if (total[m] > 0)
                    return false;
            end = e;
            return true;
        }
        var readings = new List<ChordReading>();
        void Add(int i, int j, int root, ChordQuality quality, double fit)
        {
            if (i >= j)
                return;
            int bass = root < 0 ? -1 : ((lowest[i] % 12) + 12) % 12;
            bool alone = Enumerable.Range(i, j - i).All(k => mono[k]);
            // A melody's chord is chosen, not heard: no bass sounds it.
            if (root >= 0 && (lowest[i] == int.MaxValue || melody != null && (melody.Everywhere || alone)))
                bass = root;
            readings.Add(new ChordReading(spans[i].Start, spans[j - 1].End, root, quality, bass, fit, alone));
        }
        foreach (var (i, j, root, quality, fit) in stretches)
        {
            int at = i;
            for (int k = i; k < j; k++)
            {
                if (!WholeBarSilent(k, out int end))
                    continue;
                end = Math.Min(end, j);
                Add(at, k, root, quality, fit);
                Add(k, end, -1, ChordQuality.Major, 1);
                at = end;
                k = end - 1;
            }
            Add(at, j, root, quality, fit);
        }

        // One chord read over several stretches is one reading (its bass the first's).
        var joined = new List<ChordReading>();
        foreach (var r in readings)
        {
            if (joined.Count > 0 && joined[^1].Root == r.Root && joined[^1].Quality == r.Quality)
            {
                var last = joined[^1];
                joined[^1] = last with { EndTick = r.EndTick, Fit = Math.Min(last.Fit, r.Fit), Monophonic = last.Monophonic && r.Monophonic };
            }
            else
                joined.Add(r);
        }
        return melody == null ? joined : Coloured(joined, melody);
    }

    /// <summary>
    /// The chosen chords (a melody's) written as rich as <see cref="MelodyChords.Richness"/> asks,
    /// now that the chord after each is known: a dominant (the key's V, or a major chord outside
    /// the key) whose root falls a fifth to the next a dominant seventh (it resolves — its seventh
    /// is the tone that leads, played or not), and,
    /// rich, a minor one so a minor seventh; plain, every seventh a triad. A chord heard in the
    /// parts is left as it is played.
    /// </summary>
    private static List<ChordReading> Coloured(List<ChordReading> readings, MelodyChords melody)
    {
        var coloured = new List<ChordReading>(readings.Count);
        for (int k = 0; k < readings.Count; k++)
        {
            var r = readings[k];
            if (r.Root >= 0 && (melody.Everywhere || r.Monophonic))
            {
                // After the last chord read (a section read alone) the music goes on: home, more
                // often than not — a V ending a section is the half cadence back to I.
                int nextRoot = k + 1 < readings.Count ? readings[k + 1].Root : melody.Tonic;
                bool resolves = nextRoot >= 0 && ((nextRoot - r.Root) % 12 + 12) % 12 == 5;
                // A dominant: the key's V, or a major chord the key does not hold (A → Dm in C, a
                // secondary dominant). The I and IV falling a fifth (C → F, F → B♭) stay triads.
                bool dominant = melody.Tonic < 0 || r.Root == (melody.Tonic + 7) % 12
                    || !melody.Chords.Any(c => c.Root == r.Root && c.Quality == ChordQuality.Major && c.Preference >= 0);
                var quality = (melody.Richness, r.Quality) switch
                {
                    (ChordRichness.Plain, ChordQuality.Dominant7) => ChordQuality.Major,
                    (ChordRichness.Plain, ChordQuality.Major7) => ChordQuality.Major,
                    (ChordRichness.Plain, ChordQuality.Minor7) => ChordQuality.Minor,
                    (ChordRichness.Plain, ChordQuality.HalfDiminished7) => ChordQuality.Diminished,
                    (not ChordRichness.Plain, ChordQuality.Major) when resolves && dominant => ChordQuality.Dominant7,
                    (ChordRichness.Rich, ChordQuality.Minor) when resolves => ChordQuality.Minor7,
                    _ => r.Quality,
                };
                r = r with { Quality = quality };
            }
            coloured.Add(r);
        }
        return coloured;
    }

    /// <summary>A chord read: its root, quality, prior and tones (semitones above the root, and
    /// whether each is a fifth — a fifth left out is common).</summary>
    private readonly record struct Candidate(int Root, ChordQuality Quality, double Prior, (int Semitone, bool Fifth)[] Tones);

    private static readonly Candidate[] Candidates =
    [
        .. Qualities.SelectMany(q => Enumerable.Range(0, 12).Select(root => new Candidate(root, q.Quality, q.Prior,
            [.. ChordQualityRegistry.GetTones(q.Quality).Select(t => (t.Semitone, t.DiatonicStep == 4 && t.Semitone == 7))]))),
    ];

    /// <summary>One beat's fit to a chord (in weight) and the weight on its tones: the weight on
    /// them, less the weight off them and a share of the beat for each tone that does not sound
    /// (a missing fifth a third as much).</summary>
    private static (double Fit, double Matched) BeatFit(double[,] weight, int beat, double total, Candidate chord)
    {
        if (total <= 0)
            return (0, 0);
        double matched = 0, missing = 0;
        foreach (var (semitone, fifth) in chord.Tones)
        {
            double w = weight[beat, (chord.Root + semitone) % 12];
            matched += w;
            if (w < 0.05 * total)
                missing += fifth ? 0.3 : 1.0;
        }
        return (matched - 0.7 * (total - matched) - 0.25 * total * missing / chord.Tones.Length, matched);
    }

    /// <summary>A reading of the beats up to a point: its value, the chord its last stretch is
    /// read as, where that stretch starts and the reading before it.</summary>
    private readonly record struct State(double Value, int Root, ChordQuality Quality, double Fit,
        int From, int FromState, bool Lone);

    /// <summary>The root of the reading before the first chord.</summary>
    private const int NoChord = -2;

    /// <summary>The readings kept at each point, and the chords offered for each stretch.</summary>
    private const int KeptStates = 8, Offered = 4;

    /// <summary>
    /// How well a chord follows another, in a beat's usual weight: the root falling a fifth best
    /// (V→I, ii→V, vi→ii — the more from a major chord, a dominant resolving), a step up or a third
    /// next, a fifth up a little; a semitone or a tritone behind. The key's tonic, when it is known,
    /// starts and ends a piece more often than not.
    /// </summary>
    private static double Progression(int prevRoot, ChordQuality prevQuality, int root, ChordQuality quality,
        int tonic, bool start, bool end)
    {
        if (root < 0)
            return 0;
        double value = end && root == tonic ? 0.3 : 0;
        if (prevRoot == NoChord)
            return value + (start && root == tonic ? 0.2 : 0);
        if (prevRoot < 0)
            return value;
        bool prevMajor = prevQuality is ChordQuality.Major or ChordQuality.Dominant7;
        int motion = ((root - prevRoot) % 12 + 12) % 12;
        return value + motion switch
        {
            5 => prevMajor ? 0.6 : 0.4,
            2 => 0.15,
            3 or 4 or 8 or 9 => 0.1,
            7 => 0.05,
            10 => 0.05,
            1 or 11 => -0.1,
            6 => -0.3,
            _ => 0,
        };
    }

    /// <summary>The chords that fit a stretch best, from its beats' summed fits: each with its root
    /// (-1 where nothing sounds), quality, score (in weight) and fit (the share of the weight on
    /// its tones). The lowest note at the stretch's start is the root more often than not.</summary>
    private static List<(int Root, ChordQuality Quality, double Score, double Fit)> Chords(
        double[] fitted, double[] matched, double sum, int lowest)
    {
        if (sum <= 0)
            return [(-1, ChordQuality.Major, 0, 1)];
        int bassPc = lowest == int.MaxValue ? -1 : ((lowest % 12) + 12) % 12;
        var options = new List<(int, ChordQuality, double, double)>(Candidates.Length);
        for (int c = 0; c < Candidates.Length; c++)
        {
            var chord = Candidates[c];
            double score = fitted[c] + chord.Prior * sum + (bassPc == chord.Root ? 0.35 * sum : 0);
            options.Add((chord.Root, chord.Quality, score, matched[c] / sum));
        }
        return [.. options.OrderByDescending(o => o.Item3).Take(Offered)];
    }

    /// <summary>The chord of <paramref name="melody"/> a melody's stretch fits best: the weight
    /// on its tones, less half the weight off them (a melody passes through tones no chord
    /// holds), the common chords ahead when two fit alike. No tone counts as missing — a
    /// melody does not sound its chord. A low line is a bass line, and a bass plays the root on
    /// the bar's first beat more often than not: under G3 there, that note is the root's
    /// (owner's report 2026-10-26: a bass's `c e g a | c e a g f e` read Am).</summary>
    private static List<(int Root, ChordQuality Quality, double Score, double Fit)> MelodyOptions(
        double[] pooled, double sum, MelodyChords melody, int lowest)
    {
        int bassPc = lowest < LowestMelody ? ((lowest % 12) + 12) % 12 : -1;
        if (sum <= 0)
            return [(-1, ChordQuality.Major, 0, 1)];
        var options = new List<(int, ChordQuality, double, double)>();
        foreach (var (root, quality, preference) in melody.Chords)
        {
            var tones = ChordQualityRegistry.GetTones(quality);
            double matched = 0;
            foreach (var tone in tones)
                matched += pooled[(root + tone.Semitone) % 12];
            // A seventh chord where its seventh sounds — a fifth of the stretch at least, more than
            // a passing note — with two more of its tones (E G alone are a C, not an Am7 whose A
            // never sounds), and behind the triad by a tenth of the weight, so that four tones over two bars
            // (E G | A) are not one seventh chord where a chord a bar fits as well.
            bool seventh = tones.Count > 3;
            if (seventh && (pooled[(root + tones[3].Semitone) % 12] < 0.2 * sum
                || tones.Count(t => pooled[(root + t.Semitone) % 12] >= 0.1 * sum) < 3))
                continue;
            // A bass's seventh chord stands on its root: C E G A over a C bass is a C, not Am7/C.
            if (seventh && bassPc >= 0 && root != bassPc)
                continue;
            double score = matched - 0.5 * (sum - matched) + 0.05 * preference * sum - (seventh ? 0.1 * sum : 0)
                + (root == bassPc ? 0.2 * sum : 0);
            options.Add((root, quality, score, matched / sum));
        }
        return [.. options.OrderByDescending(o => o.Item3).Take(Offered)];
    }

    /// <summary>The notes that step into a bar line — a beat long at most, ending on it, the next
    /// note starting there a tone or a semitone away: the approach of a walking bass or a melody
    /// (`c g a b | c`: the B is not the Cmaj7's seventh — owner's report 2026-10-26). At the end
    /// of what is read (a section read alone) the next note is not there: a last note reached by
    /// a step, ending on the last bar line, is taken to lead on.</summary>
    private static HashSet<SoundingNote> Approaches(List<SoundingNote> sorted, IReadOnlyList<(int Tick, bool Accent)> beats)
    {
        var bars = beats.Where(b => b.Accent).Select(b => b.Tick).ToHashSet();
        int beat = beats.Count > 1 ? beats[1].Tick - beats[0].Tick : int.MaxValue;
        var starts = sorted.ToLookup(n => n.StartTick);
        var ends = sorted.ToLookup(n => n.StartTick + n.DurationTicks);
        int last = sorted.Count == 0 ? 0 : sorted.Max(n => n.StartTick + n.DurationTicks);
        var approaches = new HashSet<SoundingNote>();
        foreach (var n in sorted)
        {
            int end = n.StartTick + n.DurationTicks;
            if (n.DurationTicks > beat)
                continue;
            if (bars.Contains(end) && starts[end].Any(m => Math.Abs(m.Pitch - n.Pitch) is 1 or 2))
                approaches.Add(n);
            else if (end == last && !starts[end].Any() && ends[n.StartTick].Any(m => Math.Abs(m.Pitch - n.Pitch) is 1 or 2))
                approaches.Add(n);
        }
        return approaches;
    }

    private static bool Overlapping(List<SoundingNote> notes)
    {
        // A legato line's notes overlap a little as they play (a slur's hold): two notes sound
        // together when they overlap by a quarter of the shorter one.
        for (int a = 0; a < notes.Count; a++)
            for (int b = a + 1; b < notes.Count; b++)
            {
                var (x, y) = (notes[a], notes[b]);
                int overlap = Math.Min(x.StartTick + x.DurationTicks, y.StartTick + y.DurationTicks)
                    - Math.Max(x.StartTick, y.StartTick);
                if (x.Pitch != y.Pitch && overlap * 4 > Math.Min(x.DurationTicks, y.DurationTicks))
                    return true;
            }
        return false;
    }

    private static readonly string[] Sharps = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    private static readonly string[] Flats = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"];
    private static readonly string[] Plain = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"];

    /// <summary>A pitch class as a chord symbol's root: sharps in a sharp key, flats in a flat
    /// one, the common spelling of each in C.</summary>
    internal static string PitchName(int pc, int keySharps)
        => (keySharps > 0 ? Sharps : keySharps < 0 ? Flats : Plain)[((pc % 12) + 12) % 12];

    /// <summary>The quality as a chords row spells it.</summary>
    internal static string SuffixOf(ChordQuality quality) => quality switch
    {
        ChordQuality.Major => "",
        ChordQuality.Minor => "m",
        ChordQuality.Diminished => "dim",
        ChordQuality.Augmented => "aug",
        ChordQuality.Dominant7 => "7",
        ChordQuality.Major7 => "maj7",
        ChordQuality.Minor7 => "m7",
        ChordQuality.HalfDiminished7 => "m7-5",
        ChordQuality.Diminished7 => "dim7",
        ChordQuality.Sus4 => "sus4",
        ChordQuality.Dominant7Sus4 => "7sus4",
        _ => "",
    };
}
