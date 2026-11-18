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

namespace LilySharp.Lsp;

public sealed partial class LilySharpLanguageServer
{
    /// <summary>
    /// Where the document an editor names by <paramref name="uri"/> is on the disk, or the
    /// empty string when it is not a file (an untitled score). THE one reader of
    /// <see cref="Uri.LocalPath"/> in the server (DocumentFilePathTests holds it so).
    /// </summary>
    /// <remarks>
    /// VS Code writes a Windows drive's colon encoded — <c>file:///c%3A/Users/me/song.lys</c> —
    /// and <see cref="Uri.LocalPath"/> reads that as <c>/c:/Users/me/song.lys</c>: with the
    /// colon encoded .NET does not see a drive, so the path keeps its leading slash and is a
    /// path that is nowhere. Until session 1299 everything read beside a document was looked
    /// for there: every <c>using</c> in the editor was LYS0028 "cannot read", and a scan's side
    /// file was never found. (The tests named their documents by <c>new Uri(path)</c>, the form
    /// that reads right; a real VS Code said it, Lab sessions/p1299/vscode-e2e.)
    /// </remarks>
    internal static string FilePathOf(Uri? uri)
    {
        if (uri is not { IsAbsoluteUri: true, IsFile: true })
            return string.Empty;
        string path = uri.LocalPath;
        // "/c:/dir/file": a drive behind a slash. (Read on every system: the server and the
        // editor are on one machine, so the form only arrives where it is a drive.)
        if (path.Length >= 3 && path[0] == '/' && char.IsAsciiLetter(path[1]) && path[2] == ':'
            && (path.Length == 3 || path[3] == '/'))
            path = path[1..].Replace('/', System.IO.Path.DirectorySeparatorChar);
        return path;
    }
}
