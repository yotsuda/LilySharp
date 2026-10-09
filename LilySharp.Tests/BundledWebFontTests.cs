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
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace LilySharp.Tests;

/// <summary>
/// Every bundled <c>.woff2</c> keeps the layout tables of the <c>.otf</c> beside it. The
/// engine measures text with the OTF (HarfBuzz applies its kerning and substitutions) while
/// the preview and an SVG's embedded face draw it with the WOFF2 — so a WOFF2 without GPOS
/// draws every string unkerned, wider than the layout reserved.
/// </summary>
/// <remarks>
/// Found 2026-10-09: <c>PetalumaScript.woff2</c> was a TrueType conversion with no GPOS, GSUB
/// or kern, and a Petaluma section label "Introduction" overran its box by 1.2 staff spaces
/// in the preview (measured 14.51 kerned, drawn 15.69). The WOFF2s are now the OTFs wrapped
/// unchanged (fontTools, flavor woff2 — which drops only DSIG, as the format requires).
/// </remarks>
[Trait("Category", "Unit")]
public class BundledWebFontTests
{
    private static readonly string[] LayoutTables = ["GDEF", "GPOS", "GSUB", "kern"];

    public static IEnumerable<object[]> Pairs()
    {
        foreach (var woff2 in Directory.GetFiles(FontsDir(), "*.woff2").Order(StringComparer.Ordinal))
            if (File.Exists(Path.ChangeExtension(woff2, ".otf")))
                yield return [Path.GetFileName(woff2)];
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void TheWebFont_KeepsTheOtfsLayoutTables(string woff2Name)
    {
        string woff2 = Path.Combine(FontsDir(), woff2Name);
        var otfTables = SfntTables(File.ReadAllBytes(Path.ChangeExtension(woff2, ".otf")));
        var webTables = Woff2Tables(File.ReadAllBytes(woff2));
        var missing = LayoutTables.Where(t => otfTables.Contains(t) && !webTables.Contains(t)).ToArray();
        Assert.True(missing.Length == 0,
            $"{woff2Name} lacks {string.Join(", ", missing)} that its .otf has — the preview would draw "
            + "text the layout did not measure. Rewrap the .otf: fontTools TTFont(otf); flavor='woff2'; save.");
    }

    private static string FontsDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "LilySharp.Core", "Fonts");
            if (Directory.Exists(candidate))
                return candidate;
        }
        throw new DirectoryNotFoundException("LilySharp.Core/Fonts");
    }

    /// <summary>The table tags of an OpenType file (the sfnt table directory).</summary>
    private static HashSet<string> SfntTables(byte[] b)
    {
        int count = (b[4] << 8) | b[5];
        var tags = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
            tags.Add(Encoding.ASCII.GetString(b, 12 + 16 * i, 4));
        return tags;
    }

    // The WOFF2 known-tag table (W3C WOFF2 §5.1), indexed by a directory entry's low 6 bits.
    private static readonly string[] KnownTags =
    [
        "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "cvt ", "fpgm", "glyf",
        "loca", "prep", "CFF ", "VORG", "EBDT", "EBLC", "gasp", "hdmx", "kern", "LTSH", "PCLT",
        "VDMX", "vhea", "vmtx", "BASE", "GDEF", "GPOS", "GSUB", "EBSC", "JSTF", "MATH", "CBDT",
        "CBLC", "COLR", "CPAL", "SVG ", "sbix", "acnt", "avar", "bdat", "bloc", "bsln", "cvar",
        "fdsc", "feat", "fmtx", "fvar", "gvar", "hsty", "just", "lcar", "mort", "morx", "opbd",
        "prop", "trak", "Zapf", "Silf", "Glat", "Gloc", "Feat", "Sill",
    ];

    /// <summary>The table tags of a WOFF2 file (its table directory, W3C WOFF2 §5).</summary>
    private static HashSet<string> Woff2Tables(byte[] b)
    {
        Assert.Equal("wOF2", Encoding.ASCII.GetString(b, 0, 4));
        int count = (b[12] << 8) | b[13];
        int pos = 48;   // the fixed WOFF2 header
        var tags = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            byte flags = b[pos++];
            string tag;
            if ((flags & 0x3F) == 0x3F)
            {
                tag = Encoding.ASCII.GetString(b, pos, 4);
                pos += 4;
            }
            else
                tag = KnownTags[flags & 0x3F];
            tags.Add(tag);
            int version = flags >> 6;
            ReadBase128(b, ref pos);   // origLength
            bool transformed = tag is "glyf" or "loca" ? version == 0 : version != 0;
            if (transformed)
                ReadBase128(b, ref pos);   // transformLength
        }
        return tags;
    }

    private static uint ReadBase128(byte[] b, ref int pos)
    {
        uint value = 0;
        for (int i = 0; i < 5; i++)
        {
            byte d = b[pos++];
            value = (value << 7) | (uint)(d & 0x7F);
            if ((d & 0x80) == 0)
                return value;
        }
        throw new InvalidDataException("UIntBase128 longer than five bytes");
    }
}
