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

namespace LilySharp.Core.Rendering;

/// <summary>A disposable that does nothing — the no-op result of a scope-opening
/// call (e.g. a backend that ignores source-position metadata).</summary>
internal sealed class NullScope : IDisposable
{
    public static readonly NullScope Instance = new();
    public void Dispose() { }
}

/// <summary>A disposable that runs a closure once on <see cref="Dispose"/> — used
/// by drawing contexts to restore graphics state at the end of a group/source scope.</summary>
internal sealed class ScopeAction : IDisposable
{
    private Action? _action;
    public ScopeAction(Action a) { _action = a; }
    public void Dispose() { _action?.Invoke(); _action = null; }
}
