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
