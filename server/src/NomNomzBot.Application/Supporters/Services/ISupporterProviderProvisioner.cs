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

namespace NomNomzBot.Application.Supporters.Services;

/// <summary>
/// Provider-side ingest provisioning for an OAuth-connected supporter source (supporter-events.md D3): where
/// the provider's API can register the webhook itself (Patreon's <c>w:campaigns.webhook</c>), this makes
/// connect truly one-step — the bot registers its own ingest URL with the provider and seals the secret the
/// PROVIDER mints onto the inbound endpoint. Auto-discovered; a new capable provider is one implementation.
/// Idempotent: re-provisioning finds the existing registration and just re-syncs the secret.
/// </summary>
public interface ISupporterProviderProvisioner
{
    /// <summary>The supporter source this provisions (matches <c>ISupporterSource.SourceKey</c>).</summary>
    string SourceKey { get; }

    /// <summary>
    /// Registers (or finds) the provider-side webhook pointing at <paramref name="ingestUrl"/> using the
    /// vaulted OAuth tokens of <paramref name="integrationConnectionId"/>, and seals the provider-minted
    /// verification secret onto inbound endpoint <paramref name="endpointId"/>.
    /// </summary>
    Task<Result> ProvisionAsync(
        Guid broadcasterId,
        Guid integrationConnectionId,
        Guid endpointId,
        string ingestUrl,
        CancellationToken ct = default
    );
}
