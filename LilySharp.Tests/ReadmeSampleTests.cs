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
using System.Text.RegularExpressions;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// The README opens with a picture of <c>samples/harbour-lights.lys</c> and the source under
/// it. The sample itself is compiled by every sample sweep; this keeps the README's copy of
/// the source from drifting away from the file, and the picture from going missing.
/// </summary>
/// <remarks>
/// The picture is not compared: engraving improvements change its pixels. Re-render it with
/// <c>lysc png samples/harbour-lights.lys -d docs/images</c> (Release build)
/// when the sample changes or the engraving visibly moves.
/// </remarks>
public class ReadmeSampleTests
{
    [Fact]
    public void TheReadmeShowsItsSampleExactlyAsTheFileIsWritten()
    {
        var root = CollectResumeTests.FindRepoRoot();
        string readme = File.ReadAllText(Path.Combine(root, "README.md"));
        string sample = File.ReadAllText(Path.Combine(root, "samples", "harbour-lights.lys"));

        var m = Regex.Match(readme,
            @"<!-- README-SAMPLE:harbour-lights[^>]*-->\r?\n```lilysharp\r?\n(.*?)```", RegexOptions.Singleline);
        Assert.True(m.Success, "README.md lost its README-SAMPLE:harbour-lights block");
        Assert.Equal(Normalize(sample), Normalize(m.Groups[1].Value));

        Assert.Contains("docs/images/harbour-lights.png", readme, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "docs", "images", "harbour-lights.png")),
            "docs/images/harbour-lights.png is missing — render it with lysc png");
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd();
}
