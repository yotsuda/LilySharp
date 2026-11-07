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

using System;
using System.IO;
using System.Linq;
using LilySharp.Lsp;
using LilySharp.Lsp.Protocol;
using Xunit;

namespace LilySharp.Tests.Lsp;

/// <summary>
/// The <c>lilysharp/export</c> request's batch face (HANDOFF §2F F-export): a file
/// named by path rather than by open document, and <c>All</c> writing every score
/// under the CLI's <c>--all</c> names. The generators are tested elsewhere; these
/// guard the request wiring — which text is read, what each score's file is called,
/// what is left out and said so.
/// </summary>
public sealed class ExportRequestTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "lilysharp-export-" + Guid.NewGuid().ToString("N"));

    public ExportRequestTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static LilySharpLanguageServer Server() => new(Stream.Null, Stream.Null);

    // Two scores on the SAME staff, differing only in form: `main` plays the section
    // once, `sub` twice — so a difference between the two outputs of a form-driven
    // format (the twin, MIDI) can only come from the form the request chose.
    private const string TwoScores = """
        title "Two"
        time 4/4
        key c major
        part melody { clef treble }
        phrase mel { c'4 d e f | g4 a b c | }
        section Main { melody { mel } }
        form { Main }
        form sub { Main Main }
        score { staff melody }
        score sub { form sub staff melody }
        """;

    // Parts, a section and a form, but no `score` block — the renderer's default
    // picture, and one file to write.
    private const string NoScoreBlock = """
        time 4/4
        key c major
        part melody { clef treble }
        section Main { melody { c'4 d e f | g1 | } }
        form { Main }
        """;

    private string Book(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    private string Out => Path.Combine(_dir, "out");

    [Theory]
    [InlineData("svg", ".svg")]
    [InlineData("pdf", ".pdf")]
    [InlineData("png", ".png")]
    [InlineData("ly", ".ly")]
    [InlineData("musicxml", ".xml")]
    [InlineData("midi", ".mid")]
    public void All_WritesEveryScore_UnderTheCliNames(string format, string ext)
    {
        var book = Book("two.lys", TwoScores);

        var response = Server().Export(new ExportParams
        {
            Path = book, Format = format, All = true, OutputDirectory = Out,
        });

        Assert.True(response.Success, response.Error);
        Assert.Equal(
            new[] { Path.Combine(Out, "two" + ext), Path.Combine(Out, "two-sub" + ext) },
            response.OutputPaths);
        Assert.All(response.OutputPaths!, p => Assert.True(new FileInfo(p).Length > 0, p));
        Assert.Empty(response.Warnings!);
        Assert.Equal(Out, response.OutputPath);
    }

    [Fact]
    public void All_GivesAFormDrivenFormat_EachScoresOwnForm()
    {
        var book = Book("two.lys", TwoScores);

        var response = Server().Export(new ExportParams
        {
            Path = book, Format = "ly", All = true, OutputDirectory = Out,
        });

        Assert.True(response.Success, response.Error);
        // Same staff, so the only thing that can differ is the form — and it must,
        // or `sub` was written with the primary form the one-score call uses.
        Assert.NotEqual(
            File.ReadAllText(Path.Combine(Out, "two.ly")),
            File.ReadAllText(Path.Combine(Out, "two-sub.ly")));
    }

    // The preview's Export button: one score, named. `sub` plays Main twice and `main` once,
    // so a form-driven format that honours the name writes a different file for each — and
    // the same file the batch export writes for that score. Until 2026-10-03 the one-score
    // call handed MIDI, MusicXML and the twin a null form (the primary): the owner's
    // `score practice` on `form practice { B … }` exported a .mid that began with section A.
    [Theory]
    [InlineData("midi", ".mid")]
    [InlineData("musicxml", ".xml")]
    [InlineData("ly", ".ly")]
    public void OneScore_GivesAFormDrivenFormat_ThatScoresOwnForm(string format, string ext)
    {
        var book = Book("two.lys", TwoScores);
        var subOnly = Path.Combine(_dir, "sub-only" + ext);
        var mainOnly = Path.Combine(_dir, "main-only" + ext);

        var sub = Server().Export(new ExportParams { Path = book, Format = format, OutputPath = subOnly, RenderName = "sub" });
        var main = Server().Export(new ExportParams { Path = book, Format = format, OutputPath = mainOnly, RenderName = "main" });
        var all = Server().Export(new ExportParams { Path = book, Format = format, All = true, OutputDirectory = Out });

        Assert.True(sub.Success, sub.Error);
        Assert.True(main.Success, main.Error);
        Assert.True(all.Success, all.Error);
        Assert.Equal(File.ReadAllBytes(Path.Combine(Out, "two-sub" + ext)), File.ReadAllBytes(subOnly));
        Assert.Equal(File.ReadAllBytes(Path.Combine(Out, "two" + ext)), File.ReadAllBytes(mainOnly));
        Assert.NotEqual(File.ReadAllBytes(mainOnly), File.ReadAllBytes(subOnly));
    }

    [Fact]
    public void All_Vsqx_WritesTheFirstScore_AndSaysWhatItLeftOut()
    {
        var book = Book("two.lys", TwoScores);

        var response = Server().Export(new ExportParams
        {
            Path = book, Format = "vsqx", All = true, OutputDirectory = Out,
        });

        Assert.True(response.Success, response.Error);
        Assert.Equal(new[] { Path.Combine(Out, "two.vsqx") }, response.OutputPaths);
        var warning = Assert.Single(response.Warnings!);
        Assert.Contains("sub", warning);
    }

    // WAV: the score's MIDI through the SoundFont 2 the editor names (its playback font, or the
    // one it asked for). `sub` plays Main twice, so its sound is longer — the one-score call
    // honours the score as MIDI does.
    [Fact]
    public void Wav_PlaysTheChosenScore_ThroughTheNamedFont()
    {
        var book = Book("two.lys", TwoScores);
        var font = TinySoundFont.Write(Path.Combine(_dir, "sine.sf2"));
        var subOnly = Path.Combine(_dir, "sub-only.wav");
        var mainOnly = Path.Combine(_dir, "main-only.wav");

        var sub = Server().Export(new ExportParams { Path = book, Format = "wav", OutputPath = subOnly, RenderName = "sub", SoundFont = font });
        var main = Server().Export(new ExportParams { Path = book, Format = "wav", OutputPath = mainOnly, RenderName = "main", SoundFont = font });
        var all = Server().Export(new ExportParams { Path = book, Format = "wav", All = true, OutputDirectory = Out, SoundFont = font });

        Assert.True(sub.Success, sub.Error);
        Assert.True(main.Success, main.Error);
        Assert.True(all.Success, all.Error);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(subOnly), 0, 4));
        Assert.True(new FileInfo(subOnly).Length > new FileInfo(mainOnly).Length, "sub plays Main twice");
        Assert.Equal(
            new[] { Path.Combine(Out, "two.wav"), Path.Combine(Out, "two-sub.wav") },
            all.OutputPaths);
        Assert.Equal(File.ReadAllBytes(subOnly), File.ReadAllBytes(Path.Combine(Out, "two-sub.wav")));
    }

    // No font, or one the synth cannot read (a DLS bank, a SoundFont 3 — what the preview's own
    // synth plays): refused with the reason, before the folder is made.
    [Fact]
    public void Wav_WithoutAReadableFont_IsRefused_AndWritesNothing()
    {
        var book = Book("two.lys", TwoScores);
        var sf3 = Path.Combine(_dir, "font.sf3");
        File.Copy(TinySoundFont.Write(Path.Combine(_dir, "a.sf2")), sf3);

        var none = Server().Export(new ExportParams { Path = book, Format = "wav", All = true, OutputDirectory = Out });
        var compressed = Server().Export(new ExportParams { Path = book, Format = "wav", All = true, OutputDirectory = Out, SoundFont = sf3 });

        Assert.False(none.Success);
        Assert.Contains(".sf2", none.Error);
        Assert.False(compressed.Success);
        Assert.Contains("SoundFont 3", compressed.Error);
        Assert.False(Directory.Exists(Out));
    }

    // A WAV the editor renders through the preview's synth and font (PlaybackOnly): no font asked
    // for, nothing written, and each score's file named as the server names it, with the notes
    // the preview's Play sounds for that score — `sub` plays Main twice.
    [Fact]
    public void Wav_PlaybackOnly_GivesEachScoresNotes_AndWritesNothing()
    {
        var book = Book("two.lys", TwoScores);
        var subOnly = Path.Combine(_dir, "sub-only.wav");

        var sub = Server().Export(new ExportParams { Path = book, Format = "wav", OutputPath = subOnly, RenderName = "sub", PlaybackOnly = true });
        var all = Server().Export(new ExportParams { Path = book, Format = "wav", All = true, OutputDirectory = Out, PlaybackOnly = true });

        Assert.True(sub.Success, sub.Error);
        Assert.True(all.Success, all.Error);
        var one = Assert.Single(sub.Playbacks!);
        Assert.Equal(subOnly, one.OutputPath);
        Assert.False(File.Exists(subOnly));
        Assert.Equal(new[] { Path.Combine(Out, "two.wav"), Path.Combine(Out, "two-sub.wav") }, all.OutputPaths);
        Assert.Equal(all.OutputPaths, all.Playbacks!.Select(p => p.OutputPath));
        Assert.All(all.OutputPaths!, p => Assert.False(File.Exists(p), p));
        Assert.Equal(8, all.Playbacks[0].Notes.Length);
        Assert.Equal(16, all.Playbacks[1].Notes.Length);
        Assert.Equal(one.Notes.Select(n => (n.T, n.P)), all.Playbacks[1].Notes.Select(n => (n.T, n.P)));
    }

    // An MP3 is the editor's alone (it encodes what it renders): asked with PlaybackOnly, each
    // score's file under the .mp3 name and its notes, nothing written; asked to write, refused.
    [Fact]
    public void Mp3_IsTheEditors_NamedAndGivenItsNotes_NeverWritten()
    {
        var book = Book("two.lys", TwoScores);

        var all = Server().Export(new ExportParams { Path = book, Format = "mp3", All = true, OutputDirectory = Out, PlaybackOnly = true });
        var written = Server().Export(new ExportParams { Path = book, Format = "mp3", All = true, OutputDirectory = Out });

        Assert.True(all.Success, all.Error);
        Assert.Equal(new[] { Path.Combine(Out, "two.mp3"), Path.Combine(Out, "two-sub.mp3") }, all.OutputPaths);
        Assert.Equal(new[] { 8, 16 }, all.Playbacks!.Select(p => p.Notes.Length));
        Assert.All(all.OutputPaths!, p => Assert.False(File.Exists(p), p));
        Assert.False(written.Success);
        Assert.Contains("editor", written.Error);
    }

    // An MP4 (owner's request 2026-11-07): the server draws the video's pictures and the editor
    // encodes them with the sound. A section the form plays twice is printed twice, and the note
    // that sounds is lit where it is printed that time — the second c'' on the second copy.
    [Fact]
    public void Mp4_DrawsThePictures_TheSoundingCopyLit()
    {
        var book = Book("twice.lys", """
            tempo 120
            part m { clef treble }
            section A { m { c''4 d e f | } }
            form { A A }
            score { staff m }
            """);
        var response = Server().Export(new ExportParams { Path = book, Format = "mp4", OutputPath = Path.Combine(_dir, "twice.mp4"), PlaybackOnly = true });
        Assert.True(response.Success, response.Error);
        var job = Assert.Single(response.Playbacks!);
        try
        {
            Assert.False(File.Exists(job.OutputPath));
            Assert.Equal(1.5, job.Lead);
            // The list: each picture and its duration, the last named again for its duration to count.
            var lines = File.ReadAllLines(job.Frames!);
            var shown = new List<(string File, double Seconds)>();
            for (int i = 0; i + 1 < lines.Length; i += 2)
                shown.Add((lines[i][6..^1], double.Parse(lines[i + 1]["duration ".Length..], System.Globalization.CultureInfo.InvariantCulture)));
            // Eight quarters at 120 (4 s), two to ring out, and the lead.
            Assert.Equal(1.5 + 4 + 2, shown.Sum(s => s.Seconds), 1);
            string At(double t)
            {
                double end = 0;
                foreach (var (file, seconds) in shown) { end += seconds; if (t < end) return file; }
                return shown[^1].File;
            }
            double LitX(string file)
            {
                using var bitmap = SkiaSharp.SKBitmap.Decode(file);
                double sum = 0; int count = 0;
                for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        var p = bitmap.GetPixel(x, y);
                        if (p.Red > 200 && p.Green is > 80 and < 170 && p.Blue < 90) { sum += x; count++; }
                    }
                Assert.True(count > 0, $"something is lit in {file}");
                return sum / count;
            }
            using (var still = SkiaSharp.SKBitmap.Decode(At(0.5)))
                Assert.Equal((1240, 1754), (still.Width, still.Height));
            double first = LitX(At(1.5 + 0.25)), second = LitX(At(1.5 + 2.25));
            Assert.True(second > first + 100, $"the second c'' ({second:0}) is lit right of the first ({first:0})");
        }
        finally
        {
            Directory.Delete(job.WorkDirectory!, true);
        }
    }

    [Fact]
    public void All_NoScoreBlock_WritesOneFile_UnderTheFilesName()
    {
        var book = Book("plain.lys", NoScoreBlock);

        var response = Server().Export(new ExportParams
        {
            Path = book, Format = "svg", All = true, OutputDirectory = Out,
        });

        Assert.True(response.Success, response.Error);
        Assert.Equal(new[] { Path.Combine(Out, "plain.svg") }, response.OutputPaths);
        Assert.True(File.Exists(Path.Combine(Out, "plain.svg")));
    }

    [Fact]
    public void AnOpenDocument_Wins_OverTheFileOnDisk()
    {
        var book = Book("two.lys", TwoScores.Replace("\"Two\"", "\"Disk\""));
        var uri = new Uri(book);
        var server = Server();
        server.DidOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri, Text = TwoScores.Replace("\"Two\"", "\"Open\""),
                LanguageId = "lilysharp", Version = 1,
            },
        });

        var response = server.Export(new ExportParams
        {
            TextDocument = new TextDocumentIdentifier { Uri = uri },
            Path = book, Format = "svg", All = true, OutputDirectory = Out,
        });

        Assert.True(response.Success, response.Error);
        var svg = File.ReadAllText(Path.Combine(Out, "two.svg"));
        Assert.Contains("Open", svg);
        Assert.DoesNotContain("Disk", svg);
    }

    [Fact]
    public void OneScore_IsUnchanged_AndNamesTheOneFile()
    {
        var book = Book("two.lys", TwoScores);
        var target = Path.Combine(_dir, "chosen.svg");

        var response = Server().Export(new ExportParams
        {
            Path = book, Format = "svg", OutputPath = target, RenderName = "sub",
        });

        Assert.True(response.Success, response.Error);
        Assert.Equal(target, response.OutputPath);
        Assert.Equal(new[] { target }, response.OutputPaths);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public void ASyntaxError_ReturnsIt_AndWritesNothing()
    {
        var book = Book("broken.lys", "part m { section A { c4 d }");

        var response = Server().Export(new ExportParams
        {
            Path = book, Format = "pdf", All = true, OutputDirectory = Out,
        });

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
        Assert.False(Directory.Exists(Out));
    }

    [Fact]
    public void AMissingFile_ReturnsAnError()
    {
        var response = Server().Export(new ExportParams
        {
            Path = Path.Combine(_dir, "no-such.lys"), Format = "pdf", All = true, OutputDirectory = Out,
        });

        Assert.False(response.Success);
        Assert.Contains("no-such.lys", response.Error);
    }

    [Fact]
    public void NeitherDocumentNorPath_ReturnsAnError()
    {
        var response = Server().Export(new ExportParams { Format = "pdf" });

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
    }

    [Fact]
    public void All_WithoutAnOutputDirectory_ReturnsAnError()
    {
        var book = Book("two.lys", TwoScores);

        var response = Server().Export(new ExportParams { Path = book, Format = "pdf", All = true });

        Assert.False(response.Success);
        Assert.NotNull(response.Error);
    }

    [Fact]
    public void AnUnknownFormat_ReturnsAnError()
    {
        var book = Book("two.lys", TwoScores);

        var response = Server().Export(new ExportParams
        {
            Path = book, Format = "docx", All = true, OutputDirectory = Out,
        });

        Assert.False(response.Success);
        Assert.Contains("docx", response.Error);
    }
}
