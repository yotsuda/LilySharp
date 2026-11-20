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

using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LilySharp.Core.Editing;
using LilySharp.Lsp.Protocol;
using StreamJsonRpc;
using LilySharp.Core.Syntax;
using LilySharp.Core.Semantics;
using LilySharp.Core.Svg;
using LilySharp.Core.Svg.Collector;
using LilySharp.Core.Svg.Model;
using LilySharp.Core.Music;
using LspRange = LilySharp.Lsp.Protocol.Range;
using LspDiagnosticSeverity = LilySharp.Lsp.Protocol.DiagnosticSeverity;
using CoreDiagnosticSeverity = LilySharp.Core.Syntax.DiagnosticSeverity;
using CoreDiagnostic = LilySharp.Core.Syntax.Diagnostic;

namespace LilySharp.Lsp;

public sealed partial class LilySharpLanguageServer
{
    /// <summary>
    /// Flattens the piece to note events in seconds for the preview's WebAudio
    /// player: the same MidiExporter model as .mid export, with the tempo map
    /// applied server-side so the webview only schedules oscillators.
    /// </summary>
    [JsonRpcMethod("lilysharp/playback", UseSingleObjectParameterDeserialization = true)]
    public Task<PlaybackResponse> GetPlaybackAsync(PlaybackParams @params, CancellationToken token)
        => OffDispatch(() => GetPlayback(@params), token);

    public PlaybackResponse GetPlayback(PlaybackParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new PlaybackResponse { Error = DocumentNotFound };
        var (tree, _) = ExpandUsings(doc, @params.TextDocument.Uri);
        return PlaybackOf(tree, @params.RenderName, @params.SoundingOnly);
    }

    /// <summary>
    /// The note events of an arbitrary source — an AI candidate not applied yet, or the text it
    /// would replace (owner's request 2026-10-25: hear a candidate before accepting it). The
    /// <c>using</c>s resolve beside <see cref="PlaybackTextParams.Uri"/>, the document's own
    /// file. Read-only; nothing is opened.
    /// </summary>
    [JsonRpcMethod("lilysharp/playbackText", UseSingleObjectParameterDeserialization = true)]
    public Task<PlaybackResponse> GetPlaybackTextAsync(PlaybackTextParams @params, CancellationToken token)
        => OffDispatch(() => GetPlaybackText(@params), token);

    public PlaybackResponse GetPlaybackText(PlaybackTextParams @params)
    {
        string text = @params.Text ?? "";
        string basePath = FilePathOf(@params.Uri);
        var tree = ExpandUsings(text, SyntaxTree.Parse(text), basePath,
            IncludeFiles.Read).Tree;
        return PlaybackOf(tree, @params.RenderName);
    }

    /// <summary>
    /// The words of the score the preview shows, on their notes in seconds — one voice per part a
    /// lyrics row sings — for the editor to have sung by a singing engine (docs/singing-design.md
    /// §4; <see cref="LilySharp.Core.Midi.SungVoices"/>). The same export and clock as
    /// <c>lilysharp/playback</c>, so the voice lands where the synth's notes do.
    /// </summary>
    [JsonRpcMethod("lilysharp/singerSlot", UseSingleObjectParameterDeserialization = true)]
    public Task<SingerSlotResponse> GetSingerSlotAsync(SingerSlotParams @params, CancellationToken token)
        => OffDispatch(() => GetSingerSlot(@params), token);

    /// <summary>The editor's question while a <c>singer "</c> is typed (session 1168): is the caret in
    /// one, and what language are the words it would sing? The engine's voices are the editor's to
    /// list; the words' language is read here.</summary>
    public SingerSlotResponse GetSingerSlot(SingerSlotParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null || SingerSlotAt(doc.Text, @params.Offset) is not { } slot)
            return new SingerSlotResponse();
        var (tree, _) = ExpandUsings(doc, @params.TextDocument.Uri);
        var root = tree.GetRoot();
        var languageOf = LilySharp.Core.Music.LyricLanguage.ReaderOf(root);
        string? track = slot.Part is { } part && LilySharp.Core.Music.LyricBindings.TracksOfPart(root, part) is [var own] ? own : slot.Track;
        // No words named yet: the file's language, or every voice where its lyrics differ.
        bool anyLanguage = track == null && slot.Language == null
            && root.DescendantNodes<LilySharp.Core.Syntax.LyricsBlockSyntax>().Select(b => languageOf(b.VoiceName)).Distinct(StringComparer.Ordinal).Count() > 1;
        return new SingerSlotResponse { InSlot = true, Language = LilySharp.Core.Music.LyricLanguage.CodeOf(slot.Language) ?? languageOf(track), AnyLanguage = anyLanguage, Gender = slot.Gender, Quoted = slot.Quoted };
    }

    [JsonRpcMethod("lilysharp/sung", UseSingleObjectParameterDeserialization = true)]
    public Task<SungResponse> GetSungAsync(SungParams @params, CancellationToken token)
        => OffDispatch(() => GetSung(@params), token);

    /// <summary>
    /// Every setting of how a voice sings that the document writes, with where it stands: the
    /// singer lines' <c>with …</c> and the notes' <c>@sing(…)</c>. What a voice TAKES is its
    /// engine's to say, so the editor asks the engine and marks what it does not take; this is
    /// only what is written.
    /// </summary>
    [JsonRpcMethod("lilysharp/singSettings", UseSingleObjectParameterDeserialization = true)]
    public Task<SingSettingsResponse> GetSingSettingsAsync(SingSettingsParams @params, CancellationToken token)
        => OffDispatch(() => GetSingSettings(@params), token);

    public SingSettingsResponse GetSingSettings(SingSettingsParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new SingSettingsResponse();
        var root = doc.Tree.GetRoot();
        string text = doc.Text;
        var settings = new List<SingSettingDto>();
        var singers = new List<string?>();
        // A number's span takes its sign with it.
        int Signed(int start) => start > 0 && text[start - 1] == '-' ? start - 1 : start;
        foreach (var line in root.DescendantNodes<LilySharp.Core.Syntax.SingerDeclarationSyntax>())
        {
            if (line.ClauseToken == null)
                continue;
            if (!singers.Contains(line.Singer))
                singers.Add(line.Singer);
            foreach (var s in line.Settings)
                settings.Add(new SingSettingDto
                {
                    Name = s.Name, Start = s.NameToken.Span.Start, End = s.NameToken.Span.Start + s.Name.Length,
                    ValueStart = s.Number != null ? Signed(s.ValueToken.Span.Start) : s.ValueToken.Span.Start, ValueEnd = s.ValueToken.Span.End,
                    Number = s.Number, Word = s.Word, Singer = line.Singer,
                });
        }
        foreach (var mark in root.DescendantNodes<LilySharp.Core.Syntax.MusicMarkSyntax>())
        {
            if (LilySharp.Core.Semantics.AnnotationValues.SingWritten(mark) is not { } written)
                continue;
            foreach (var (s, name, value) in written)
                settings.Add(new SingSettingDto
                {
                    Name = s.Name, Start = name.Span.Start, End = name.Span.Start + s.Name.Length,
                    ValueStart = s.Number != null ? Signed(value.Span.Start) : value.Span.Start, ValueEnd = value.Span.End,
                    Number = s.Number, Word = s.Word, Arrives = s.Arrives, OnNote = true,
                });
        }
        return new SingSettingsResponse { Settings = [.. settings], Singers = [.. singers], Version = doc.Version };
    }

    /// <summary>The settings of a note's <c>@sing(…)</c> for the editor: the ones held from the
    /// note, or the ones arrived at; null with none.</summary>
    private static System.Collections.Generic.Dictionary<string, object>? SingDto(string? sing, bool arrives)
    {
        var some = LilySharp.Core.Semantics.AnnotationValues.SingOf(sing).Where(s => s.Arrives == arrives).ToList();
        return some.Count == 0 ? null : some.ToDictionary(s => s.Name, s => s.Number is { } number ? (object)number : s.Word ?? "");
    }

    /// <summary>Where the singer line that begins at <paramref name="start"/> ends: the end of
    /// its line of text, less a comment and the blanks before it.</summary>
    private static int EndOfSingerLine(string text, int start)
    {
        int end = text.IndexOfAny(['\r', '\n'], start);
        if (end < 0)
            end = text.Length;
        int comment = text.IndexOf("//", start, end - start, StringComparison.Ordinal);
        if (comment >= 0)
            end = comment;
        while (end > start && char.IsWhiteSpace(text[end - 1]))
            end--;
        return end;
    }

    public SungResponse GetSung(SungParams @params)
    {
        SyntaxTree tree;
        if (@params.Text is { } text)
        {
            // A text of its own (a candidate's Play, owner's request 2026-11-11): read as
            // lilysharp/playbackText reads it, so the voices land on the notes that answer gives.
            string basePath = FilePathOf(@params.TextDocument?.Uri);
            tree = ExpandUsings(text, SyntaxTree.Parse(text), basePath,
                IncludeFiles.Read).Tree;
        }
        // The open document, or a file on the disk the folder export names (session 1153).
        else if (ExportTreeOf(@params.TextDocument, @params.Path, out tree, out _) is { } problem)
            return new SungResponse { Error = problem };
        if (tree.HasErrors)
            return new SungResponse { Error = "Score has errors" };
        try
        {
            var score = RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), @params.RenderName);
            var root = tree.GetRoot();
            // Every singable lyrics of the file (AllLyrics, session 1163): the score's rows, and
            // for each lyrics track the score does not place, a row singing the track's default
            // part — over that part's melody, which the export then sounds whether or not the score
            // shows it (MidiExporter.AllParts). One voice a part: a track never joins a part the score
            // already sings, whose words and singer are the score's (owner's report 2026-11-09,
            // session 1192: a verse written first took the place of the verse the score named).
            var spec = score;
            // Which parts the score shown sings itself (its rows and the singers no staff shows).
            var shownSounds = score != null ? LilySharp.Core.Svg.Collector.SungStaves.Of(root, score) : null;
            var inScore = shownSounds != null ? new HashSet<string>(shownSounds.Sung, StringComparer.Ordinal) : null;
            // Every singer line the shown score does not sing, for the practice menu to switch on:
            // sung unseen, marked as not the score's own (docs/lyrics-singer-design.md §7).
            if (@params.AllLyrics && score != null)
            {
                var hidden = score.HiddenStaves.IsDefault ? [] : score.HiddenStaves;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var line in LilySharp.Core.Music.LyricSinger.LinesFor(root, score))
                {
                    if (line.Part is not { } part || inScore!.Contains(part) || !seen.Add(part))
                        continue;
                    hidden = hidden.Add(new LilySharp.Core.Svg.Collector.HiddenStaffSpec(part, "voice"));
                }
                if (seen.Count > 0)
                    spec = score with { HiddenStaves = hidden };
            }
            var midi = new LilySharp.Core.Midi.MidiExporter { Form = spec?.Form, Score = spec, AllParts = @params.AllLyrics && spec != null }.Export(tree);
            var voices = LilySharp.Core.Midi.SungVoices.Read(midi, LilySharp.Core.Midi.MidiClock.SecondsOf(midi));
            var names = LilySharp.Core.Music.LyricSinger.NamesReaderOf(tree.GetRoot(), spec);
            // Where each voice's singer line stands, and its first syllable that is not kana: the
            // places the editor's warnings are put at (owner, 2026-11-13).
            var lineOf = LilySharp.Core.Music.LyricSinger.LineReaderOf(tree.GetRoot(), spec);
            var asked = LilySharp.Core.Audio.SingerPlan.AskedOf(voices, null, v => lineOf(v.Track, v.Part, v.Line),
                tree.Text, LilySharp.Core.Editing.SingerParts.MadeFrom(tree.Text));
            string? PlaceOf(LilySharp.Core.Midi.SungVoice v)
                => v.WarningAt is { } at ? LilySharp.Core.Audio.SingerFailure.PlaceOf(tree.GetRoot(), tree.Text, at) : null;
            return new SungResponse
            {
                Voices = voices.Select((v, i) => new SungVoiceDto
                {
                    Part = v.Part,
                    Key = v.Key ?? v.Part,
                    Language = v.Language,
                    Warnings = v.Warnings.Select(w => LilySharp.Core.Audio.SingerFailure.WithPlace(w, PlaceOf(v))).ToArray(),
                    SingerStart = asked[i].Position >= 0 ? asked[i].Position : null,
                    SingerEnd = asked[i] is { Position: >= 0, Text: { } } ? EndOfSingerLine(tree.Text, asked[i].Position) : null,
                    WarningAt = v.WarningAt,
                    WarningPlace = PlaceOf(v),
                    InScore = inScore == null || inScore.Contains(v.Part),
                    Singer = v.Singer,
                    Name = names(v.Track, v.Part, v.Line),
                    Gender = v.Gender,
                    Track = v.Track,
                    Sing = v.Sing?.ToDictionary(p => p.Key, p => p.Value.Number is { } number ? (object)number : p.Value.Word ?? ""),
                    Notes = v.Notes.Select(n => new SungNoteDto
                    {
                        T = n.Start, D = n.Duration, P = n.Pitch, L = n.Lyric, M = n.Melisma, B = n.Breath, S = n.SourcePos, V = n.Level, E = n.LevelEnd, I = n.Inhale, H = n.Hyphen, Vb = n.Vibrato, Vr = n.VibratoRate, Sg = SingDto(n.Sing, arrives: false), St = SingDto(n.Sing, arrives: true),
                    }).ToArray(),
                }).OrderByDescending(v => v.InScore).ToArray(),
                PrintedWords = shownSounds?.PrintedWords?.ToArray() ?? [],
            };
        }
        catch (Exception ex)
        {
            return new SungResponse { Error = ex.Message };
        }
    }

    /// <summary>
    /// The lowest and highest note of each part of the score the preview shows, and where each is
    /// written (owner's request 2026-10-25: check the parts' ranges; find a singer's top note) —
    /// read off the notes Play sounds (<see cref="LilySharp.Core.Midi.PartRanges"/>).
    /// </summary>
    [JsonRpcMethod("lilysharp/ranges", UseSingleObjectParameterDeserialization = true)]
    public Task<RangesResponse> GetRangesAsync(RangesParams @params, CancellationToken token)
        => OffDispatch(() => GetRanges(@params), token);

    public RangesResponse GetRanges(RangesParams @params)
    {
        var doc = _documentManager.GetDocument(@params.TextDocument.Uri);
        if (doc == null)
            return new RangesResponse { Error = DocumentNotFound };
        var (tree, _) = ExpandUsings(doc, @params.TextDocument.Uri);
        if (tree.HasErrors)
            return new RangesResponse { Error = "Score has errors" };
        try
        {
            var score = RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), @params.RenderName);
            var midi = new LilySharp.Core.Midi.MidiExporter { Form = score?.Form, Score = score }.Export(tree);
            static RangeEnd Dto(LilySharp.Core.Midi.RangeEnd e)
                => new() { Pitch = e.Pitch, Name = e.Name, Positions = e.Positions.ToArray() };
            int fifths = LilySharp.Core.Midi.PartRanges.KeyFifths(tree.GetRoot());
            return new RangesResponse
            {
                Parts = LilySharp.Core.Midi.PartRanges.Of(midi, doc.Text, fifths)
                    .Select(p => new PartRange
                    {
                        Name = p.Name, Low = Dto(p.Low), High = Dto(p.High),
                        Weights = p.Weights.ToArray(), TessituraLow = p.TessituraLow, TessituraHigh = p.TessituraHigh,
                        Tessitura = LilySharp.Core.Midi.PartRanges.SpelledFromPitch(p.TessituraLow, fifths) + "–"
                            + LilySharp.Core.Midi.PartRanges.SpelledFromPitch(p.TessituraHigh, fifths),
                    })
                    .ToArray(),
            };
        }
        catch (Exception ex)
        {
            return new RangesResponse { Error = ex.Message };
        }
    }

    /// <summary>
    /// The beats the preview's metronome clicks (owner's request 2026-10-25), in ticks: one per
    /// beat of the meter in force (its denominator's note) in every bar, the bar's first
    /// accented. The bars are where the bar lines stand as the music plays
    /// (<see cref="LilySharp.Core.Midi.MidiFile.BarTicks"/>), so a pickup keeps the accents on
    /// the bar lines (they had followed the meter from tick 0, a pickup's beat short): a first
    /// bar shorter than the meter is a pickup, its beats counted back from its bar line and none
    /// accented. Past the last bar line, the meter's bars to the last note's end.
    /// </summary>
    internal static List<(int Tick, bool Accent)> MetronomeBeats(LilySharp.Core.Midi.MidiFile midi, int tpq)
        => MetronomeBeats(midi, tpq, out _);

    /// <summary><see cref="MetronomeBeats(LilySharp.Core.Midi.MidiFile, int)"/>, and where the music's
    /// last bar ends (<paramref name="barEnd"/>, in ticks): its closing bar line when one stands after
    /// the last note's start — the beats stop there, a fermata's hold past it unclicked — else the end
    /// of the meter's bar the last note ends in. A loop's round ends at the bar line its stretch
    /// ends by (the preview's loop; owner's report 2026-10-25: a guessed bar past a fermata made a
    /// round a bar too long).</summary>
    internal static List<(int Tick, bool Accent)> MetronomeBeats(LilySharp.Core.Midi.MidiFile midi, int tpq, out int barEnd)
        => LilySharp.Core.Midi.MetronomeBeats.Of(midi, tpq, out barEnd);

    /// <summary>The note events a tree plays, in seconds — what <c>lilysharp/playback</c> and
    /// <c>lilysharp/playbackText</c> answer.</summary>
    private static PlaybackResponse PlaybackOf(SyntaxTree tree, string? renderName, bool soundingOnly = false)
    {
        if (tree.HasErrors)
            return new PlaybackResponse { Error = "Score has errors" };
        RenderSpec? score;
        try
        {
            // The score the preview DRAWS (the same choice the svg request makes), so its
            // Play sounds that score's parts and form — `score p2 { staff p2 }`
            // used to play p1 as well.
            score = RenderSpecParser.Choose(RenderSpecParser.FindAll(tree), renderName);
        }
        catch (Exception ex)
        {
            return new PlaybackResponse { Error = ex.Message };
        }
        return PlaybackOf(tree, score, soundingOnly);
    }

    /// <summary>The note events of one score (null: a file with no <c>score</c> block — its
    /// primary form, every part), as <see cref="PlaybackOf(SyntaxTree, string?)"/> gives them
    /// for the score it chooses. The WAV the editor renders asks for each score it writes
    /// (<see cref="ExportParams.PlaybackOnly"/>).</summary>
    private static PlaybackResponse PlaybackOf(SyntaxTree tree, RenderSpec? score, bool soundingOnly = false)
    {
        if (tree.HasErrors)
            return new PlaybackResponse { Error = "Score has errors" };
        try
        {
            var midi = new LilySharp.Core.Midi.MidiExporter { Form = score?.Form, Score = score }.Export(tree);
            int tpq = midi.TicksPerQuarterNote;

            // Tempo map: merged from all tracks, sorted; default 120 bpm — the clock the singing
            // voice (lilysharp/sung) reads too.
            var clock = LilySharp.Core.Midi.MidiClock.SecondsOf(midi);
            double SecondsAt(int tick) => clock(tick);

            // The score's tracks, then the parts the score does NOT sound (owner's request
            // 2026-11-08: the practice menu turns them on; off until it does — PlaybackResponse.Extra):
            // the file's every part in this score's form, less the tracks above, each on a channel
            // the score's tracks leave free (two parts on one channel would fight over its program).
            // The preview's list only: the .mid export is untouched. Every part of THIS score's
            // export (AllParts): an export with no score named fell to the file's first score, so
            // the list was empty whenever the first score was the one shown (owner's report
            // 2026-11-09: "only the score's parts are listed").
            var tracks = midi.Tracks.Select(t => (Track: t, Channel: t.Channel)).ToList();
            var extra = new List<string>();
            // Not for a caller that reads the sounding notes alone (PlaybackParams.SoundingOnly):
            // the second export below is the whole price of the list.
            if (score != null && !soundingOnly)
            {
                var all = new LilySharp.Core.Midi.MidiExporter { Form = score.Form, Score = score, AllParts = true }.Export(tree);
                var names = midi.Tracks.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
                var used = midi.Tracks.Where(t => t.Notes.Count > 0).Select(t => t.Channel).ToHashSet();
                foreach (var t in all.Tracks)
                {
                    if (names.Contains(t.Name) || t.Notes.Count == 0)
                        continue;
                    int channel = t.Channel == 9 ? 9 : Enumerable.Range(0, 16).FirstOrDefault(c => c != 9 && !used.Contains(c), t.Channel);
                    used.Add(channel);
                    tracks.Add((t, channel));
                    extra.Add(t.Name);
                }
            }

            // Where a sung part is sung by nobody (a singer line's `in` leaves a section out):
            // its notes there are marked, and the editor plays them on the instrument.
            var voices = LilySharp.Core.Midi.SungVoices.Read(midi, SecondsAt);
            var unsung = LilySharp.Core.Midi.SungVoices.Unsung(midi, voices, SecondsAt);
            // …and whose each sung section is, in a part with several singer lines: the server
            // does not know which of them this machine can sing, so the editor, which asks, marks
            // the sections all of whose singers are missing (PlaybackNote.W; found 2026-11-13: a
            // verse line that sang kept the chorus of a line nobody could sing silent).
            var sungBy = LilySharp.Core.Midi.SungVoices.SungBy(midi, voices, SecondsAt);

            // Each note with its track: the channel and the program the .mid would play it
            // with (SplitIntoPartTracks gave every part a track and a channel), so the
            // preview's SoundFont synth sounds the same instruments the export does.
            var notes = tracks
                .SelectMany((x, ti) => x.Track.Notes.Select(n => (x.Track, x.Channel, Note: n, Index: ti)))
                .OrderBy(x => x.Note.StartTick)
                .Select(x =>
                {
                    var n = x.Note;
                    double t0 = SecondsAt(n.StartTick);
                    return new PlaybackNote
                    {
                        T = t0,
                        D = Math.Max(0.03, SecondsAt(n.StartTick + n.DurationTicks) - t0),
                        P = n.Pitch + 0.5 * n.QuarterBend,
                        V = n.Velocity,
                        // A note a `%` sounds lights the mark, not the bars it was played from.
                        S = n.Percent?.Pos ?? n.SourcePos,
                        O = n.Percent?.Ordinal ?? n.SourceOrdinal,
                        // …and which of the bar's notes it plays, so the loop does not take the
                        // bar's later notes for repeats of its first (PlaybackNote.Q).
                        Q = n.Percent != null ? n.SourcePos : null,
                        I = n.Timbre,
                        C = n.Channel == 9 ? 9 : x.Channel,
                        G = n.Channel == 9 ? 0 : x.Track.Program ?? 0,
                        N = x.Index,
                        K = n.Strike,
                        M = n.PrintedBar,
                        U = unsung.TryGetValue(x.Track.Name, out var free) && free.Any(r => n.StartTick >= r.From && n.StartTick < r.To),
                        W = sungBy.TryGetValue(x.Track.Name, out var lines)
                            ? lines.FirstOrDefault(r => n.StartTick >= r.From && n.StartTick < r.To).Keys : null,
                    };
                })
                .Concat(tracks.SelectMany((x, ti) => x.Track.Pedals.Where(c => c.Controller == LilySharp.Core.Midi.PedalChange.Sustain).Select(c => new PlaybackNote
                {
                    T = SecondsAt(c.Tick),
                    P = PlaybackNote.PedalPitch,
                    V = c.Down ? 127 : 0,
                    S = -1,
                    C = x.Channel,
                    G = x.Track.Program ?? 0,
                    N = ti,
                })))
                // Each `%` lit for the whole of the bars it sounds (owner's request 2026-11-16: its
                // notes alone lit it only while they sounded, and the bars' own notes lit instead).
                .Concat(tracks.SelectMany((x, ti) => x.Track.Notes
                    .Where(n => n.Percent != null)
                    .Select(n => (n.Percent!.Value.Pos, n.Percent!.Value.Ordinal, n.Percent!.Value.Length, Start: n.StartTick - n.Percent!.Value.Lead, Bar: n.PrintedBar))
                    .Distinct()
                    .Select(m => new PlaybackNote
                    {
                        T = SecondsAt(m.Start),
                        D = Math.Max(0.03, SecondsAt(m.Start + m.Length) - SecondsAt(m.Start)),
                        S = m.Pos,
                        O = m.Ordinal,
                        C = x.Channel,
                        G = x.Track.Program ?? 0,
                        N = ti,
                        L = true,
                        M = m.Bar,
                    })))
                .OrderBy(n => n.T)
                .ToArray();

            // What the editor's text lights besides the notes (PlaybackResponse.Lights; owner's
            // requests 2026-11-17): each rest and each section's name in the form while it lasts
            // (MidiTrack.TextMarks), each syllable while its note does — lights (nothing sounds
            // them) the page does not draw on the score (E).
            var lights = tracks.SelectMany((x, ti) => x.Track.TextMarks.Select(r => new PlaybackNote
                {
                    T = SecondsAt(r.StartTick),
                    D = Math.Max(0.03, SecondsAt(r.StartTick + r.DurationTicks) - SecondsAt(r.StartTick)),
                    S = r.SourcePos,
                    O = r.SourceOrdinal,
                    C = x.Channel,
                    G = x.Track.Program ?? 0,
                    N = ti,
                    L = true,
                    E = true,
                    M = r.PrintedBar,
                }))
                .Concat(LyricLights())
                .OrderBy(n => n.T)
                .ToArray();

            // Each syllable sung, lit in the editor's text while its note lasts (owner's request
            // 2026-11-17) — on its part's track, so a part turned off takes its words' lights along.
            IEnumerable<PlaybackNote> LyricLights()
            {
                var noteLength = new Dictionary<(string?, int), int>();
                foreach (var n in tracks.SelectMany(x => x.Track.Notes))
                    noteLength[(n.Part, n.StartTick)] = Math.Max(n.DurationTicks, noteLength.GetValueOrDefault((n.Part, n.StartTick)));
                var trackOf = tracks.Select((x, ti) => (x.Track.Name, ti)).GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.First().ti);
                foreach (var (x, ti) in tracks.Select((x, ti) => (x, ti)))
                {
                    var words = x.Track.Lyrics.Where(l => l.SourcePos >= 0).OrderBy(l => l.Tick).ToList();
                    for (int i = 0; i < words.Count; i++)
                    {
                        var w = words[i];
                        int next = words.Skip(i + 1).Where(o => o.Part == w.Part && o.Tick > w.Tick).Select(o => o.Tick).DefaultIfEmpty(-1).First();
                        int length = noteLength.TryGetValue((w.Part, w.Tick), out int d) ? d : next > 0 ? next - w.Tick : tpq;
                        int track = w.Part != null && trackOf.TryGetValue(w.Part, out int own) ? own : ti;
                        yield return new PlaybackNote
                        {
                            T = SecondsAt(w.Tick),
                            D = Math.Max(0.03, SecondsAt(w.Tick + length) - SecondsAt(w.Tick)),
                            S = w.SourcePos,
                            C = tracks[track].Channel,
                            G = tracks[track].Track.Program ?? 0,
                            N = track,
                            L = true,
                            E = true,
                        };
                    }
                }
            }

            var beats = MetronomeBeats(midi, tpq, out int barEnd).Select(b => new PlaybackBeat { T = SecondsAt(b.Tick), A = b.Accent }).ToArray();
            // Which parts the score sings and which it plays, off its rows (a file with no score
            // block plays every part).
            var shown = score ?? RenderSpecParser.PlayedSpec(tree, null, null);
            var sounds = shown != null ? LilySharp.Core.Svg.Collector.SungStaves.Of(tree.GetRoot(), shown) : null;
            return new PlaybackResponse
            {
                Notes = notes,
                Lights = lights,
                Parts = tracks.Select(x => x.Track.Name).ToArray(),
                Extra = extra.ToArray(),
                Sung = sounds?.Sung.ToArray() ?? [],
                Played = sounds?.Played.ToArray() ?? midi.Tracks.Select(t => t.Name).ToArray(),
                Beats = beats,
                BarEnd = SecondsAt(barEnd),
                Rows = shown != null ? RowsOf(shown) : [],
            };
        }
        catch (Exception ex)
        {
            return new PlaybackResponse { Error = ex.Message };
        }
    }

    /// <summary>
    /// The names the score's rows show, top to bottom (<see cref="PlaybackResponse.Rows"/>): each
    /// staff's, tab's or chord row's part or track in the score's written order (a chord row folded
    /// onto the staff below it stays above that staff's part); each name once. Shosa's request 5
    /// (owner's decision 2026-11-20): a mixer's lines go as the page does.
    /// </summary>
    internal static string[] RowsOf(LilySharp.Core.Svg.Collector.RenderSpec spec)
    {
        var rows = new List<string>();
        void Add(string? name)
        {
            if (!string.IsNullOrEmpty(name) && !rows.Contains(name))
                rows.Add(name);
        }
        foreach (var b in spec.GetVoiceBindings())
        {
            // A chord row directly above a staff is folded onto it (GRAMMAR: a score is a stack of
            // bands): drawn above the staff, so it comes first.
            Add(b.WithChords);
            Add(b.VoiceName);
        }
        return [.. rows];
    }
}
