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
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LilySharp.Core.Rendering.Pdf;

/// <summary>
/// Makes a saved PDF the same bytes for the same score. PdfSharpCore writes three things
/// no two runs share: the clock as <c>/CreationDate</c>, a <c>Guid</c>-made document
/// <c>/ID</c> in the trailer, and a <c>Guid</c>-made six-letter tag on every embedded
/// subset font's name (<c>/RFYOHX+Emmentaler-20</c>). Measured 2026-10-03: the same book
/// written twice by one <c>lysc</c> differed in 180 bytes over 28 places, all of them
/// these three — and so the PDF was the one output the corpus sweeps could not compare
/// and the one the export doors could only compare masked (ScoreExportTests).
/// </summary>
/// <remarks>
/// The clock is taken from <c>SOURCE_DATE_EPOCH</c> when it is set (the reproducible-builds
/// convention: seconds since 1970, UTC), else the real clock stays — a PDF's creation date
/// means something to its reader. The tags and the ID carry no meaning, so they are made
/// deterministic always: a tag from the subset's name and its order of appearance, the ID
/// from a hash of the file. Both rewrites keep every byte count, so the cross-reference
/// table's offsets stay true.
/// LILYSHARP-OWN: the convention is reproducible-builds.org's, not LilyPond's (its PDFs
/// carry Cairo's clock and no override); the subset-tag and ID rules are Lily#'s.
/// </remarks>
internal static class PdfReproducibility
{
    /// <summary>The creation date the saved document carries: <c>SOURCE_DATE_EPOCH</c>
    /// when set and well-formed, else null (the clock).</summary>
    public static DateTime? CreationDate()
    {
        string? epoch = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        if (!string.IsNullOrEmpty(epoch) && long.TryParse(epoch, out long seconds))
            return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        return null;
    }

    // A subset font name: PdfSharpCore's six uppercase letters, '+', the face's name
    // (spaces written as #20 by then, so the name runs to the next delimiter).
    private static readonly Regex SubsetTag = new(@"/([A-Z]{6})\+([^\s/\[\]<>()]+)", RegexOptions.Compiled);
    // The trailer's document identifier: two 32-digit hex strings (PdfSharpCore writes one Guid twice).
    private static readonly Regex DocumentId = new(@"/ID\s*\[<([0-9A-Fa-f]{32})><([0-9A-Fa-f]{32})>\]", RegexOptions.Compiled);
    // The creation date's digits — left out of the identifier's hash, so that without
    // SOURCE_DATE_EPOCH two writings differ in the date ALONE, not in the date and an
    // identifier that follows it.
    private static readonly Regex CreationDateDigits = new(@"/CreationDate \(D:([^)]*)\)", RegexOptions.Compiled);

    /// <summary>Rewrites the subset font tags and the document ID of a saved PDF in
    /// place (same length everywhere), and returns the same array.</summary>
    public static byte[] Apply(byte[] pdf)
    {
        // Latin-1 maps every byte to one char and back, so offsets are byte offsets.
        var text = Encoding.Latin1.GetString(pdf);

        // One deterministic tag per original tag, in order of first appearance: two
        // subsets of one face (regular and a synthesised bold) keep distinct names.
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in SubsetTag.Matches(text))
        {
            string original = m.Groups[1].Value;
            if (tags.ContainsKey(original))
                continue;
            tags[original] = TagFor(m.Groups[2].Value, tags.Count);
        }
        foreach (var (original, replacement) in tags)
            Overwrite(pdf, text, "/" + original + "+", "/" + replacement + "+");

        var id = DocumentId.Match(text);
        if (id.Success)
        {
            // Hash the file with the identifier and the clock blanked, then write the hash
            // as the identifier; the clock goes back as it was.
            var zero = new string('0', 32);
            Overwrite(pdf, text, id.Groups[1].Index, zero);
            Overwrite(pdf, text, id.Groups[2].Index, zero);
            var date = CreationDateDigits.Match(text);
            if (date.Success)
                Overwrite(pdf, text, date.Groups[1].Index, new string('0', date.Groups[1].Length));
            string hash = Convert.ToHexString(MD5.HashData(pdf));
            if (date.Success)
                Overwrite(pdf, text, date.Groups[1].Index, date.Groups[1].Value);
            Overwrite(pdf, text, id.Groups[1].Index, hash);
            Overwrite(pdf, text, id.Groups[2].Index, hash);
        }
        return pdf;
    }

    // Six letters A–Z from a hash of the face's name and the subset's ordinal.
    private static string TagFor(string faceName, int ordinal)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(faceName + "\n" + ordinal));
        var tag = new char[6];
        for (int i = 0; i < 6; i++)
            tag[i] = (char)('A' + hash[i] % 26);
        return new string(tag);
    }

    // Every occurrence of `from` in the text becomes `to` in the bytes (same length).
    private static void Overwrite(byte[] pdf, string text, string from, string to)
    {
        for (int at = text.IndexOf(from, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(from, at + from.Length, StringComparison.Ordinal))
            Overwrite(pdf, text, at, to);
    }

    private static void Overwrite(byte[] pdf, string text, int at, string to)
    {
        for (int i = 0; i < to.Length; i++)
            pdf[at + i] = (byte)to[i];
    }
}
