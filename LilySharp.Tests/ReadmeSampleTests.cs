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
/// The README opens with <c>samples/harbour-lights.lys</c> playing and the source under it.
/// The sample itself is compiled by every sample sweep; this keeps the README's copy of the
/// source from drifting away from the file.
/// </summary>
/// <remarks>
/// The video is a GitHub attachment, not a file here, so nothing can check it: remake it
/// (the preview's playback events and highlight, frame by frame — Lab sessions/p903/readme-audio)
/// and upload it again when the sample changes or the engraving visibly moves.
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

    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd();
}
