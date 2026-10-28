// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Platform;

/// <summary>
/// Marks a string property whose wire value is one of a fixed set of literals — the <c>kind</c> discriminator of a
/// payload that shares an event name with other payload shapes. The generated TypeScript declares the property as
/// that literal (or union of literals), so a widget narrows the payload union with <c>if (d.kind === '...')</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class WireLiteralAttribute(params string[] values) : Attribute
{
    /// <summary>The literals the property can carry on the wire.</summary>
    public IReadOnlyList<string> Values { get; } = values;
}
