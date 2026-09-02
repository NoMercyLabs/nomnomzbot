// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;

namespace NomNomzBot.Infrastructure.Webhooks;

/// <summary>
/// Outbound webhook endpoint CRUD (webhooks.md §3.5). Each endpoint pins to an enabled H.7 egress-allowlist row
/// (the SSRF boundary lives there, not here). The <c>whsec_</c> secret is minted, AEAD-sealed via the canonical
/// <see cref="ITokenProtector"/>, and revealed exactly once. Rotation keeps an overlap-valid secondary envelope so
/// the multi-sig signer accepts either during the window. (Deferred: <see cref="SendTestAsync"/> awaits the
/// SSRF-hardened egress client.)
/// </summary>
public sealed class OutboundWebhookEndpointService(
    IApplicationDbContext db,
    ITokenProtector tokenProtector,
    ISubjectKeyService subjectKeys,
    TimeProvider clock,
    IEventBus eventBus,
    ITemplateHelperValidator templateHelperValidator,
    IOutboundWebhookDispatcher dispatcher
) : IOutboundWebhookEndpointService
{
    private const string Provider = "webhook:out";

    public Result<IReadOnlyList<OutboundWebhookEventCatalogueEntry>> GetEventCatalogue() =>
        Result.Success(OutboundWebhookEventCatalogue.Entries);

    public async Task<Result<PagedList<OutboundWebhookEndpointDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<OutboundWebhookEndpoint> query = db.OutboundWebhookEndpoints.Where(e =>
            e.BroadcasterId == broadcasterId && e.DeletedAt == null
        );
        int total = await query.CountAsync(ct);
        List<OutboundWebhookEndpoint> rows = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);
        return Result.Success(
            new PagedList<OutboundWebhookEndpointDto>(
                [.. rows.Select(ToDto)],
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    public async Task<Result<OutboundWebhookEndpointDto>> GetAsync(
        Guid broadcasterId,
        Guid endpointId,
        CancellationToken ct = default
    )
    {
        OutboundWebhookEndpoint? endpoint = await FindAsync(broadcasterId, endpointId, ct);
        return endpoint is null
            ? Result.Failure<OutboundWebhookEndpointDto>("Endpoint not found.", "NOT_FOUND")
            : Result.Success(ToDto(endpoint));
    }

    public async Task<Result<PagedList<OutboundWebhookDeliveryDto>>> ListDeliveriesAsync(
        Guid broadcasterId,
        Guid endpointId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        // Confirm the endpoint exists under this tenant first, so a bad/foreign id is a clean NOT_FOUND rather
        // than an empty page that hides whether the endpoint or just its history is missing.
        OutboundWebhookEndpoint? endpoint = await FindAsync(broadcasterId, endpointId, ct);
        if (endpoint is null)
            return Result.Failure<PagedList<OutboundWebhookDeliveryDto>>(
                "Endpoint not found.",
                "NOT_FOUND"
            );

        IQueryable<OutboundWebhookDelivery> query = db.OutboundWebhookDeliveries.Where(d =>
            d.BroadcasterId == broadcasterId && d.EndpointId == endpointId
        );
        int total = await query.CountAsync(ct);
        List<OutboundWebhookDelivery> rows = await query
            .OrderByDescending(d => d.Id) // append-only bigint id — newest attempt first
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);
        return Result.Success(
            new PagedList<OutboundWebhookDeliveryDto>(
                [.. rows.Select(ToDeliveryDto)],
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    private static OutboundWebhookDeliveryDto ToDeliveryDto(OutboundWebhookDelivery d) =>
        new(
            d.Id,
            d.EndpointId,
            d.EventType,
            d.Attempt,
            d.Status.ToString(),
            d.ResponseCode,
            d.DurationMs,
            d.NextRetryAt,
            d.Error,
            d.CreatedAt
        );

    public async Task<Result<OutboundWebhookEndpointCreatedDto>> CreateAsync(
        Guid broadcasterId,
        Guid actorUserId,
        CreateOutboundWebhookRequest request,
        CancellationToken ct = default
    )
    {
        Guid? allowlistId = await db
            .HttpEgressAllowlists.Where(a =>
                a.BroadcasterId == broadcasterId
                && a.Fqdn == request.Fqdn
                && a.IsEnabled
                && a.DeletedAt == null
            )
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);
        if (allowlistId is null)
            return Result.Failure<OutboundWebhookEndpointCreatedDto>(
                "The target host is not in an enabled egress allowlist.",
                "EGRESS_NOT_ALLOWED"
            );

        // The path must not be able to redirect the delivery off the allowlisted host (authority hijack).
        if (!OutboundWebhookTargetUrl.TryBuild(request.Fqdn, request.Path, out _))
            return Result.Failure<OutboundWebhookEndpointCreatedDto>(
                "Path must keep the request on the allowlisted host (no host, userinfo, or scheme override).",
                "INVALID_PATH"
            );

        // Every subscribed type must be a real catalogue event (or '*'); webhook-lifecycle types are refused (§9).
        Result subscription = ValidateSubscribedEventTypes(request.SubscribedEventTypes);
        if (subscription.IsFailure)
            return Result.Failure<OutboundWebhookEndpointCreatedDto>(
                subscription.ErrorMessage,
                subscription.ErrorCode
            );

        // A JSON-declared body template must actually parse, or the author gets a silent downgrade to the
        // unescaped plain-text render path at delivery time (S-WEBHOOK-JSON-FALLBACK). Caught here, at save
        // time, with the parse position, while the author is present to fix it.
        Result bodyTemplateValidation = ValidateBodyTemplate(
            request.BodyTemplate,
            request.BodyIsJson
        );
        if (bodyTemplateValidation.IsFailure)
            return Result.Failure<OutboundWebhookEndpointCreatedDto>(
                bodyTemplateValidation.ErrorMessage,
                bodyTemplateValidation.ErrorCode
            );

        DateTime now = clock.GetUtcNow().UtcDateTime;
        OutboundWebhookEndpoint endpoint = new()
        {
            BroadcasterId = broadcasterId,
            Name = request.Name,
            Fqdn = request.Fqdn,
            HttpEgressAllowlistId = allowlistId,
            Path = request.Path,
            SubscribedEventTypesJson = JsonConvert.SerializeObject(request.SubscribedEventTypes),
            BodyTemplate = request.BodyTemplate,
            BodyIsJson = request.BodyIsJson,
            CustomHeadersJson = request.CustomHeaders is null
                ? null
                : JsonConvert.SerializeObject(request.CustomHeaders),
            IsEnabled = request.IsEnabled,
            ConsecutiveFailureCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
        string secret = MintSecret();
        await SealPrimaryAsync(endpoint, secret, broadcasterId, ct);
        db.OutboundWebhookEndpoints.Add(endpoint);
        await db.SaveChangesAsync(ct);
        await PublishConfigChangedAsync(broadcasterId, endpoint.Id, "created", ct);
        return Result.Success(new OutboundWebhookEndpointCreatedDto(ToDto(endpoint), secret));
    }

    public async Task<Result<OutboundWebhookEndpointDto>> UpdateAsync(
        Guid broadcasterId,
        Guid endpointId,
        UpdateOutboundWebhookRequest request,
        CancellationToken ct = default
    )
    {
        OutboundWebhookEndpoint? endpoint = await FindAsync(broadcasterId, endpointId, ct);
        if (endpoint is null)
            return Result.Failure<OutboundWebhookEndpointDto>("Endpoint not found.", "NOT_FOUND");

        if (request.Name is not null)
            endpoint.Name = request.Name;
        if (request.SubscribedEventTypes is not null)
        {
            Result subscription = ValidateSubscribedEventTypes(request.SubscribedEventTypes);
            if (subscription.IsFailure)
                return Result.Failure<OutboundWebhookEndpointDto>(
                    subscription.ErrorMessage,
                    subscription.ErrorCode
                );
            endpoint.SubscribedEventTypesJson = JsonConvert.SerializeObject(
                request.SubscribedEventTypes
            );
        }
        bool effectiveBodyIsJson = request.BodyIsJson ?? endpoint.BodyIsJson;
        string? effectiveBodyTemplate = request.BodyTemplate ?? endpoint.BodyTemplate;
        Result bodyTemplateValidation = ValidateBodyTemplate(
            effectiveBodyTemplate,
            effectiveBodyIsJson
        );
        if (bodyTemplateValidation.IsFailure)
            return Result.Failure<OutboundWebhookEndpointDto>(
                bodyTemplateValidation.ErrorMessage,
                bodyTemplateValidation.ErrorCode
            );

        if (request.BodyTemplate is not null)
            endpoint.BodyTemplate = request.BodyTemplate;
        if (request.BodyIsJson is { } bodyIsJson)
            endpoint.BodyIsJson = bodyIsJson;
        if (request.CustomHeaders is not null)
            endpoint.CustomHeadersJson = JsonConvert.SerializeObject(request.CustomHeaders);
        if (request.IsEnabled is { } enabled)
            endpoint.IsEnabled = enabled;

        endpoint.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await PublishConfigChangedAsync(broadcasterId, endpoint.Id, "updated", ct);
        return Result.Success(ToDto(endpoint));
    }

    public async Task<Result<OutboundWebhookEndpointCreatedDto>> RotateSecretAsync(
        Guid broadcasterId,
        Guid endpointId,
        CancellationToken ct = default
    )
    {
        OutboundWebhookEndpoint? endpoint = await FindAsync(broadcasterId, endpointId, ct);
        if (endpoint is null)
            return Result.Failure<OutboundWebhookEndpointCreatedDto>(
                "Endpoint not found.",
                "NOT_FOUND"
            );

        // Overlap: the current primary becomes the secondary; a fresh primary is minted.
        endpoint.SecondarySigningSecretEnvelope = endpoint.SigningSecretEnvelope;
        string secret = MintSecret();
        await SealPrimaryAsync(endpoint, secret, broadcasterId, ct);
        endpoint.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await PublishConfigChangedAsync(broadcasterId, endpoint.Id, "updated", ct);
        return Result.Success(new OutboundWebhookEndpointCreatedDto(ToDto(endpoint), secret));
    }

    public async Task<Result<OutboundWebhookEndpointDto>> ReenableAsync(
        Guid broadcasterId,
        Guid endpointId,
        CancellationToken ct = default
    )
    {
        OutboundWebhookEndpoint? endpoint = await FindAsync(broadcasterId, endpointId, ct);
        if (endpoint is null)
            return Result.Failure<OutboundWebhookEndpointDto>("Endpoint not found.", "NOT_FOUND");

        endpoint.IsEnabled = true;
        endpoint.ConsecutiveFailureCount = 0;
        endpoint.DisabledAt = null;
        endpoint.DisabledReason = null;
        endpoint.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await PublishConfigChangedAsync(broadcasterId, endpoint.Id, "updated", ct);
        return Result.Success(ToDto(endpoint));
    }

    public async Task<Result> DeleteAsync(
        Guid broadcasterId,
        Guid endpointId,
        CancellationToken ct = default
    )
    {
        OutboundWebhookEndpoint? endpoint = await FindAsync(broadcasterId, endpointId, ct);
        if (endpoint is null)
            return Result.Failure("Endpoint not found.", "NOT_FOUND");
        endpoint.DeletedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await PublishConfigChangedAsync(broadcasterId, endpoint.Id, "deleted", ct);
        return Result.Success();
    }

    public Task<Result<WebhookTestResultDto>> SendTestAsync(
        Guid broadcasterId,
        Guid endpointId,
        CancellationToken ct = default
    ) =>
        // The synthetic delivery needs the SSRF-hardened egress client (deferred with the egress handler).
        Task.FromResult(
            Result.Failure<WebhookTestResultDto>(
                "Test delivery is not available yet.",
                "SERVICE_UNAVAILABLE"
            )
        );

    public async Task<Result<OutboundWebhookDeliveryDto>> RetryDeliveryAsync(
        Guid broadcasterId,
        Guid endpointId,
        long deliveryId,
        CancellationToken ct = default
    )
    {
        OutboundWebhookEndpoint? endpoint = await FindAsync(broadcasterId, endpointId, ct);
        if (endpoint is null)
            return Result.Failure<OutboundWebhookDeliveryDto>("Endpoint not found.", "NOT_FOUND");

        OutboundWebhookDelivery? delivery = await db.OutboundWebhookDeliveries.FirstOrDefaultAsync(
            d =>
                d.Id == deliveryId
                && d.EndpointId == endpointId
                && d.BroadcasterId == broadcasterId,
            ct
        );
        if (delivery is null)
            return Result.Failure<OutboundWebhookDeliveryDto>("Delivery not found.", "NOT_FOUND");

        // Re-enabling is a separate, explicit action (ReenableAsync) — a manual retry against a disabled
        // endpoint is refused with a clear reason rather than silently dead-lettering the attempt.
        if (!endpoint.IsEnabled)
            return Result.Failure<OutboundWebhookDeliveryDto>(
                "Endpoint is disabled. Re-enable it before retrying a delivery.",
                "ENDPOINT_DISABLED"
            );

        delivery.Attempt++;
        Result<WebhookDeliveryStatus> attempt = await dispatcher.AttemptDeliveryAsync(delivery, ct);
        if (attempt.IsFailure)
            return Result.Failure<OutboundWebhookDeliveryDto>(
                attempt.ErrorMessage,
                attempt.ErrorCode
            );
        return Result.Success(ToDeliveryDto(delivery));
    }

    /// <summary>E5 dashboard live-sync: fired after every successful write so other open dashboards refetch.</summary>
    private Task PublishConfigChangedAsync(
        Guid broadcasterId,
        Guid endpointId,
        string action,
        CancellationToken ct
    ) =>
        eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterId,
                Domain = "webhooks",
                EntityId = endpointId.ToString(),
                Action = action,
            },
            ct
        );

    /// <summary>
    /// Rejects a subscription list that names an unknown or webhook-lifecycle event type (webhooks.md §9). <c>*</c> is
    /// always allowed (it means "all catalogue events" and never matches a lifecycle type). The lifecycle check runs
    /// first so a deny-listed type gets the precise "not subscribable" reason instead of a generic "unknown".
    /// </summary>
    private static Result ValidateSubscribedEventTypes(IReadOnlyList<string> eventTypes)
    {
        foreach (string eventType in eventTypes)
        {
            if (eventType == OutboundWebhookEventCatalogue.Wildcard)
                continue;
            if (OutboundWebhookEventCatalogue.IsLifecycle(eventType))
                return Result.Failure(
                    $"Event type '{eventType}' is not subscribable — webhook-lifecycle events are deny-listed to prevent self-amplification.",
                    "VALIDATION_FAILED"
                );
            if (!OutboundWebhookEventCatalogue.IsSubscribable(eventType))
                return Result.Failure(
                    $"Unknown event type '{eventType}'. Subscribe only to types from the event catalogue (or '*').",
                    "VALIDATION_FAILED"
                );
        }
        return Result.Success();
    }

    /// <summary>
    /// Validates a body template on both axes required to save (S042c, S-WEBHOOK-JSON-FALLBACK): every
    /// <c>{{helper}}</c> placeholder must name a real key from <see cref="TemplateHelperContext.Webhook"/>
    /// (an unknown key like <c>{{user.nmae}}</c> would otherwise render as nothing on a live delivery,
    /// silently), and a JSON-declared template must actually parse. Both are checked at save time, with
    /// the author present to fix it, rather than failing quietly at delivery time.
    /// </summary>
    /// <summary>
    /// Validates a body template the SAME WAY <see cref="WebhookBodyTemplateRenderer"/> renders it, which
    /// is the only way the two can agree.
    /// <para>
    /// A JSON body is parsed FIRST and its helper keys are then checked on the string LEAVES only. Running
    /// the helper validator over the raw JSON text instead reads structural braces as placeholders — for
    /// <c>{"broken": [1, 2,}</c> it reports an unknown helper named <c>"broken": [1, 2,</c> and the author
    /// never learns their JSON is malformed. A non-JSON body has no structure to respect, so its whole text
    /// is validated.
    /// </para>
    /// </summary>
    private Result ValidateBodyTemplate(string? bodyTemplate, bool bodyIsJson)
    {
        if (bodyTemplate is null)
            return Result.Success();

        if (!bodyIsJson)
            return templateHelperValidator.Validate(bodyTemplate, TemplateHelperContext.Webhook);

        JToken parsed;
        try
        {
            parsed = JToken.Parse(bodyTemplate);
        }
        catch (JsonReaderException ex)
        {
            return Result.Failure(
                $"Body template is declared as JSON but does not parse: {ex.Message}",
                "INVALID_JSON_BODY_TEMPLATE"
            );
        }

        IEnumerable<JToken> tokens = parsed is JContainer container
            ? container.DescendantsAndSelf()
            : [parsed];
        foreach (JToken leaf in tokens)
        {
            if (leaf.Type is not JTokenType.String)
                continue;

            Result leafValidation = templateHelperValidator.Validate(
                leaf.Value<string>(),
                TemplateHelperContext.Webhook
            );
            if (leafValidation.IsFailure)
                return leafValidation;
        }

        return Result.Success();
    }

    private Task<OutboundWebhookEndpoint?> FindAsync(
        Guid broadcasterId,
        Guid endpointId,
        CancellationToken ct
    ) =>
        db.OutboundWebhookEndpoints.FirstOrDefaultAsync(
            e => e.Id == endpointId && e.BroadcasterId == broadcasterId && e.DeletedAt == null,
            ct
        );

    private async Task SealPrimaryAsync(
        OutboundWebhookEndpoint endpoint,
        string secret,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        endpoint.SigningSecretEnvelope = await tokenProtector.ProtectAsync(
            secret,
            new(broadcasterId.ToString(), Provider, endpoint.Id.ToString()),
            ct
        );
        endpoint.EncryptionKeyId = await ResolveKeyIdAsync(broadcasterId, ct);
    }

    private async Task<Guid> ResolveKeyIdAsync(Guid broadcasterId, CancellationToken ct)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{Provider}:{broadcasterId}"));
        Guid subjectUserId = new(hash.AsSpan(0, 16));
        string subjectIdHash = Convert.ToHexStringLower(hash);
        Result<Guid> keyId = await subjectKeys.GetOrCreateSubjectKeyAsync(
            subjectUserId,
            subjectIdHash,
            ct
        );
        return keyId.IsSuccess ? keyId.Value : Guid.Empty;
    }

    private static string MintSecret() =>
        "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

    private static OutboundWebhookEndpointDto ToDto(OutboundWebhookEndpoint e) =>
        new(
            e.Id,
            e.Name,
            e.Fqdn,
            e.Path,
            JsonConvert.DeserializeObject<List<string>>(e.SubscribedEventTypesJson) ?? [],
            e.BodyTemplate,
            e.BodyIsJson,
            e.IsEnabled,
            e.ConsecutiveFailureCount,
            e.DisabledAt,
            e.DisabledReason,
            e.LastDeliveryAt,
            e.LastSuccessAt,
            e.CreatedAt,
            e.UpdatedAt
        );
}
