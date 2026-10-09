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
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LilySharp.Core.Pdf;
using LilySharp.Core.Syntax;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The same score is the same PDF bytes: the subset font tags and the document ID are
/// deterministic always, the creation date under <c>SOURCE_DATE_EPOCH</c>. Before
/// 2026-10-03 two PDFs of one score differed in 180 bytes (PdfReproducibility says where),
/// so no sweep could compare them and the export doors compared them masked.
/// </summary>
[Collection("SourceDateEpoch")]
public class PdfReproducibilityTests
{
    private const string Book = """
        title "Reproducible"
        composer "Lily#"
        time 4/4
        key g major
        part m { clef treble }
        section A { m { g'4@mf a' b' c'' | d''2.@text("dolce") r4 | } }
        form { A }
        score { staff m }
        """;

    private static byte[] Pdf() => PdfGenerator.Generate(SyntaxTree.Parse(Book));

    private static string Text(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    [Fact]
    public void TwoWritings_UnderSourceDateEpoch_AreOneFile()
    {
        using var _ = SourceDateEpoch.Set("1700000000");
        Assert.Equal(Pdf(), Pdf());
    }

    [Fact]
    public void WithoutTheEpoch_OnlyTheClockDiffers()
    {
        using var _ = SourceDateEpoch.Set(null);
        string a = Text(Pdf()), b = Text(Pdf());
        var date = new Regex(@"/CreationDate \(D:[^)]*\)");
        Assert.Equal(date.Replace(a, "X"), date.Replace(b, "X"));
    }

    [Fact]
    public void TheSubsetTags_AreLetters_AndTheIdIsTheFilesHash()
    {
        using var _ = SourceDateEpoch.Set("1700000000");
        string text = Text(Pdf());
        var tags = Regex.Matches(text, @"/([A-Z]{6})\+").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.NotEmpty(tags);
        // Emmentaler and the two text faces: distinct tags, one per subset.
        Assert.Equal(tags.Count, tags.Distinct().Count());
        var id = Regex.Match(text, @"/ID\s*\[<([0-9A-F]{32})><([0-9A-F]{32})>\]");
        Assert.True(id.Success, "the trailer carries a document ID");
        Assert.Equal(id.Groups[1].Value, id.Groups[2].Value);
        Assert.Contains("/CreationDate (D:20231114", text); // 1700000000 = 2023-11-14T22:13:20Z
    }
}

/// <summary>Sets <c>SOURCE_DATE_EPOCH</c> for the process while in scope; the tests that
/// touch it share one collection so they never race.</summary>
internal sealed class SourceDateEpoch : IDisposable
{
    private readonly string? _before = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");

    private SourceDateEpoch(string? value) => Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", value);

    public static SourceDateEpoch Set(string? value) => new(value);

    public void Dispose() => Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", _before);
}

[CollectionDefinition("SourceDateEpoch")]
public class SourceDateEpochCollection { }
