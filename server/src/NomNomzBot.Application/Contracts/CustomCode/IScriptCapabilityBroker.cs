// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.CustomCode;

/// <summary>
/// Per-tenant capability grant assembly + enforcement (custom-code.md §3.2). Deny-by-default: a script is granted
/// exactly the capabilities it declared, each only if the catalog allows it for the channel (feature-flag gate,
/// never a <c>critical</c> tier). Any disallowed declared capability fails the whole grant FORBIDDEN (fail-closed).
/// </summary>
public interface IScriptCapabilityBroker
{
    Task<Result<ScriptCapabilityGrant>> BuildGrantAsync(
        Guid broadcasterId,
        IReadOnlyList<string> declaredCapabilities,
        CancellationToken cancellationToken = default
    );

    /// <summary>The full catalogue of capability keys this build exposes (UI + save-time validation read this).</summary>
    IReadOnlyList<ScriptCapabilityDescriptor> Catalog { get; }
}
