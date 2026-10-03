// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
