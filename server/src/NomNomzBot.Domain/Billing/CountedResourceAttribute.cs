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
