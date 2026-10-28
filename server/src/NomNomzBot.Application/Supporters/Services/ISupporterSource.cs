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
using NomNomzBot.Application.Supporters.Dtos;

namespace NomNomzBot.Application.Supporters.Services;

/// <summary>
/// A generic monetization provider adapter (supporter-events.md §3, D2). One per provider, auto-discovered.
/// Normalizes a raw provider payload into a <see cref="SupporterEventDraft"/> — including the dedup
/// <c>ProviderTransactionId</c> — with no persistence. Adding a provider = drop an adapter; the ingest engine
/// is unchanged.
/// </summary>
public interface ISupporterSource
{
    /// <summary>The provider key — <c>kofi</c>, <c>patreon</c>, … (matches <c>SupporterConnection.SourceKey</c>).</summary>
    string SourceKey { get; }

    /// <summary>Which kinds this provider emits and how it ingests.</summary>
    SupporterSourceCapabilities Capabilities { get; }

    /// <summary>Normalizes a raw provider payload into a draft. Failure = the payload is not a supported event.</summary>
    Task<Result<SupporterEventDraft>> NormalizeAsync(
        string rawPayload,
        CancellationToken ct = default
    );
}
