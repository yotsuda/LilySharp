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
using SkiaSharp;

namespace LilySharp.Core.Rendering;

/// <summary>
/// Classifies whether a font may be embedded into an exported PDF, and — separately —
/// pulls its raw bytes for the embedder. Embedding a font can be legally restricted:
/// the OpenType OS/2 <c>fsType</c> field can forbid it outright, and even when it does
/// not, only fonts under a clearly-redistributable license (OFL / Apache / etc.) are
/// auto-cleared; everything else is flagged as license-unverified ("gray").
/// </summary>
public static class FontEmbedInfo
{
    /// <summary>How freely a resolved font may be embedded into a PDF.</summary>
    public enum FontEmbedClass
    {
        /// <summary>Embedding is clearly allowed (a known libre family, fsType permits it).</summary>
        Free,
        /// <summary>fsType permits embedding but the license is unverified — proceed with care.</summary>
        Gray,
        /// <summary>The font's fsType forbids embedding.</summary>
        Forbidden,
        /// <summary>The family is not installed on this system.</summary>
        NotFound,
    }

    // ASCII "OS/2" as a big-endian uint, the SkiaSharp table tag for the OS/2 table.
    private const uint Os2Tag = 0x4F532F32u;

    private static readonly Dictionary<string, FontEmbedClass> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Classifies the embeddability of the font resolved from <paramref name="family"/>.
    /// Results are cached (case-insensitive) since a score can name the same font often.
    /// </summary>
    public static FontEmbedClass Classify(string family)
    {
        if (string.IsNullOrEmpty(family))
            return FontEmbedClass.NotFound;

        if (Cache.TryGetValue(family, out var cached))
            return cached;

        var result = ClassifyUncached(family);
        Cache[family] = result;
        return result;
    }

    private static FontEmbedClass ClassifyUncached(string family)
    {
        var typeface = SKTypeface.FromFamilyName(family);
        // Skia never returns null for an unknown family — it hands back a default face.
        // Detect "not found" by checking the resolved family name against the request.
        if (typeface == null || !typeface.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase))
            return FontEmbedClass.NotFound;

        ushort fsType = 0;
        var bytes = typeface.GetTableData(Os2Tag);
        // fsType is a big-endian UInt16 at byte offset 8 of the OS/2 table.
        if (bytes != null && bytes.Length >= 10)
            fsType = (ushort)((bytes[8] << 8) | bytes[9]);

        return ClassifyFsTypeAndName(fsType, family);
    }

    /// <summary>
    /// The pure decision, split out so it can be unit-tested without a real installed
    /// font: a restricted-embedding fsType always wins; otherwise a known libre family
    /// clears, and everything else is gray (license unverified).
    /// </summary>
    internal static FontEmbedClass ClassifyFsTypeAndName(ushort fsType, string family)
    {
        // Bit 1 (0x0002) is RESTRICTED_LICENSE_EMBEDDING — the font forbids embedding.
        if ((fsType & 0x0002) != 0)
            return FontEmbedClass.Forbidden;
        if (IsKnownLibreFamily(family))
            return FontEmbedClass.Free;
        return FontEmbedClass.Gray;
    }

    // Known redistributable/embeddable family markers (OFL / Apache / other libre).
    // Matched as case-insensitive substrings so face variants ("Noto Serif CJK JP",
    // "BIZ UDMincho") are covered by their family stem.
    private static readonly string[] LibreMarkers =
    {
        "Noto", "Source Han", "Source Serif", "Source Sans", "Source Code",
        "IPAex", "IPAGothic", "IPAMincho", "IPAPGothic", "IPAPMincho",
        "Liberation", "DejaVu", "Ubuntu", "Fira", "Open Sans", "Roboto",
        "M PLUS", "Kosugi", "Sawarabi", "BIZ UD", "Zen ", "Klee",
        "Shippori", "Mochiy",
    };

    /// <summary>
    /// True if <paramref name="family"/> contains (case-insensitively) any known
    /// libre-font family marker. Anything else is treated as license-unverified (gray).
    /// </summary>
    internal static bool IsKnownLibreFamily(string family)
    {
        if (string.IsNullOrEmpty(family))
            return false;
        foreach (var marker in LibreMarkers)
            if (family.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// Reads the raw font file bytes for <paramref name="family"/>, for the PDF-embedding
    /// step. Returns null if the family is not installed or its bytes cannot be read.
    /// </summary>
    public static byte[]? TryGetFontBytes(string family)
    {
        if (string.IsNullOrEmpty(family))
            return null;
        var tf = SKTypeface.FromFamilyName(family);
        if (tf == null || !tf.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase))
            return null;
        return TryGetFontBytes(tf);
    }

    /// <summary>
    /// Reads the raw font file bytes behind an ALREADY-RESOLVED typeface — the per-codepoint
    /// face <c>SKFontManager.MatchCharacter</c> hands back for a character the bundled Latin
    /// faces cannot draw. The family overload cannot serve that: matching by name would look
    /// the face up a second time and can land on a different one.
    /// </summary>
    /// <remarks>
    /// ⚠️ A face inside a COLLECTION (.ttc — Yu Gothic, Meiryo and MS Gothic all ship that
    /// way on Japanese Windows) streams as the WHOLE collection, with the face's index in it
    /// as <see cref="SKTypeface.OpenStream"/>'s out parameter. An embedder wants one font
    /// program and has nowhere to put an index, so the face is lifted out here
    /// (<see cref="SfntExtractor"/>) and the caller never sees a container.
    /// </remarks>
    public static byte[]? TryGetFontBytes(SKTypeface typeface)
    {
        if (typeface == null)
            return null;
        try
        {
            using var s = typeface.OpenStream(out int faceIndex);
            if (s == null)
                return null;
            using var ms = new MemoryStream();
            var chunk = new byte[64 * 1024];
            int read;
            while ((read = s.Read(chunk, chunk.Length)) > 0)
                ms.Write(chunk, 0, read);
            var bytes = ms.ToArray();
            if (bytes.Length == 0)
                return null;
            return SfntExtractor.IsCollection(bytes)
                ? SfntExtractor.ExtractFont(bytes, faceIndex)
                : bytes;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Classifies an already-resolved typeface, without a name lookup — the fsType half of
    /// <see cref="Classify(string)"/> applied to the face the caller already holds.
    /// </summary>
    public static FontEmbedClass Classify(SKTypeface typeface)
    {
        if (typeface == null)
            return FontEmbedClass.NotFound;
        ushort fsType = 0;
        var bytes = typeface.GetTableData(Os2Tag);
        if (bytes != null && bytes.Length >= 10)
            fsType = (ushort)((bytes[8] << 8) | bytes[9]);
        return ClassifyFsTypeAndName(fsType, typeface.FamilyName);
    }
}
