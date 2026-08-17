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

namespace LilySharp.Core.Rendering;

/// <summary>
/// Lifts ONE face out of a TrueType Collection as a standalone SFNT font program.
/// </summary>
/// <remarks>
/// ⚠️ WITHOUT THIS THERE IS NO CJK IN THE PDF ON WINDOWS. The per-codepoint fallback the
/// renderers resolve for Japanese text lands on Yu Gothic UI on a stock Japanese Windows,
/// and Yu Gothic, Meiryo and MS Gothic all ship as <c>.ttc</c> COLLECTIONS — several faces
/// sharing one file, which <see cref="SkiaSharp.SKTypeface.OpenStream(out int)"/> hands back whole
/// (14.7 MB, face index 1, measured 2026-08-11). A PDF embedder wants a single font program
/// and has no index to pass, so the collection has to be split before it can be embedded.
/// <para>
/// The split is a rewrite of the table directory and nothing else: a collection's per-face
/// Offset Table already lists that face's tables, but its <c>offset</c> fields are from the
/// start of the FILE. Copying the tables out and repointing the directory at their new
/// positions is the whole operation — table CONTENT is untouched, and tables shared between
/// faces are simply copied for the one face asked for.
/// </para>
/// <para>
/// ⚠️ <c>head.checkSumAdjustment</c> is left as the collection wrote it, so it no longer
/// matches the extracted file. Nothing in this pipeline verifies it (PdfSharpCore subsets
/// the face and writes its own tables), and recomputing it would mean re-checksumming every
/// table to produce a number no reader here reads.
/// </para>
/// </remarks>
internal static class SfntExtractor
{
    // 'ttcf' — the tag a TrueType Collection opens with, where a single font has its
    // sfntVersion ('OTTO', or 0x00010000 for TrueType outlines).
    private const uint TtcTag = 0x74746366u;

    /// <summary>True when <paramref name="bytes"/> is a collection rather than one font.</summary>
    public static bool IsCollection(byte[]? bytes)
        => bytes != null && bytes.Length >= 4 && ReadU32(bytes, 0) == TtcTag;

    /// <summary>
    /// The face at <paramref name="index"/> as a standalone font program, or null when the
    /// collection is malformed or the index is out of range.
    /// </summary>
    public static byte[]? ExtractFont(byte[] ttc, int index)
    {
        // TTC header: tag, majorVersion, minorVersion, numFonts, then one offset per font.
        if (!IsCollection(ttc) || ttc.Length < 12)
            return null;
        uint numFonts = ReadU32(ttc, 8);
        if (index < 0 || index >= numFonts)
            return null;
        int dirPos = 12 + index * 4;
        if (dirPos + 4 > ttc.Length)
            return null;

        // This face's Offset Table: sfntVersion, numTables, then the binary-search hints.
        long tableStart = ReadU32(ttc, dirPos);
        if (tableStart + 12 > ttc.Length)
            return null;
        uint sfntVersion = ReadU32(ttc, (int)tableStart);
        int numTables = ReadU16(ttc, (int)tableStart + 4);
        if (numTables == 0)
            return null;
        int recPos = (int)tableStart + 12;
        if (recPos + numTables * 16 > ttc.Length)
            return null;

        var tags = new uint[numTables];
        var checksums = new uint[numTables];
        var srcOffsets = new uint[numTables];
        var lengths = new uint[numTables];
        for (int i = 0; i < numTables; i++)
        {
            int p = recPos + i * 16;
            tags[i] = ReadU32(ttc, p);
            checksums[i] = ReadU32(ttc, p + 4);
            srcOffsets[i] = ReadU32(ttc, p + 8);
            lengths[i] = ReadU32(ttc, p + 12);
            if ((long)srcOffsets[i] + lengths[i] > ttc.Length)
                return null;
        }

        // Table data follows the directory, each table starting on a 4-byte boundary.
        long total = 12 + (long)numTables * 16;
        var dstOffsets = new uint[numTables];
        for (int i = 0; i < numTables; i++)
        {
            dstOffsets[i] = (uint)total;
            total += (lengths[i] + 3) & ~3u;
        }
        if (total > int.MaxValue)
            return null;

        var result = new byte[total];
        WriteU32(result, 0, sfntVersion);
        WriteU16(result, 4, (ushort)numTables);
        // searchRange / entrySelector / rangeShift are derived from numTables, so they are
        // recomputed rather than copied — the collection's are for the same count, but a
        // reader that trusts them deserves ones that match the directory it is handed.
        int entrySelector = 0;
        while (1 << (entrySelector + 1) <= numTables)
            entrySelector++;
        int searchRange = 16 * (1 << entrySelector);
        WriteU16(result, 6, (ushort)searchRange);
        WriteU16(result, 8, (ushort)entrySelector);
        WriteU16(result, 10, (ushort)(numTables * 16 - searchRange));

        for (int i = 0; i < numTables; i++)
        {
            int p = 12 + i * 16;
            WriteU32(result, p, tags[i]);
            WriteU32(result, p + 4, checksums[i]);
            WriteU32(result, p + 8, dstOffsets[i]);
            WriteU32(result, p + 12, lengths[i]);
            Array.Copy(ttc, srcOffsets[i], result, dstOffsets[i], lengths[i]);
        }
        return result;
    }

    private static uint ReadU32(byte[] b, int i)
        => ((uint)b[i] << 24) | ((uint)b[i + 1] << 16) | ((uint)b[i + 2] << 8) | b[i + 3];

    private static ushort ReadU16(byte[] b, int i) => (ushort)((b[i] << 8) | b[i + 1]);

    private static void WriteU32(byte[] b, int i, uint v)
    {
        b[i] = (byte)(v >> 24);
        b[i + 1] = (byte)(v >> 16);
        b[i + 2] = (byte)(v >> 8);
        b[i + 3] = (byte)v;
    }

    private static void WriteU16(byte[] b, int i, ushort v)
    {
        b[i] = (byte)(v >> 8);
        b[i + 1] = (byte)v;
    }
}
