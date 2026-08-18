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

using System.Runtime.InteropServices;
using SkiaSharp;

namespace LilySharp.Core.Rendering;

/// <summary>
/// One glyph's outline, read from the font's OWN TABLES through HarfBuzz's
/// <c>hb_font_draw_glyph</c> and appended to an <see cref="SKPath"/> in Skia's Y-DOWN frame.
/// </summary>
/// <remarks>
/// ⚠️ THIS EXISTS BECAUSE <c>SKPaint.GetTextPath</c> IS NOT THE SAME FUNCTION ON TWO
/// MACHINES. MEASURED 2026-08-19 on the bundled bold serif at TextSize 1000 (upem 1000):
/// Windows returns the design's own integers (<c>"3"</c> — top <c>-708</c>, bottom
/// <c>14</c>); Linux, where SkiaSharp's native build scales through FreeType, returns the
/// same outline quantised to 1/512 of a unit (<c>-708.0078125</c> / <c>13.916015625</c>).
/// No paint setting changes it: <c>NoHinting</c>, <c>IsLinearText</c> and
/// <c>SubpixelText</c> were each measured on both sides and all four combinations return
/// the Linux numbers on Linux. It is the FreeType path itself, not grid-fitting.
/// <para>
/// That is ~1e-5 em per glyph, which the font-size multiply and the vertical stacking turn
/// into ~1e-4 staff spaces — enough to move a ledger point off its recorded residual and to
/// change a two-decimal SVG coordinate. It is what made the CI ubuntu leg red: 51 of the 53
/// failures this repo's suite reports on Linux are this and nothing else.
/// </para>
/// <para>
/// HarfBuzz has no rasteriser in it. <c>hb_font_draw_glyph</c> interprets the CFF
/// charstrings and hands back the design's coordinates, so the same font gives the same
/// outline everywhere. MEASURED: the emitted command stream for the bold serif digits and
/// for three Emmentaler glyphs is SHA-256 IDENTICAL on Windows and Linux, and its extremes
/// are the numbers Windows' Skia was already returning — this moves Linux onto Windows'
/// answer rather than moving both onto a third one.
/// </para>
/// <para>
/// LILYSHARP-OWN: LilyPond reads its text ink through FreeType as well (Pango over the
/// FreeType outline, <c>lily/modified-font-metric.cc:125-143</c>
/// <c>Modified_font_metric::text_stencil</c>), so this is a DEPARTURE — Lily# now reads the
/// design where LilyPond reads a scaler. It is one Lily# can afford and LilyPond cannot:
/// LilyPond rasterises at <c>PANGO_RESOLUTION</c> 1200 dpi, where the quantum is orders
/// below the residuals this ledger records, and it has no second platform to agree with.
/// WHAT OBSERVES IT: the 529-point LP ledger, unmoved by this change — every residual
/// recorded against LilyPond's own dump still reads what it read, which is the measurement
/// that says the design and LilyPond's FreeType agree here to past the last digit any entry
/// carries. WHEN IT GOES AWAY: if a ledger entry ever moves BECAUSE of this, the answer is
/// not a rasteriser again — it is that this engine and LilyPond disagree about a glyph, and
/// that belongs in the ledger with a number on it.
/// </para>
/// <para>
/// ⚠️ RAW P/INVOKE, DELIBERATELY. <c>HarfBuzzSharp</c> 8.3.1 binds the shaping and the
/// metrics but exposes no draw or outline API at all (checked by reflection over the
/// assembly's types and over <c>Font</c>'s methods). The native library it already ships —
/// the one the shaper in <see cref="TextFontMetrics"/> is calling — exports the whole
/// <c>hb_draw_*</c> family, so this needs no new package.
/// </para>
/// <para>
/// ⚠️ NOT THREAD-SAFE, like the shaping it sits beside: <c>hb_font_t</c> caches inside
/// itself. Callers hold the same lock they already hold for <c>Shape</c>.
/// </para>
/// </remarks>
internal static class HarfBuzzOutline
{
    // The native library HarfBuzzSharp itself binds — same file, same load, no new asset.
    private const string Lib = "libHarfBuzzSharp";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MoveToFunc(IntPtr dfuncs, IntPtr data, IntPtr st,
                                     float toX, float toY, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void LineToFunc(IntPtr dfuncs, IntPtr data, IntPtr st,
                                     float toX, float toY, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void QuadToFunc(IntPtr dfuncs, IntPtr data, IntPtr st,
                                     float cX, float cY, float toX, float toY, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CubicToFunc(IntPtr dfuncs, IntPtr data, IntPtr st,
                                      float c1X, float c1Y, float c2X, float c2Y,
                                      float toX, float toY, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ClosePathFunc(IntPtr dfuncs, IntPtr data, IntPtr st, IntPtr user);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr hb_draw_funcs_create();
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_draw_funcs_make_immutable(IntPtr dfuncs);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_draw_funcs_set_move_to_func(
        IntPtr dfuncs, MoveToFunc func, IntPtr userData, IntPtr destroy);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_draw_funcs_set_line_to_func(
        IntPtr dfuncs, LineToFunc func, IntPtr userData, IntPtr destroy);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_draw_funcs_set_quadratic_to_func(
        IntPtr dfuncs, QuadToFunc func, IntPtr userData, IntPtr destroy);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_draw_funcs_set_cubic_to_func(
        IntPtr dfuncs, CubicToFunc func, IntPtr userData, IntPtr destroy);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_draw_funcs_set_close_path_func(
        IntPtr dfuncs, ClosePathFunc func, IntPtr userData, IntPtr destroy);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void hb_font_draw_glyph(
        IntPtr font, uint glyph, IntPtr dfuncs, IntPtr drawData);

    // Where one glyph's commands land: the path being built, the pen it sits at, and the
    // font-unit to path-unit factor. Reaches the callbacks through hb's own draw_data.
    private sealed class Sink
    {
        public SKPath Path = null!;
        public float PenX;
        public float Scale;
    }

    // Rooted for the process: a collected delegate is a callback into freed memory, and
    // these are handed to native code that keeps them in an immutable funcs object.
    private static readonly MoveToFunc MoveTo = OnMoveTo;
    private static readonly LineToFunc LineTo = OnLineTo;
    private static readonly QuadToFunc QuadTo = OnQuadTo;
    private static readonly CubicToFunc CubicTo = OnCubicTo;
    private static readonly ClosePathFunc ClosePath = OnClosePath;

    private static readonly IntPtr DrawFuncs = CreateDrawFuncs();

    private static IntPtr CreateDrawFuncs()
    {
        var df = hb_draw_funcs_create();
        hb_draw_funcs_set_move_to_func(df, MoveTo, IntPtr.Zero, IntPtr.Zero);
        hb_draw_funcs_set_line_to_func(df, LineTo, IntPtr.Zero, IntPtr.Zero);
        hb_draw_funcs_set_quadratic_to_func(df, QuadTo, IntPtr.Zero, IntPtr.Zero);
        hb_draw_funcs_set_cubic_to_func(df, CubicTo, IntPtr.Zero, IntPtr.Zero);
        hb_draw_funcs_set_close_path_func(df, ClosePath, IntPtr.Zero, IntPtr.Zero);
        hb_draw_funcs_make_immutable(df);
        return df;
    }

    private static Sink Target(IntPtr data) => (Sink)GCHandle.FromIntPtr(data).Target!;

    // ⚠️ Y IS NEGATED HERE, once, at the boundary. HarfBuzz is Y-UP about the baseline and
    // SKPath (the frame every caller of this file already reads) is Y-DOWN; the two
    // conventions meeting anywhere else is how a sign error becomes a silent mirror.
    private static void OnMoveTo(IntPtr d, IntPtr data, IntPtr st, float x, float y, IntPtr u)
    {
        var s = Target(data);
        s.Path.MoveTo(x * s.Scale + s.PenX, -y * s.Scale);
    }

    private static void OnLineTo(IntPtr d, IntPtr data, IntPtr st, float x, float y, IntPtr u)
    {
        var s = Target(data);
        s.Path.LineTo(x * s.Scale + s.PenX, -y * s.Scale);
    }

    private static void OnQuadTo(IntPtr d, IntPtr data, IntPtr st,
                                 float cx, float cy, float x, float y, IntPtr u)
    {
        var s = Target(data);
        s.Path.QuadTo(cx * s.Scale + s.PenX, -cy * s.Scale,
                      x * s.Scale + s.PenX, -y * s.Scale);
    }

    private static void OnCubicTo(IntPtr d, IntPtr data, IntPtr st,
                                  float c1x, float c1y, float c2x, float c2y,
                                  float x, float y, IntPtr u)
    {
        var s = Target(data);
        s.Path.CubicTo(c1x * s.Scale + s.PenX, -c1y * s.Scale,
                       c2x * s.Scale + s.PenX, -c2y * s.Scale,
                       x * s.Scale + s.PenX, -y * s.Scale);
    }

    private static void OnClosePath(IntPtr d, IntPtr data, IntPtr st, IntPtr u)
        => Target(data).Path.Close();

    /// <summary>
    /// Appends glyph <paramref name="glyph"/> of <paramref name="hbFont"/> to
    /// <paramref name="path"/>, its origin at <paramref name="penX"/> on the baseline.
    /// </summary>
    /// <param name="path">The path being built; the glyph is appended to it.</param>
    /// <param name="hbFont">A raw <c>hb_font_t</c> handle, its scale already set.</param>
    /// <param name="glyph">The face's own glyph id — not a code point.</param>
    /// <param name="penX">Where this glyph's origin sits, in path units.</param>
    /// <param name="scale">Font units to path units — 1.0 when the font's units-per-em
    /// already is the path's frame, which is the case for every bundled face.</param>
    /// <remarks>⚠️ The caller must hold the lock on <paramref name="hbFont"/>.</remarks>
    public static void Append(SKPath path, IntPtr hbFont, uint glyph, float penX, float scale)
    {
        var sink = new Sink { Path = path, PenX = penX, Scale = scale };
        var handle = GCHandle.Alloc(sink);
        try
        {
            hb_font_draw_glyph(hbFont, glyph, DrawFuncs, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
    }
}
