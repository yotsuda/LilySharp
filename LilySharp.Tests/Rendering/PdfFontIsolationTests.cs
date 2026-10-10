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

using System.Text.RegularExpressions;
using LilySharp.Core.Rendering;
using LilySharp.Core.Rendering.Pdf;
using Xunit;

namespace LilySharp.Tests.Rendering;

/// <summary>
/// Two PDF documents in ONE process, the SAME face name, OPPOSITE answers — each page
/// carries its own.
/// </summary>
/// <remarks>
/// PdfSharpCore keeps one font resolver per process AND remembers what it answered for a
/// family name for the life of the process (<c>FontFactory</c>'s cache, keyed on the
/// requested family and style). Until session 885 a page asked for the face by its NAME and
/// left the answer — own program, or bundled stand-in — to a per-document field on that
/// shared resolver, so the first document to ask froze its answer for every later one.
/// MEASURED 2026-10-10 (Lab sessions/p885/probe2.log): <c>title "Georgia" embedded</c> then
/// <c>title "Georgia"</c> embedded Georgia on BOTH pages; <c>title "Verdana"</c> then
/// <c>title "Verdana" embedded</c> drew Schola on both. The page now spells the answer in
/// what it asks for (<c>LysEmbed:&lt;name&gt;</c> / the generic family), and this is the net.
/// <para>
/// ⚠️ DETERMINISTIC BY CONSTRUCTION, not by installed fonts. The only face whose own program
/// every machine can embed is a music font's bundled companion (Petaluma Script), and a
/// companion always embeds — so the OPPOSITE answer for it is handed to the second document
/// directly (<see cref="PdfDocumentContext.TextFaces"/>), which is the door that property
/// exists for. The drawing side cannot tell a hand-built answer from a loaded one; what is
/// under test is that the answer, not the name, reaches PdfSharpCore.
/// </para>
/// <para>
/// ⚠️ ORDER-INDEPENDENT ON PURPOSE: the own-program page is drawn before AND after the
/// stand-in page, so whichever request PdfSharpCore cached first, the other still comes
/// out right. Under the poison (ask for the bare name again) the stand-in page carries
/// Petaluma Script.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class PdfFontIsolationTests
{
    private const string Companion = "Petaluma Script";

    [Fact]
    public void OppositeAnswersForOneName_EachPageCarriesItsOwn()
    {
        // The plan names the companion for the title, so the page MEASURES it (the named
        // path); the answer the page draws with is the document's.
        var plan = new TextFontPlan.Builder().Role(TextRole.Title, [Companion]).Build();
        Assert.False(plan.Resolve(TextRole.Title).IsBundled, "the probe plan stopped naming a face");

        var standIn = new DocumentTextFaces(new Dictionary<string, DocumentTextFaces.Face>(StringComparer.OrdinalIgnoreCase)
        {
            [Companion] = new DocumentTextFaces.Face(Embedded: false, Sans: false),
        });

        var own = BaseFonts(plan, faces: null);
        var standsIn = BaseFonts(plan, faces: standIn);
        var ownAgain = BaseFonts(plan, faces: null);

        Assert.Contains(own, f => f.Contains("Petaluma", StringComparison.Ordinal));
        Assert.DoesNotContain(own, f => f.Contains("Schola", StringComparison.Ordinal));
        Assert.Contains(standsIn, f => f.Contains("Schola", StringComparison.Ordinal));
        Assert.DoesNotContain(standsIn, f => f.Contains("Petaluma", StringComparison.Ordinal));
        Assert.Equal(own, ownAgain);
    }

    /// <summary>The <c>/BaseFont</c> names a one-string page ends up carrying (a subset tag
    /// and <c>#20</c> for a space in front of and inside the family word, hence the
    /// substring tests above).</summary>
    private static IReadOnlyList<string> BaseFonts(TextFontPlan plan, DocumentTextFaces? faces)
    {
        using var doc = new PdfDocumentContext(new PdfDocumentOptions { PointsPerSpace = 6.0, AutoSizePages = true });
        doc.Fonts = plan;
        if (faces != null)
            doc.TextFaces = faces;
        var gc = doc.BeginPage(40, 8);
        gc.DrawText("Probe", 1.0, 5.0, 2.0, TextRole.Title);
        doc.EndPage();
        doc.Dispose();

        string raw = System.Text.Encoding.Latin1.GetString(doc.GetBytes());
        var names = Regex.Matches(raw, @"/BaseFont\s*/([A-Za-z0-9+\-,#]+)")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.NotEmpty(names);
        return names;
    }
}
