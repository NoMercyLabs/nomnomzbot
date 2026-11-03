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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Webhooks.Pipeline;

/// <summary>
/// Supplies the asking channel's outbound webhook endpoints for the <c>webhook_endpoint</c> picker kind. The saved
/// value is the endpoint id; the label is the endpoint name and the secondary text is the host it posts to. A deleted
/// endpoint and another channel's endpoint never appear. A saved id in the 26-character wire form still finds its
/// endpoint. The search, the order and the page run in the database query.
/// </summary>
internal sealed class WebhookEndpointOptionProvider : IPipelineOptionProvider
{
    private readonly IApplicationDbContext _db;

    public WebhookEndpointOptionProvider(IApplicationDbContext db)
    {
        _db = db;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.WebhookEndpoint;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        PipelineOptionQuery asked = PipelineOptionPage.WithOwnedIdValue(request);
        IQueryable<OutboundWebhookEndpoint> query = _db.OutboundWebhookEndpoints.Where(e =>
            e.BroadcasterId == broadcasterId && e.DeletedAt == null
        );

        if (asked.IsValueLookup)
        {
            if (!Guid.TryParse(asked.Value, out Guid id))
                return PipelineOptionPage.Empty();

            query = query.Where(e => e.Id == id);
        }
        else if (!string.IsNullOrWhiteSpace(asked.Search))
        {
            string term = asked.Search.Trim().ToLowerInvariant();
            query = query.Where(e => e.Name.ToLower().Contains(term));
        }

        int total = await query.CountAsync(ct);
        List<OutboundWebhookEndpoint> page = await query
            .OrderBy(e => e.Name)
            .Skip((asked.Pagination.Page - 1) * asked.Pagination.PageSize)
            .Take(asked.Pagination.PageSize)
            .ToListAsync(ct);

        return Result.Success(
            PipelineOptionListResult.Of(
                [.. page.Select(e => PipelineOptionPage.Option(e.Id.ToString(), e.Name, e.Fqdn))],
                total
            )
        );
    }
}
