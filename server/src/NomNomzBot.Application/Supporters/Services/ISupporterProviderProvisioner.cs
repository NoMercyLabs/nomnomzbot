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
