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

namespace LilySharp.Core.Semantics.Symbols;

/// <summary>
/// Kinds of symbols in LilySharp.
/// </summary>
public enum SymbolKind
{
    /// <summary>A section definition (section A { ... }).</summary>
    Section,

    /// <summary>A phrase definition (phrase name = { ... }).</summary>
    Phrase,

    /// <summary>A part definition (part name { ... }).</summary>
    Part,

    /// <summary>A variable definition (name = expression).</summary>
    Variable,

    /// <summary>A structure definition (structure { ... }).</summary>
    Structure
}
