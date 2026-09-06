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

using System.Text;

namespace LilySharp.Cli;

/// <summary>
/// Gives each file of a PARALLEL batch its own console, so one file's report arrives as one
/// block instead of interleaved a line at a time with three others'.
/// </summary>
/// <remarks>
/// ⚠️ WHY IT IS SHAPED LIKE THIS. Every command writes straight to <c>Console</c>
/// (<c>Created: …</c>, the diagnostics, the warnings), and <c>Console.SetOut</c> is
/// PROCESS-global — there is no per-thread console to set. So the real console is replaced
/// ONCE by a writer that asks an <see cref="AsyncLocal{T}"/> where the current work item's
/// buffer is: inside a captured region it collects, outside one it writes straight through,
/// which is what keeps the batch's own progress lines going to the terminal immediately.
/// <para>
/// ⚠️ <c>AsyncLocal</c> RATHER THAN <c>[ThreadStatic]</c> because the value has to travel
/// with the work item, and a work item may resume on another thread. It flows through
/// <c>Parallel.For</c>'s execution context, which is how the buffer reaches the command.
/// </para>
/// <para>
/// ⚠️ STDOUT AND STDERR ARE CAPTURED SEPARATELY and flushed to their own streams, so a
/// caller redirecting only one of them still gets what it asked for. Their relative order
/// within one file is not preserved — that is already true of any two-stream pipeline, and
/// the alternative (one interleaved buffer) would break redirection.
/// </para>
/// </remarks>
internal sealed class CapturedConsole : TextWriter
{
    private static readonly AsyncLocal<(StringBuilder Out, StringBuilder Err)?> _current = new();

    private static TextWriter? _realOut;
    private static TextWriter? _realErr;
    private static bool _installed;

    private readonly bool _isError;

    private CapturedConsole(bool isError) => _isError = isError;

    public override Encoding Encoding => (_isError ? _realErr : _realOut)!.Encoding;

    /// <summary>Replaces the process console once. Idempotent and thread-safe.</summary>
    /// <remarks>
    /// ⚠️ LOCKED EVEN THOUGH THE ONE CALLER CALLS IT BEFORE STARTING ANY WORKER. A
    /// lazily-initialised singleton behind a bare bool is the shape that is safe until
    /// somebody calls it from inside a worker, and then fails by capturing the CAPTURING
    /// writer as <c>_realOut</c> — output that vanishes rather than output that is wrong,
    /// which is worse to diagnose. The lock costs one uncontended acquire per run.
    /// </remarks>
    public static void Install()
    {
        lock (_writeGate)
        {
            if (_installed)
                return;
            _realOut = Console.Out;
            _realErr = Console.Error;
            Console.SetOut(new CapturedConsole(isError: false));
            Console.SetError(new CapturedConsole(isError: true));
            _installed = true;
        }
    }

    /// <summary>
    /// Runs <paramref name="body"/> with everything it prints collected, and returns what it
    /// printed. Nested capture is not supported and not needed: one work item, one file.
    /// </summary>
    public static (string Out, string Err) Collect(Action body)
    {
        var pair = (Out: new StringBuilder(), Err: new StringBuilder());
        var saved = _current.Value;
        _current.Value = pair;
        try { body(); }
        finally { _current.Value = saved; }
        return (pair.Out.ToString(), pair.Err.ToString());
    }

    /// <summary>Writes a block to the real console under one lock, so it stays whole.</summary>
    public static void Emit(string outText, string errText)
    {
        lock (_writeGate)
        {
            if (outText.Length > 0) _realOut!.Write(outText);
            if (errText.Length > 0) _realErr!.Write(errText);
        }
    }

    private static readonly object _writeGate = new();

    private TextWriter Passthrough => (_isError ? _realErr : _realOut)!;

    private StringBuilder? Buffer =>
        _current.Value is { } p ? (_isError ? p.Err : p.Out) : null;

    public override void Write(char value)
    {
        var b = Buffer;
        if (b != null) b.Append(value);
        else lock (_writeGate) Passthrough.Write(value);
    }

    public override void Write(string? value)
    {
        if (value is null) return;
        var b = Buffer;
        if (b != null) b.Append(value);
        else lock (_writeGate) Passthrough.Write(value);
    }

    public override void WriteLine(string? value)
    {
        var b = Buffer;
        if (b != null) b.Append(value).Append(CoreNewLine);
        else lock (_writeGate) Passthrough.WriteLine(value);
    }

    public override void WriteLine()
    {
        var b = Buffer;
        if (b != null) b.Append(CoreNewLine);
        else lock (_writeGate) Passthrough.WriteLine();
    }

    public override void Flush()
    {
        if (Buffer is null)
            lock (_writeGate) Passthrough.Flush();
    }
}
