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
using NomNomzBot.Application.DTOs.Webhooks;

namespace NomNomzBot.Application.Contracts.Webhooks;

/// <summary>
/// The core inbound ingest path (webhooks.md §3.2): the single place a raw request becomes a verified, deduped,
/// journaled, fanned-out event. THE DISPATCHER OWNS token resolution — the anonymous controller only pre-limits +
/// guards size/method/content, then hands the raw request here. Returns a typed outcome the controller maps to HTTP.
/// </summary>
public interface IInboundWebhookDispatcher
{
    Task<Result<InboundDispatchResult>> DispatchAsync(
        InboundWebhookRequest request,
        CancellationToken ct = default
    );
}
