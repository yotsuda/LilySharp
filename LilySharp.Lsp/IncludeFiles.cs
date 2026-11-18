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

using System.Collections.Concurrent;

namespace LilySharp.Lsp;

/// <summary>
/// What a <c>using "..."</c> reads from the disk, for the requests of an open document: the
/// file's text, read again only when the file was written again.
/// </summary>
/// <remarks>
/// <para>
/// A book with includes is expanded on every arrival (<c>ExpandUsings</c>), and several arrive
/// per keystroke: the preview, the Problems panel's pass, the caret's line, the sounding notes,
/// the code actions. Each one asks for every include — the first to build the piece, the others
/// to see that the piece they are about to reuse is still the piece (the snapshot's replay: an
/// include saved from another editor, or written by a generator, must show). Until session 1299
/// every question opened the file and read it whole: arrivals × includes reads per keystroke,
/// each a string of the file's size, to find it had not changed.
/// </para>
/// <para>
/// Now a question is one look at the file's length and time of writing; the text read last is
/// handed out again — the same string, so the replay's comparison is two references — while
/// both stand. ⚠️ A time of writing says "not written since" only once it is OLD: a file
/// written twice within its file system's tick (100 ns on NTFS, but a second or two on others)
/// to the same length would carry the same stamp with other text. So a text read less than
/// <see cref="Settles"/> after its file was written is never handed out again (the file is read
/// each time until its stamp is that old) — the rule git's index keeps for the same reason.
/// A stamp in the future (a clock that disagrees, a share) never settles.
/// </para>
/// <para>
/// What this does not see: a file changed to the same length by a tool that puts the old time
/// of writing back. The piece then stands until the file is written again or the server starts.
/// </para>
/// </remarks>
internal static class IncludeFiles
{
    /// <summary>How old a file's time of writing has to be before its text is kept.</summary>
    internal static readonly TimeSpan Settles = TimeSpan.FromSeconds(3);

    /// <summary>More files than this are not kept at once: a server that has seen this many has
    /// seen many books, and starts again from none.</summary>
    private const int Most = 256;

    private sealed record Kept(long Length, DateTime WrittenUtc, string Text);

    private static readonly ConcurrentDictionary<string, Kept> Texts = new(StringComparer.Ordinal);

    /// <summary>The text of the file at <paramref name="path"/>, or null when there is none.
    /// The same string as the last answer when the file was not read again.</summary>
    public static string? Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            Texts.TryRemove(path, out _);
            return null;
        }
        // (Both read before the text: a file written in between is then kept under the stamp it
        // no longer has, and is read again at the next question.)
        long length = info.Length;
        DateTime written = info.LastWriteTimeUtc;
        if (Texts.TryGetValue(path, out var kept) && kept.Length == length && kept.WrittenUtc == written)
            return kept.Text;

        string text = File.ReadAllText(path);
        TimeSpan age = DateTime.UtcNow - written;
        if (age >= Settles)
        {
            if (Texts.Count >= Most)
                Texts.Clear();
            Texts[path] = new Kept(length, written, text);
        }
        else
        {
            Texts.TryRemove(path, out _);
        }
        return text;
    }
}
