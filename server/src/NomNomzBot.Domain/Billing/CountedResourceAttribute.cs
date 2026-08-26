// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Billing;

/// <summary>
/// Declares an entity as a limit-registry-governed countable resource (S-BUDGETS-a). This is the structural
/// source of truth the guard test scans: every <c>DbSet&lt;T&gt;</c> entity carrying this attribute MUST have a
/// matching entry in <c>LimitedResourceRegistry</c> with the same <see cref="Class"/> — an attributed entity
/// with no registry entry, or a classification mismatch, fails the guard loudly.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CountedResourceAttribute(string limitKey, ResourceClass @class) : Attribute
{
    /// <summary>Matches <c>TierLimit.LimitKey</c> / <c>LimitedResourceRegistry</c> key.</summary>
    public string LimitKey { get; } = limitKey;

    public ResourceClass Class { get; } = @class;
}
