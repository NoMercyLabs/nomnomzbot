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
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
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
/// Inbound webhook endpoint CRUD (webhooks.md §3.1). Mints a 64-char opaque URL token and seals the per-provider
/// verification secret through the canonical <see cref="ITokenProtector"/> (per-tenant DEK, AAD bound to the
/// endpoint id so envelopes cannot be transplanted between endpoints). The plaintext secret is never persisted or
/// returned.
/// </summary>
public sealed class InboundWebhookEndpointService(
    IApplicationDbContext db,
    ITokenProtector tokenProtector,
    ISubjectKeyService subjectKeys,
    IConfiguration configuration,
    TimeProvider clock,
    IEventBus eventBus
) : IInboundWebhookEndpointService
{
    private const string Provider = "webhook:in";

    public async Task<Result<PagedList<InboundWebhookEndpointDto>>> ListAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<InboundWebhookEndpoint> query = db.InboundWebhookEndpoints.Where(e =>
            e.BroadcasterId == broadcasterId && e.DeletedAt == null
        );
        int total = await query.CountAsync(ct);
        List<InboundWebhookEndpoint> rows = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);
        return Result.Success(
            new PagedList<InboundWebhookEndpointDto>(
                [.. rows.Select(ToDto)],
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    public async Task<Result<InboundWebhookEndpointDto>> GetAsync(
        Guid broadcasterId,
        Guid endpointId,
        CancellationToken ct = default
    )
    {
        InboundWebhookEndpoint? endpoint = await FindAsync(broadcasterId, endpointId, ct);
        return endpoint is null
            ? Result.Failure<InboundWebhookEndpointDto>("Endpoint not found.", "NOT_FOUND")
            : Result.Success(ToDto(endpoint));
    }

    public async Task<Result<InboundWebhookEndpointDto>> CreateAsync(
        Guid broadcasterId,
        Guid actorUserId,
        CreateInboundWebhookRequest request,
        CancellationToken ct = default
    )
    {
        if (request.Adapter == WebhookAdapterKind.Generic && request.GenericConfig is null)
            return Result.Failure<InboundWebhookEndpointDto>(
                "A generic adapter requires a generic config.",
                "VALIDATION_FAILED"
            );

        DateTime now = clock.GetUtcNow().UtcDateTime;
        InboundWebhookEndpoint endpoint = new()
        {
            BroadcasterId = broadcasterId,
            Name = request.Name,
            Token = MintToken(),
            AdapterKind = request.Adapter,
            TargetPipelineId = request.TargetPipelineId,
            TargetEventType = request.TargetEventType,
            GenericConfigJson = request.GenericConfig is null
                ? null
                : JsonConvert.SerializeObject(request.GenericConfig),
            IsEnabled = request.IsEnabled,
            ReceiveCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await SealSecretAsync(endpoint, request.VerificationSecret, broadcasterId, ct);
        db.InboundWebhookEndpoints.Add(endpoint);
        await db.SaveChangesAsync(ct);
        await PublishConfigChangedAsync(broadcasterId, endpoint.Id, "created", ct);
        return Result.Success(ToDto(endpoint));
    }

    public async Task<Result<InboundWebhookEndpointDto>> UpdateAsync(
        Guid broadcasterId,
        Guid endpointId,
        UpdateInboundWebhookRequest request,
        CancellationToken ct = default
    )
    {
        InboundWebhookEndpoint? endpoint = await FindAsync(broadcasterId, endpointId, ct);
        if (endpoint is null)
            return Result.Failure<InboundWebhookEndpointDto>("Endpoint not found.", "NOT_FOUND");

        if (request.Name is not null)
            endpoint.Name = request.Name;
        if (request.TargetPipelineId is not null)
            endpoint.TargetPipelineId = request.TargetPipelineId;
        if (request.TargetEventType is not null)
            endpoint.TargetEventType = request.TargetEventType;
        if (request.GenericConfig is not null)
            endpoint.GenericConfigJson = JsonConvert.SerializeObject(request.GenericConfig);
        if (request.IsEnabled is bool enabled)
            endpoint.IsEnabled = enabled;
        if (!string.IsNullOrEmpty(request.VerificationSecret))
            await SealSecretAsync(endpoint, request.VerificationSecret, broadcasterId, ct);

        endpoint.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await PublishConfigChangedAsync(broadcasterId, endpoint.Id, "updated", ct);
        return Result.Success(ToDto(endpoint));
    }

    public async Task<Result<InboundWebhookEndpointDto>> RotateTokenAsync(
        Guid broadcasterId,
        Guid endpointId,
        CancellationToken ct = default
    )
    {
        InboundWebhookEndpoint? endpoint = await FindAsync(broadcasterId, endpointId, ct);
        if (endpoint is null)
            return Result.Failure<InboundWebhookEndpointDto>("Endpoint not found.", "NOT_FOUND");

        endpoint.Token = MintToken();
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
        InboundWebhookEndpoint? endpoint = await FindAsync(broadcasterId, endpointId, ct);
        if (endpoint is null)
            return Result.Failure("Endpoint not found.", "NOT_FOUND");
        endpoint.DeletedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await PublishConfigChangedAsync(broadcasterId, endpoint.Id, "deleted", ct);
        return Result.Success();
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

    private Task<InboundWebhookEndpoint?> FindAsync(
        Guid broadcasterId,
        Guid endpointId,
        CancellationToken ct
    ) =>
        db.InboundWebhookEndpoints.FirstOrDefaultAsync(
            e => e.Id == endpointId && e.BroadcasterId == broadcasterId && e.DeletedAt == null,
            ct
        );

    private async Task SealSecretAsync(
        InboundWebhookEndpoint endpoint,
        string secret,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        endpoint.VerificationSecretEnvelope = await tokenProtector.ProtectAsync(
            secret,
            new TokenProtectionContext(broadcasterId.ToString(), Provider, endpoint.Id.ToString()),
            ct
        );
        endpoint.EncryptionKeyId = await ResolveKeyIdAsync(broadcasterId, ct);
    }

    /// <summary>The DEK id <see cref="ITokenProtector"/> resolves for this tenant's inbound secrets (matches its derivation).</summary>
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

    private static string MintToken() =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)); // 64 url-safe chars

    private InboundWebhookEndpointDto ToDto(InboundWebhookEndpoint e) =>
        new(
            e.Id,
            e.Name,
            e.AdapterKind,
            $"{configuration["App:BaseUrl"]}/api/v1/webhooks/in/{e.Token}",
            !string.IsNullOrEmpty(e.VerificationSecretEnvelope),
            e.TargetPipelineId,
            e.TargetEventType,
            e.IsEnabled,
            e.LastReceivedAt,
            e.ReceiveCount,
            e.CreatedAt,
            e.UpdatedAt
        );
}
