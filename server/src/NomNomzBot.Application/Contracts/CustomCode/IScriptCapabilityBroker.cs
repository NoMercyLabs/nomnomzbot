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
