// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Platform;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// Per-tenant capability grant assembly (custom-code.md §3.2, catalogue §6.2). Deny-by-default: a script gets only
/// the capabilities it declared, each validated against the catalogue (must exist, must not be <c>critical</c>, its
/// feature-flag must be enabled for the channel). Any disallowed declared capability fails the whole grant
/// FORBIDDEN — fail-closed.
/// </summary>
public sealed class ScriptCapabilityBroker(IFeatureFlagService featureFlags)
    : IScriptCapabilityBroker
{
    private const string FeatureGate = "custom_code";

    // The §6.2 host-call surface. low/tos only — no `critical` capability is ever exposed to run_code.
    private static readonly ScriptCapabilityDescriptor[] CatalogEntries =
    [
        new("vars.read", "low", FeatureGate, SideEffecting: false),
        new("vars.write", "low", FeatureGate, SideEffecting: true),
        new("args.get", "low", FeatureGate, SideEffecting: false),
        new("user.get", "low", FeatureGate, SideEffecting: false),
        new("chat.send", "tos", FeatureGate, SideEffecting: true),
        new("chat.reply", "tos", FeatureGate, SideEffecting: true),
        new("music.queue", "tos", FeatureGate, SideEffecting: true),
        new("music.nowPlaying", "low", FeatureGate, SideEffecting: false),
        new("economy.read", "low", FeatureGate, SideEffecting: false),
        new("http.fetch", "tos", FeatureGate, SideEffecting: true),
    ];

    public IReadOnlyList<ScriptCapabilityDescriptor> Catalog => CatalogEntries;

    public async Task<Result<ScriptCapabilityGrant>> BuildGrantAsync(
        Guid broadcasterId,
        IReadOnlyList<string> declaredCapabilities,
        CancellationToken cancellationToken = default
    )
    {
        List<ScriptCapabilityDescriptor> granted = [];
        foreach (string key in declaredCapabilities.Distinct(StringComparer.Ordinal))
        {
            ScriptCapabilityDescriptor? descriptor = CatalogEntries.FirstOrDefault(c =>
                c.Key == key
            );
            if (descriptor is null)
                return Result.Failure<ScriptCapabilityGrant>(
                    $"Unknown capability: {key}.",
                    "FORBIDDEN"
                );
            if (descriptor.FloorTier == "critical")
                return Result.Failure<ScriptCapabilityGrant>(
                    $"Capability not permitted in scripts: {key}.",
                    "FORBIDDEN"
                );
            if (
                !await featureFlags.IsEnabledForAsync(
                    descriptor.FeatureFlagKey,
                    broadcasterId,
                    cancellationToken
                )
            )
                return Result.Failure<ScriptCapabilityGrant>(
                    $"Capability is not enabled: {key}.",
                    "FORBIDDEN"
                );
            granted.Add(descriptor);
        }
        return Result.Success(new ScriptCapabilityGrant(broadcasterId, granted));
    }
}
