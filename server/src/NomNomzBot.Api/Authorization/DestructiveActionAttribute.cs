// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Authorization;

/// <summary>
/// Marks a controller action as a destructive operation (deletes or disables something other data
/// may reference) for the S-CONSEQ consequence law: every destructive action must surface a real,
/// counted blast radius before the save. A `DestructiveActionScannerTests` guard enumerates every
/// <c>[HttpDelete]</c>/disable action under <c>Controllers/V1</c> and fails loud on any that carries
/// neither this attribute nor an explicit <see cref="NotDestructiveAttribute"/> exemption — an
/// unclassified destructive action is the defect, not a thing to silently skip.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DestructiveActionAttribute : Attribute
{
    /// <summary>
    /// True when this action has a paired blast-radius query (or computes the count inline) backed
    /// by real counted rows — never an estimate. The scanner requires this to be true; the attribute
    /// exists so the requirement is visible at the call site, not just enforced by a test.
    /// </summary>
    public bool HasCountedBlastRadius { get; init; }

    /// <summary>
    /// Set (ISO date) on an action that NEEDS a counted blast radius but does not have one yet. The scanner
    /// admits it ONLY while it is listed in the dated baseline in <c>DestructiveActionScannerTests</c>, which
    /// may only shrink — a new destructive action can never join it. Never set this together with
    /// <see cref="HasCountedBlastRadius"/>.
    /// </summary>
    public string? PendingBlastRadiusSince { get; init; }
}

/// <summary>
/// Explicit, reasoned exemption for an endpoint the scanner would otherwise flag as an unclassified
/// destructive action (e.g. an <c>[HttpDelete]</c> that removes a leaf record nothing else can
/// reference). Requires a reason so the exemption reads as a decision, not a gap.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class NotDestructiveAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}
