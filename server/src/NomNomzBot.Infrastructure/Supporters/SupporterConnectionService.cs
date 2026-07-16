// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Application.Supporters.Dtos;
using NomNomzBot.Application.Supporters.Services;
using NomNomzBot.Domain.Supporters.Entities;
using NomNomzBot.Domain.Webhooks.Enums;

namespace NomNomzBot.Infrastructure.Supporters;

/// <summary>
/// Manages a broadcaster's supporter connections + browses recorded events (supporter-events.md §5). A
/// connection is the enforced enable-toggle for a provider: ingest is default-deny and only fires when a live
/// connection exists (checked in <see cref="SupporterIngestService"/>). A webhook provider connects in ONE
/// step: its verification secret passes straight through to an auto-provisioned inbound-webhook endpoint
/// (never stored on the connection), and the endpoint's ingest URL comes back on the DTO to paste into the
/// provider's settings. A socket/ws/poll provider's key (or, for DonorDrive, its public donations URL) is
/// AEAD-sealed onto the connection (supporter-events.md §0 D6) for the ingress runners to open.
/// </summary>
public sealed class SupporterConnectionService : ISupporterConnectionService
{
    private const int MaxPageSize = 100;

    private readonly IApplicationDbContext _db;
    private readonly IReadOnlyDictionary<string, ISupporterSource> _sources;
    private readonly ITokenProtector _protector;
    private readonly IInboundWebhookEndpointService _endpoints;
    private readonly IReadOnlyDictionary<string, ISupporterProviderProvisioner> _provisioners;

    public SupporterConnectionService(
        IApplicationDbContext db,
        IEnumerable<ISupporterSource> sources,
        ITokenProtector protector,
        IInboundWebhookEndpointService endpoints,
        IEnumerable<ISupporterProviderProvisioner> provisioners
    )
    {
        _db = db;
        _sources = sources.ToDictionary(s => s.SourceKey, StringComparer.OrdinalIgnoreCase);
        _protector = protector;
        _endpoints = endpoints;
        _provisioners = provisioners.ToDictionary(
            p => p.SourceKey,
            StringComparer.OrdinalIgnoreCase
        );
    }

    /// <summary>The per-connection AEAD context: subject = the tenant, provider = the source, one field role.</summary>
    internal static TokenProtectionContext SecretContext(Guid broadcasterId, string sourceKey) =>
        new(broadcasterId.ToString(), sourceKey, "auth_secret");

    public async Task<Result<IReadOnlyList<SupporterConnectionDto>>> ListAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        List<SupporterConnection> connections = await _db
            .SupporterConnections.Where(c => c.BroadcasterId == broadcasterId)
            .OrderBy(c => c.SourceKey)
            .ToListAsync(ct);

        List<SupporterConnectionDto> items = [];
        foreach (SupporterConnection connection in connections)
            items.Add(await ToDtoAsync(connection, ct));
        return Result.Success<IReadOnlyList<SupporterConnectionDto>>(items);
    }

    public async Task<Result<SupporterConnectionDto>> UpsertAsync(
        Guid broadcasterId,
        Guid actorUserId,
        UpsertSupporterConnectionRequest request,
        CancellationToken ct = default
    )
    {
        string sourceKey = request.SourceKey?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!_sources.TryGetValue(sourceKey, out ISupporterSource? source))
            return Result.Failure<SupporterConnectionDto>(
                $"Unknown supporter source '{request.SourceKey}'.",
                "VALIDATION_FAILED"
            );

        string mode = request.ConnectionMode?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!string.Equals(mode, source.Capabilities.ConnectionMode, StringComparison.Ordinal))
            return Result.Failure<SupporterConnectionDto>(
                $"'{sourceKey}' ingests via '{source.Capabilities.ConnectionMode}', not '{mode}'.",
                "VALIDATION_FAILED"
            );

        // Revive a soft-deleted row rather than orphaning it (the unique index is filtered on DeletedAt IS NULL).
        SupporterConnection? connection = await _db
            .SupporterConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c => c.BroadcasterId == broadcasterId && c.SourceKey == sourceKey,
                ct
            );

        if (connection is null)
        {
            connection = new SupporterConnection
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = broadcasterId,
                SourceKey = sourceKey,
            };
            _db.SupporterConnections.Add(connection);
        }

        connection.DeletedAt = null;
        connection.ConnectionMode = mode;
        connection.IntegrationConnectionId = request.IntegrationConnectionId;
        connection.IsEnabled = request.IsEnabled;
        if (string.IsNullOrEmpty(connection.Status))
            connection.Status = "idle";

        if (mode == "webhook")
        {
            // One-step connect: the secret passes straight through to the provider's inbound endpoint —
            // auto-provisioned on first supply, secret-rotated on a later one — and is NEVER stored here.
            // An omitted secret just toggles the connection; the endpoint (if any) is untouched.
            if (!string.IsNullOrWhiteSpace(request.AuthSecret))
            {
                Result provisioned = await ProvisionEndpointAsync(
                    broadcasterId,
                    actorUserId,
                    connection,
                    sourceKey,
                    request.AuthSecret,
                    ct
                );
                if (provisioned.IsFailure)
                    return Result.Failure<SupporterConnectionDto>(
                        provisioned.ErrorMessage!,
                        provisioned.ErrorCode!
                    );
            }
            // OAuth-provisioned providers (Patreon) go further: the BOT registers the webhook on the
            // provider's side and the endpoint verifies with the secret the PROVIDER mints — the streamer
            // supplies no secret at all, just the OAuth connection.
            else if (
                request.IntegrationConnectionId is Guid oauthConnectionId
                && _provisioners.TryGetValue(
                    sourceKey,
                    out ISupporterProviderProvisioner? provisioner
                )
            )
            {
                Result provisioned = await ProvisionViaProviderAsync(
                    broadcasterId,
                    actorUserId,
                    connection,
                    sourceKey,
                    oauthConnectionId,
                    provisioner,
                    ct
                );
                if (provisioned.IsFailure)
                {
                    // The connect stays retryable: the row persists in error so a re-upsert re-provisions.
                    connection.Status = "error";
                    await _db.SaveChangesAsync(ct);
                    return Result.Failure<SupporterConnectionDto>(
                        provisioned.ErrorMessage!,
                        provisioned.ErrorCode!
                    );
                }
            }
        }
        // A socket/ws/poll provider's key is AEAD-sealed here for the ingress runner to open. An omitted
        // secret on a re-upsert keeps the stored one (toggling IsEnabled must never wipe the credential).
        else if (!string.IsNullOrWhiteSpace(request.AuthSecret))
        {
            connection.AuthSecretCipher = await _protector.ProtectAsync(
                request.AuthSecret,
                SecretContext(broadcasterId, sourceKey),
                ct
            );
        }

        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToDtoAsync(connection, ct));
    }

    /// <summary>
    /// Creates (first secret) or secret-rotates (later secret) the connection's inbound-webhook endpoint —
    /// the one-step connect: no separate trip to the Webhooks page, and the ingest URL rides back on the DTO.
    /// </summary>
    private async Task<Result> ProvisionEndpointAsync(
        Guid broadcasterId,
        Guid actorUserId,
        SupporterConnection connection,
        string sourceKey,
        string verificationSecret,
        CancellationToken ct
    )
    {
        WebhookAdapterKind? adapter = SupporterWebhookAdapters.AdapterFor(sourceKey);
        if (adapter is not WebhookAdapterKind kind)
            return Result.Failure(
                $"'{sourceKey}' has no inbound webhook adapter to provision.",
                "VALIDATION_FAILED"
            );

        if (connection.InboundWebhookEndpointId is Guid endpointId)
        {
            Result<InboundWebhookEndpointDto> rotated = await _endpoints.UpdateAsync(
                broadcasterId,
                endpointId,
                new UpdateInboundWebhookRequest { VerificationSecret = verificationSecret },
                ct
            );
            return rotated.IsFailure
                ? Result.Failure(rotated.ErrorMessage!, rotated.ErrorCode!)
                : Result.Success();
        }

        Result<InboundWebhookEndpointDto> created = await _endpoints.CreateAsync(
            broadcasterId,
            actorUserId,
            new CreateInboundWebhookRequest
            {
                Name = $"{sourceKey} (supporters)",
                Adapter = kind,
                VerificationSecret = verificationSecret,
                IsEnabled = true,
            },
            ct
        );
        if (created.IsFailure)
            return Result.Failure(created.ErrorMessage!, created.ErrorCode!);

        connection.InboundWebhookEndpointId = created.Value.Id;
        return Result.Success();
    }

    /// <summary>
    /// The full provider-side connect (Patreon): ensure our inbound endpoint exists (a random placeholder
    /// secret until the provider mints the real one), then have the provisioner register the provider-side
    /// webhook against its ingest URL and seal the provider's secret onto it.
    /// </summary>
    private async Task<Result> ProvisionViaProviderAsync(
        Guid broadcasterId,
        Guid actorUserId,
        SupporterConnection connection,
        string sourceKey,
        Guid oauthConnectionId,
        ISupporterProviderProvisioner provisioner,
        CancellationToken ct
    )
    {
        string ingestUrl;
        Guid endpointId;
        if (connection.InboundWebhookEndpointId is Guid existingId)
        {
            Result<InboundWebhookEndpointDto> existing = await _endpoints.GetAsync(
                broadcasterId,
                existingId,
                ct
            );
            if (existing.IsFailure)
                return Result.Failure(existing.ErrorMessage!, existing.ErrorCode!);
            endpointId = existingId;
            ingestUrl = existing.Value.IngestUrl;
        }
        else
        {
            WebhookAdapterKind? adapter = SupporterWebhookAdapters.AdapterFor(sourceKey);
            if (adapter is not WebhookAdapterKind kind)
                return Result.Failure(
                    $"'{sourceKey}' has no inbound webhook adapter to provision.",
                    "VALIDATION_FAILED"
                );

            // A random placeholder secret: the endpoint must never be verifiable before the provider's real
            // secret lands (an unguessable value fails every HMAC check until then).
            string placeholder = Convert.ToHexStringLower(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)
            );
            Result<InboundWebhookEndpointDto> created = await _endpoints.CreateAsync(
                broadcasterId,
                actorUserId,
                new CreateInboundWebhookRequest
                {
                    Name = $"{sourceKey} (supporters)",
                    Adapter = kind,
                    VerificationSecret = placeholder,
                    IsEnabled = true,
                },
                ct
            );
            if (created.IsFailure)
                return Result.Failure(created.ErrorMessage!, created.ErrorCode!);
            connection.InboundWebhookEndpointId = created.Value.Id;
            endpointId = created.Value.Id;
            ingestUrl = created.Value.IngestUrl;
        }

        return await provisioner.ProvisionAsync(
            broadcasterId,
            oauthConnectionId,
            endpointId,
            ingestUrl,
            ct
        );
    }

    /// <summary>The public DTO, with the provisioned endpoint's ingest URL resolved for webhook providers.</summary>
    private async Task<SupporterConnectionDto> ToDtoAsync(
        SupporterConnection connection,
        CancellationToken ct
    )
    {
        string? endpointUrl = null;
        if (connection.InboundWebhookEndpointId is Guid endpointId)
        {
            Result<InboundWebhookEndpointDto> endpoint = await _endpoints.GetAsync(
                connection.BroadcasterId,
                endpointId,
                ct
            );
            if (endpoint.IsSuccess)
                endpointUrl = endpoint.Value.IngestUrl;
        }

        return new SupporterConnectionDto(
            connection.SourceKey,
            connection.ConnectionMode,
            connection.AuthSecretCipher != null || connection.InboundWebhookEndpointId != null,
            connection.IsEnabled,
            connection.Status,
            connection.LastEventAt,
            endpointUrl
        );
    }

    public async Task<Result> DeleteAsync(
        Guid broadcasterId,
        Guid actorUserId,
        string sourceKey,
        CancellationToken ct = default
    )
    {
        string key = sourceKey?.Trim().ToLowerInvariant() ?? string.Empty;
        SupporterConnection? connection = await _db.SupporterConnections.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.SourceKey == key,
            ct
        );
        if (connection is null)
            return Result.Failure($"No '{sourceKey}' supporter connection.", "NOT_FOUND");

        // A one-step-provisioned endpoint belongs to this connection — disconnecting retires it too, so the
        // dead ingest URL stops resolving. Endpoints created manually on the Webhooks page are never linked
        // here and stay untouched.
        if (connection.InboundWebhookEndpointId is Guid endpointId)
            await _endpoints.DeleteAsync(broadcasterId, endpointId, ct);

        _db.SupporterConnections.Remove(connection); // Soft delete via the interceptor.
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<PagedList<SupporterEventDto>>> ListEventsAsync(
        Guid broadcasterId,
        SupporterEventQuery query,
        CancellationToken ct = default
    )
    {
        int page = query.Page < 1 ? 1 : query.Page;
        int pageSize = query.PageSize is < 1 or > MaxPageSize ? 25 : query.PageSize;

        IQueryable<SupporterEvent> q = _db.SupporterEvents.Where(e =>
            e.BroadcasterId == broadcasterId
        );
        if (!string.IsNullOrWhiteSpace(query.Kind))
        {
            string kind = query.Kind.Trim().ToLowerInvariant();
            q = q.Where(e => e.Kind == kind);
        }
        if (!string.IsNullOrWhiteSpace(query.SourceKey))
        {
            string src = query.SourceKey.Trim().ToLowerInvariant();
            q = q.Where(e => e.SourceKey == src);
        }

        int total = await q.CountAsync(ct);
        List<SupporterEventDto> items = await q.OrderByDescending(e => e.ReceivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new SupporterEventDto(
                e.Id,
                e.SourceKey,
                e.Kind,
                e.SupporterDisplayName,
                e.AmountMinor,
                e.Currency,
                e.Tier,
                e.Quantity,
                e.MessageText,
                e.IsRecurring,
                e.ReceivedAt
            ))
            .ToListAsync(ct);

        return Result.Success(new PagedList<SupporterEventDto>(items, page, pageSize, total));
    }
}
