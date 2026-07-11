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
