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
using System.Runtime.InteropServices;

namespace LilySharp.Core.Rendering;

/// <summary>
/// A font file cut down to the characters a document uses, through HarfBuzz's
/// <c>hb-subset</c>: what an SVG embeds so that its words are set in the face the layout
/// measured, on a machine that does not have that face.
/// </summary>
/// <remarks>
/// MEASURED 2026-11-19 on the bundled regular serif (texgyreschola-regular.otf, 140,596
/// bytes, 1090 glyphs): the 36 glyphs of an eight-bar song's words, tempo and staff names
/// come out as 7,024 bytes — an OpenType/CFF font still (<c>OTTO</c>), its kerning kept for
/// the glyphs that stay, which a browser loads from a <c>data:</c> URL as it loads the whole
/// file.
/// <para>
/// ⚠️ RAW P/INVOKE, as <see cref="HarfBuzzOutline"/> is and for its reason: HarfBuzzSharp
/// 8.3.1 binds no subsetting, and the native library it ships exports the whole
/// <c>hb_subset_*</c> family on every platform it is built for (checked 2026-11-19 over the
/// seventeen native assets of the package: each names <c>hb_subset_or_fail</c> and
/// <c>hb_subset_input_unicode_set</c>). No new package.
/// </para>
/// <para>
/// Everything made here is destroyed here, and nothing is shared: unlike the shaping font,
/// a call may run beside another.
/// </para>
/// </remarks>
internal static class HarfBuzzSubset
{
    // The native library HarfBuzzSharp itself binds — same file, same load, no new asset.
    private const string Lib = "libHarfBuzzSharp";

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr hb_blob_create(IntPtr data, uint length, int mode, IntPtr userData, IntPtr destroy);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_blob_destroy(IntPtr blob);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr hb_blob_get_data(IntPtr blob, out uint length);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr hb_face_create(IntPtr blob, uint index);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_face_destroy(IntPtr face);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr hb_face_reference_blob(IntPtr face);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr hb_subset_input_create_or_fail();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_subset_input_destroy(IntPtr input);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr hb_subset_input_unicode_set(IntPtr input);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_set_add(IntPtr set, uint codepoint);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr hb_subset_or_fail(IntPtr face, IntPtr input);

    /// <summary>HB_MEMORY_MODE_READONLY: the bytes are ours, pinned for the call, and not written.</summary>
    private const int ReadOnly = 1;

    /// <summary>
    /// <paramref name="font"/> with the glyphs of <paramref name="codepoints"/> alone (and
    /// what the font needs to stay a font), or null when HarfBuzz could not make one — the
    /// caller then embeds nothing and the viewer falls back, as it did before this existed.
    /// </summary>
    public static byte[]? Subset(byte[] font, IEnumerable<int> codepoints)
    {
        var pin = GCHandle.Alloc(font, GCHandleType.Pinned);
        IntPtr blob = IntPtr.Zero, face = IntPtr.Zero, input = IntPtr.Zero, subset = IntPtr.Zero, result = IntPtr.Zero;
        try
        {
            blob = hb_blob_create(pin.AddrOfPinnedObject(), (uint)font.Length, ReadOnly, IntPtr.Zero, IntPtr.Zero);
            face = hb_face_create(blob, 0);
            input = hb_subset_input_create_or_fail();
            if (input == IntPtr.Zero)
                return null;
            // (The set is the input's own: it is not ours to destroy.)
            IntPtr set = hb_subset_input_unicode_set(input);
            foreach (int codepoint in codepoints)
                hb_set_add(set, (uint)codepoint);
            subset = hb_subset_or_fail(face, input);
            if (subset == IntPtr.Zero)
                return null;
            result = hb_face_reference_blob(subset);
            IntPtr data = hb_blob_get_data(result, out uint length);
            if (data == IntPtr.Zero || length == 0)
                return null;
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, (int)length);
            return bytes;
        }
        finally
        {
            if (result != IntPtr.Zero) hb_blob_destroy(result);
            if (subset != IntPtr.Zero) hb_face_destroy(subset);
            if (input != IntPtr.Zero) hb_subset_input_destroy(input);
            if (face != IntPtr.Zero) hb_face_destroy(face);
            if (blob != IntPtr.Zero) hb_blob_destroy(blob);
            pin.Free();
        }
    }
}
