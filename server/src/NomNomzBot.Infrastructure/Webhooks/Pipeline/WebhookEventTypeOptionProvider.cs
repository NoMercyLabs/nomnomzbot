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

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Webhooks.Pipeline;

/// <summary>
/// Supplies the subscribable outbound webhook event types for the <c>webhook_event_type</c> picker kind, read from the
/// webhook event catalogue (<see cref="IOutboundWebhookEndpointService.GetEventCatalogue"/>), the closed set an
/// endpoint's subscription is validated against. The saved value is the event type; the secondary text is its
/// category. The catalogue is the same for every channel. A catalogue that cannot be read is unavailable with the
/// service's reason.
/// </summary>
internal sealed class WebhookEventTypeOptionProvider : IPipelineOptionProvider
{
    private const string CategoryPrefix = "webhook_event_category_";

    private readonly IOutboundWebhookEndpointService _endpoints;

    public WebhookEventTypeOptionProvider(IOutboundWebhookEndpointService endpoints)
    {
        _endpoints = endpoints;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.WebhookEventType;

    public Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        Result<IReadOnlyList<OutboundWebhookEventCatalogueEntry>> catalogue =
            _endpoints.GetEventCatalogue();

        return Task.FromResult(
            catalogue.IsFailure
                ? PipelineOptionPage.Unavailable(catalogue.ErrorMessage!)
                : PipelineOptionPage.Of(catalogue.Value.Select(ToOption), request)
        );
    }

    private static PipelineOption ToOption(OutboundWebhookEventCatalogueEntry entry) =>
        PipelineOptionPage.Option(
            entry.EventType,
            entry.EventType,
            entry.CategoryKey.Replace(CategoryPrefix, string.Empty).Replace('_', ' ')
        );
}
