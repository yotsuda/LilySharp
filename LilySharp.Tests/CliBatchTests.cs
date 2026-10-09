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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// <c>lysc &lt;cmd&gt; --batch &lt;list&gt;</c> — many files, one process.
/// </summary>
/// <remarks>
/// ★ THE CLAIM WORTH GUARDING is not that the flag parses; it is that a batched run writes
/// what N separate runs write. The whole point of sharing a process is to pay the ~1 s
/// launch once, and the whole risk of sharing one is that something the CLI never had to
/// think about — a cache, a resolver, a static memo — carries from one document into the
/// next. So the tests here compare BYTES against separate runs rather than checking that
/// files appeared.
/// <para>
/// ⚠️ SPAWNS THE REAL CLI, for the reason <c>CliBestEffortOutputTests</c> gives at length:
/// as <c>dotnet lysc.dll</c>, never the apphost. The harness is copied from there rather
/// than shared, because lifting it would make one test class depend on another's private
/// shape for no gain.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public class CliBatchTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lysc-batch-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static readonly string Muxer = Path.Combine(
        Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..")),
        OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

    private static (int Exit, string Stdout, string Stderr) Lysc(params string[] args)
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "lysc.dll");
        Assert.True(File.Exists(dll), $"lysc.dll not beside the tests: {dll}");
        Assert.True(File.Exists(Muxer), $"no dotnet host where the runtime says one is: {Muxer}");

        var psi = new ProcessStartInfo(Muxer)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(dll);
        foreach (string a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        string outText = p.StandardOutput.ReadToEnd();
        string errText = p.StandardError.ReadToEnd();
        p.WaitForExit(120_000);
        return (p.ExitCode, outText, errText);
    }

    /// <summary>A one-bar book whose title is <paramref name="name"/>.</summary>
    private string Book(string name, string extra = "")
    {
        string path = Path.Combine(_dir, name + ".lys");
        File.WriteAllText(path,
            $"title \"{name}\"\n{extra}part m {{ clef treble }}\n"
            + "section A { m { c'4 d' e' f' | } }\nform { A }\nscore { staff m }\n");
        return path;
    }

    private string List(params string[] lines)
    {
        string path = Path.Combine(_dir, "list-" + Guid.NewGuid().ToString("N")[..6] + ".txt");
        File.WriteAllLines(path, lines);
        return path;
    }

    /// <summary>A folder under the test's own, named <paramref name="name"/> — where a run
    /// is told to write (outputs are named for their book; only the folder is chosen).</summary>
    private string Folder(string name) => Path.Combine(_dir, name);

    /// <summary>The output <paramref name="book"/> writes into <paramref name="folder"/>.</summary>
    private static string Out(string folder, string book, string ext)
        => Path.Combine(folder, Path.GetFileNameWithoutExtension(book) + ext);

    [Fact]
    public void ABatchWritesExactlyWhatSeparateRunsWrite()
    {
        // ★ THE CLAIM. Three books, rendered one process each, then all three in one; the
        // bytes must match. Anything a shared process carries between documents — a font
        // cache keyed too loosely, a static memo, a resolver pointed at the last book —
        // shows up here and nowhere cheaper.
        string[] books = [Book("alpha"), Book("beta"), Book("gamma")];
        string solo = Folder("solo"), batched = Folder("batch");

        foreach (string b in books)
            Assert.Equal(0, Lysc("svg", "-n", "-d", solo, b).Exit);

        string list = List([.. books.Select(b => $"{b}\t{batched}")]);
        var batch = Lysc("svg", "-n", "--batch", list);
        Assert.Equal(0, batch.Exit);

        foreach (string b in books)
            Assert.Equal(File.ReadAllBytes(Out(solo, b, ".svg")), File.ReadAllBytes(Out(batched, b, ".svg")));

        Assert.Contains("3 file(s), 0 failed", batch.Stdout);
    }

    [Fact]
    public void ABatchedPdfDoesNotInheritThePreviousBooksFonts()
    {
        // ⚠️ THE ONE PLACE A SHARED PROCESS WAS ALREADY KNOWN TO BE DANGEROUS.
        // PdfDocumentContext's own remark says PdfSharpCore lets the font resolver be set
        // ONCE PER PROCESS, and that "two documents with DIFFERENT fonts { } still overwrite
        // each other's faces, because the resolver they share has room for one answer". One
        // file per process made that unreachable; --batch makes it reachable, so it is
        // measured rather than reasoned about.
        //
        // ⚠️ A PDF IS NOT BYTE-COMPARABLE (it carries a creation time; two runs of the same
        // book differ — measured). What IS stable is the set of embedded faces, once the
        // per-document subset prefix ("ABCDEF+") is stripped.
        string bound = Book("bound", "fonts { title \"TeX Gyre Heros\" }\n");
        string plain = Book("plain");

        string SoloFaces(string book)
        {
            Assert.Equal(0, Lysc("pdf", "-d", Folder("solo"), book).Exit);
            return Faces(Out(Folder("solo"), book, ".pdf"));
        }
        string soloBound = SoloFaces(bound), soloPlain = SoloFaces(plain);

        // ★ POSITIVE CONTROL FIRST. If the two books embed the same faces on their own, this
        // instrument cannot see a leak at all and a green result would mean nothing.
        Assert.NotEqual(soloPlain, soloBound);

        // Both orders: the bound book must not colour the plain one, and the plain one must
        // not strip the bound one.
        foreach (var order in new[] { new[] { bound, plain }, new[] { plain, bound } })
        {
            string folder = Folder("first-" + Path.GetFileNameWithoutExtension(order[0]));
            string list = List([.. order.Select(b => $"{b}\t{folder}")]);
            Assert.Equal(0, Lysc("pdf", "--batch", list).Exit);
            Assert.Equal(soloBound, Faces(Out(folder, bound, ".pdf")));
            Assert.Equal(soloPlain, Faces(Out(folder, plain, ".pdf")));
        }
    }

    /// <summary>The PDF's embedded face names, without the per-document subset prefix.</summary>
    private static string Faces(string pdf)
    {
        string text = Encoding.Latin1.GetString(File.ReadAllBytes(pdf));
        var names = Regex.Matches(text, @"/BaseFont\s*/([A-Za-z0-9+#\-,]+)")
            .Select(m => Regex.Replace(m.Groups[1].Value, "^[A-Z]{6}\\+", ""))
            .Distinct().OrderBy(n => n, StringComparer.Ordinal);
        return string.Join(" ", names);
    }

    [Fact]
    public void OneBadFileDoesNotStopTheRest_ButTheRunStillFails()
    {
        // ⚠️ THE ONE WAY A BATCH DIFFERS FROM N RUNS, and it is deliberate: a sweep over a
        // live corpus meets books that do not parse, and stopping at the first would waste
        // the other nine hundred. The failure comes back in the exit code instead.
        string good = Book("good");
        string bad = Path.Combine(_dir, "bad.lys");
        File.WriteAllText(bad, "part m { clef treble\n");   // unclosed
        string after = Book("after");

        string outDir = Folder("out");
        string list = List($"{good}\t{outDir}", $"{bad}\t{outDir}", $"{after}\t{outDir}");
        var r = Lysc("svg", "-n", "--batch", list);

        Assert.NotEqual(0, r.Exit);
        Assert.Contains("3 file(s), 1 failed", r.Stdout);
        Assert.True(new FileInfo(Out(outDir, after, ".svg")).Length > 0,
            "the file after the bad one must still have been written");
    }

    [Fact]
    public void CommentsAndBlankLinesAreSkipped_AndATabNamesTheFolder()
    {
        string a = Book("a"), b = Book("b");
        string folder = Folder("named");
        string list = List(
            "# a comment", "", $"   {a}   ", $"{b}\t{folder}", "   ");

        var r = Lysc("svg", "-n", "--batch", list);

        Assert.Equal(0, r.Exit);
        Assert.Contains("2 file(s), 0 failed", r.Stdout);
        Assert.True(File.Exists(Path.ChangeExtension(a, ".svg")), "the untabbed line writes beside its book");
        Assert.True(File.Exists(Out(folder, b, ".svg")), "the tabbed line writes into the folder after the tab");
    }

    [Fact]
    public void ABatchWideFolder_TakesEveryBook_AndALinesOwnFolderWins()
    {
        string a = Book("wide-a"), b = Book("wide-b");
        string wide = Folder("wide"), own = Folder("own");
        var r = Lysc("svg", "-n", "-d", wide, "--batch", List(a, $"{b}\t{own}"));

        Assert.Equal(0, r.Exit);
        Assert.True(File.Exists(Out(wide, a, ".svg")));
        Assert.True(File.Exists(Out(own, b, ".svg")));
        Assert.False(File.Exists(Out(wide, b, ".svg")));
    }

    [Fact]
    public void AnOutputNameIsRefused_WithTheFolderToUseInstead()
    {
        // Output names are fixed (the book's stem, plus the score's alias); a batch chooses
        // folders only — and an old script's -o is told so rather than "Unknown option".
        string list = List(Book("only"));
        var r = Lysc("svg", "--batch", list, "-o", Path.Combine(_dir, "one.svg"));

        Assert.NotEqual(0, r.Exit);
        Assert.Contains("-o/--output is gone", r.Stderr);
        Assert.Contains("-d <folder>", r.Stderr);
    }

    [Fact]
    public void AParallelBatchWritesExactlyWhatASequentialOneWrites()
    {
        // ★ THE CLAIM FOR --parallel, and the only one worth making: engraving several
        // documents at once in ONE process must not change a single byte. Every static on
        // the render path is a way for it to, which is why this compares bytes over enough
        // books to keep several workers genuinely overlapping rather than finishing in turn.
        string[] books = [.. Enumerable.Range(0, 12).Select(i => Book($"p{i}"))];

        string seq = Folder("seq"), parallel = Folder("par");
        Assert.Equal(0, Lysc("svg", "-n", "-d", seq, "--batch", List(books)).Exit);

        var par = Lysc("svg", "-n", "-d", parallel, "--batch", List(books), "-j", "4");
        Assert.Equal(0, par.Exit);

        foreach (string b in books)
            Assert.Equal(File.ReadAllBytes(Out(seq, b, ".svg")), File.ReadAllBytes(Out(parallel, b, ".svg")));

        // ...and every file got exactly one progress line, none lost and none doubled.
        // ⚠️ THE PROGRESS LINES, not every mention of the name: a `Created:` line may name
        // the book too, so counting bare occurrences says nothing.
        Assert.Contains("12 file(s), 0 failed", par.Stdout);
        Assert.Contains("4 at a time", par.Stdout);
        foreach (string b in books)
            // ⚠️ `\r?$`: the CLI writes CRLF on Windows, and Multiline's `$` matches before
            // the `\n` — with the `\r` still unconsumed, a bare `$` never matches.
            Assert.Equal(1, Regex.Matches(
                par.Stdout, @"^\[\d+/12\] " + Regex.Escape(b) + @"\r?$",
                RegexOptions.Multiline).Count);
    }

    [Fact]
    public void ParallelIsRefusedForPdf_WithTheReason()
    {
        // ⚠️ THE ONE COMMAND THAT CANNOT. The font resolver is a process singleton each
        // document re-points at its own faces; sequentially that is correct and
        // ABatchedPdfDoesNotInheritThePreviousBooksFonts proves it, concurrently it is not.
        // Refusing beats shipping a mode that is wrong only under load — the worst kind.
        string list = List(Book("one"), Book("two"));
        var r = Lysc("pdf", "--batch", list, "-j", "2");

        Assert.NotEqual(0, r.Exit);
        Assert.Contains("cannot run in parallel", r.Stderr);
        Assert.Contains("font resolver", r.Stderr);
    }

    [Fact]
    public void BatchIsAvailableToEveryCommand_NotJustSvg()
    {
        // The flag is taken before dispatch, so it is not a per-command feature; `check`
        // writes nothing at all and still batches.
        string list = List(Book("x"), Book("y"));
        var r = Lysc("check", "--batch", list);

        Assert.Equal(0, r.Exit);
        Assert.Contains("2 file(s), 0 failed", r.Stdout);
    }
}
