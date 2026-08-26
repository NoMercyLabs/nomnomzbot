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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Discord.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Discord;

/// <summary>
/// The both-opt-in handshake + connection lifecycle (discord.md §3.1). The bot OAuth token is custodied through
/// <see cref="IIntegrationTokenVault"/> only — this service never writes a plaintext token column. A link is
/// active only when the server admin approved AND the streamer enabled it; crossing into that state publishes
/// <see cref="DiscordGuildLinkedEvent"/>, leaving it publishes <see cref="DiscordGuildUnlinkedEvent"/>.
/// </summary>
public sealed class DiscordGuildService : IDiscordGuildService
{
    private const string Provider = "discord";
    private const string Approved = "approved";
    private const string Pending = "pending";
    private const string Revoked = "revoked";

    /// <summary>The pipeline action that posts to the linked guild; it is dead the moment the guild is gone.</summary>
    private const string DiscordNotificationActionType = "send_discord_notification";

    private readonly IApplicationDbContext _db;
    private readonly IIntegrationTokenVault _vault;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly IPipelineStepReferenceScanner _stepReferences;

    public DiscordGuildService(
        IApplicationDbContext db,
        IIntegrationTokenVault vault,
        IUnitOfWork unitOfWork,
        IEventBus eventBus,
        TimeProvider timeProvider,
        IPipelineStepReferenceScanner stepReferences
    )
    {
        _db = db;
        _vault = vault;
        _unitOfWork = unitOfWork;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _stepReferences = stepReferences;
    }

    public async Task<Result<IReadOnlyList<DiscordGuildConnectionDto>>> GetConnectionsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        List<DiscordGuildConnection> connections = await _db
            .DiscordGuildConnections.Where(c => c.BroadcasterId == broadcasterId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        IReadOnlyList<DiscordGuildConnectionDto> dtos = [.. connections.Select(ToDto)];
        return Result.Success(dtos);
    }

    public async Task<Result<DiscordGuildConnectionDto>> GetConnectionAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    )
    {
        DiscordGuildConnection? connection = await FindAsync(broadcasterId, connectionId, ct);
        return connection is null
            ? Errors.NotFound<DiscordGuildConnectionDto>(
                "Discord connection",
                connectionId.ToString()
            )
            : Result.Success(ToDto(connection));
    }

    public async Task<Result<DiscordGuildConnectionDto>> UpsertFromOAuthAsync(
        Guid broadcasterId,
        DiscordGuildOAuthResult oauth,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(oauth.GuildId))
            return Result.Failure<DiscordGuildConnectionDto>(
                "The Discord authorization did not return a guild.",
                "VALIDATION_FAILED"
            );

        bool channelExists = await _db.Channels.AnyAsync(c => c.Id == broadcasterId, ct);
        if (!channelExists)
            return Errors.ChannelNotFound<DiscordGuildConnectionDto>(broadcasterId.ToString());

        // Retriable unit (Npgsql's retrying strategy rejects a bare Begin/Commit); the guild-linked event
        // publishes after the commit, so a retried attempt cannot announce a link twice.
        Result<(DiscordGuildConnection Connection, bool WasActive)> linked =
            await _unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    DiscordGuildConnection? connection =
                        await _db.DiscordGuildConnections.FirstOrDefaultAsync(
                            c => c.BroadcasterId == broadcasterId && c.GuildId == oauth.GuildId,
                            token
                        );

                    bool wasActive = connection is not null && IsActive(connection);
                    DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

                    if (connection is null)
                    {
                        connection = new()
                        {
                            Id = Guid.CreateVersion7(),
                            BroadcasterId = broadcasterId,
                            GuildId = oauth.GuildId,
                        };
                        _db.DiscordGuildConnections.Add(connection);
                    }

                    connection.GuildName = oauth.GuildName ?? connection.GuildName;
                    connection.BotInstalled = true;
                    // The OAuth bot-install carries the server admin's approval implicitly (they
                    // authorized the install).
                    connection.ServerConsentStatus = Approved;
                    connection.ApprovedByDiscordUserId =
                        oauth.InstalledByDiscordUserId ?? connection.ApprovedByDiscordUserId;
                    connection.ApprovedAt ??= now;

                    await _unitOfWork.SaveChangesAsync(token);

                    // Vault the bot OAuth token (no plaintext column). The connection upsert is
                    // idempotent on (BroadcasterId, Provider="discord", ProviderAccountId=GuildId).
                    Result<IntegrationConnectionDto> vaultConnection =
                        await _vault.UpsertConnectionAsync(
                            new(
                                broadcasterId,
                                Provider,
                                oauth.GuildId,
                                oauth.GuildName,
                                oauth.Scopes,
                                ClientId: null,
                                IsByok: false,
                                ConnectedByUserId: null,
                                SettingsJson: null
                            ),
                            token
                        );
                    if (vaultConnection.IsFailure)
                        return Result.Failure<(DiscordGuildConnection, bool)>(
                            vaultConnection.ErrorMessage,
                            vaultConnection.ErrorCode
                        );

                    Result storeTokens = await _vault.StoreTokensAsync(
                        vaultConnection.Value.Id,
                        new(oauth.AccessToken, oauth.RefreshToken, AppToken: null, oauth.ExpiresAt),
                        oauth.Scopes,
                        token
                    );
                    if (storeTokens.IsFailure)
                        return Result.Failure<(DiscordGuildConnection, bool)>(
                            storeTokens.ErrorMessage,
                            storeTokens.ErrorCode
                        );

                    return Result.Success((connection, wasActive));
                },
                ct,
                shouldCommit: link => link.IsSuccess
            );

        if (linked.IsFailure)
            return Result.Failure<DiscordGuildConnectionDto>(linked.ErrorMessage, linked.ErrorCode);

        (DiscordGuildConnection stored, bool wasActiveBefore) = linked.Value;
        if (!wasActiveBefore && IsActive(stored))
            await PublishLinkedAsync(stored, ct);

        return Result.Success(ToDto(stored));
    }

    public async Task<Result> ApproveServerConsentAsync(
        Guid broadcasterId,
        Guid connectionId,
        string approvedByDiscordUserId,
        CancellationToken ct = default
    )
    {
        DiscordGuildConnection? connection = await FindAsync(broadcasterId, connectionId, ct);
        if (connection is null)
            return Errors.NotFound<object>("Discord connection", connectionId.ToString());

        bool wasActive = IsActive(connection);
        connection.ServerConsentStatus = Approved;
        connection.ApprovedByDiscordUserId = approvedByDiscordUserId;
        connection.ApprovedAt = _timeProvider.GetUtcNow().UtcDateTime;

        await _db.SaveChangesAsync(ct);

        if (!wasActive && IsActive(connection))
            await PublishLinkedAsync(connection, ct);

        return Result.Success();
    }

    public async Task<Result> RevokeServerConsentAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    )
    {
        DiscordGuildConnection? connection = await FindAsync(broadcasterId, connectionId, ct);
        if (connection is null)
            return Errors.NotFound<object>("Discord connection", connectionId.ToString());

        bool wasActive = IsActive(connection);
        connection.ServerConsentStatus = Revoked;
        await _db.SaveChangesAsync(ct);

        if (wasActive)
            await PublishUnlinkedAsync(connection, "server_revoked", ct);

        return Result.Success();
    }

    public async Task<Result> SetStreamerEnabledAsync(
        Guid broadcasterId,
        Guid connectionId,
        bool enabled,
        CancellationToken ct = default
    )
    {
        DiscordGuildConnection? connection = await FindAsync(broadcasterId, connectionId, ct);
        if (connection is null)
            return Errors.NotFound<object>("Discord connection", connectionId.ToString());

        bool wasActive = IsActive(connection);
        connection.StreamerEnabled = enabled;
        await _db.SaveChangesAsync(ct);

        bool isActive = IsActive(connection);
        if (!wasActive && isActive)
            await PublishLinkedAsync(connection, ct);
        else if (wasActive && !isActive)
            await PublishUnlinkedAsync(connection, "streamer_disabled", ct);

        return Result.Success();
    }

    public async Task<Result> DisconnectAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    )
    {
        DiscordGuildConnection? connection = await FindAsync(broadcasterId, connectionId, ct);
        if (connection is null)
            return Result.Success(); // idempotent

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                // Cascade soft-delete the connection's configs + roles (the SoftDeleteInterceptor
                // stamps DeletedAt).
                List<DiscordNotificationConfig> configs = await _db
                    .DiscordNotificationConfigs.Where(c => c.GuildConnectionId == connectionId)
                    .ToListAsync(token);
                _db.DiscordNotificationConfigs.RemoveRange(configs);

                List<DiscordNotificationRole> roles = await _db
                    .DiscordNotificationRoles.Where(r => r.GuildConnectionId == connectionId)
                    .ToListAsync(token);
                _db.DiscordNotificationRoles.RemoveRange(roles);

                _db.DiscordGuildConnections.Remove(connection);
                await _unitOfWork.SaveChangesAsync(token);

                // Revoke the vaulted bot token (soft-deletes IntegrationTokens, Status=revoked).
                Guid? vaultConnectionId = await _db
                    .IntegrationConnections.IgnoreQueryFilters()
                    .Where(c =>
                        c.BroadcasterId == broadcasterId
                        && c.Provider == Provider
                        && c.ProviderAccountId == connection.GuildId
                    )
                    .Select(c => (Guid?)c.Id)
                    .FirstOrDefaultAsync(token);
                if (vaultConnectionId is { } vid)
                    await _vault.RevokeConnectionAsync(vid, "discord_disconnected", token);
            },
            ct
        );

        await PublishUnlinkedAsync(connection, "disconnected", ct);
        return Result.Success();
    }

    public async Task<Result<bool>> IsLinkActiveAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    )
    {
        DiscordGuildConnection? connection = await FindAsync(broadcasterId, connectionId, ct);
        return connection is null ? Result.Success(false) : Result.Success(IsActive(connection));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private Task<DiscordGuildConnection?> FindAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct
    ) =>
        _db.DiscordGuildConnections.FirstOrDefaultAsync(
            c => c.Id == connectionId && c.BroadcasterId == broadcasterId,
            ct
        );

    private static bool IsActive(DiscordGuildConnection c) =>
        c is { ServerConsentStatus: Approved, StreamerEnabled: true, DeletedAt: null };

    private Task PublishLinkedAsync(DiscordGuildConnection c, CancellationToken ct) =>
        _eventBus.PublishAsync(
            new DiscordGuildLinkedEvent
            {
                BroadcasterId = c.BroadcasterId,
                GuildConnectionId = c.Id,
                GuildId = c.GuildId,
                GuildName = c.GuildName ?? string.Empty,
            },
            ct
        );

    private Task PublishUnlinkedAsync(
        DiscordGuildConnection c,
        string reason,
        CancellationToken ct
    ) =>
        _eventBus.PublishAsync(
            new DiscordGuildUnlinkedEvent
            {
                BroadcasterId = c.BroadcasterId,
                GuildConnectionId = c.Id,
                GuildId = c.GuildId,
                Reason = reason,
            },
            ct
        );

    private static DiscordGuildConnectionDto ToDto(DiscordGuildConnection c) =>
        new(
            c.Id,
            c.BroadcasterId,
            c.GuildId,
            c.GuildName,
            c.BotInstalled,
            c.ServerConsentStatus,
            c.ApprovedByDiscordUserId,
            c.ApprovedAt,
            c.StreamerEnabled,
            IsActive(c),
            c.CreatedAt,
            c.UpdatedAt
        );

    /// <summary>
    /// Counts what STOPS WORKING on disconnect, not just the rows removed: the notification rules and role
    /// buttons cascade with the connection, and every Discord pipeline step loses its destination.
    /// </summary>
    public async Task<Result<BlastRadiusDto>> GetDisconnectBlastRadiusAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    )
    {
        DiscordGuildConnection? connection = await FindAsync(broadcasterId, connectionId, ct);
        if (connection is null)
            return Result<BlastRadiusDto>.Failure(
                $"Discord connection '{connectionId}' was not found.",
                "NOT_FOUND"
            );

        int rules = await _db.DiscordNotificationConfigs.CountAsync(
            config => config.GuildConnectionId == connectionId,
            ct
        );
        int roleButtons = await _db.DiscordNotificationRoles.CountAsync(
            role => role.GuildConnectionId == connectionId,
            ct
        );

        Result<PipelineStepReferenceScan> scan = await _stepReferences.CountByActionTypesAsync(
            broadcasterId,
            [DiscordNotificationActionType],
            ct
        );
        if (scan.IsFailure)
            return Result<BlastRadiusDto>.Failure(
                scan.ErrorMessage ?? "The reference scan failed.",
                scan.ErrorCode ?? "SCAN_FAILED"
            );

        List<BlastRadiusCategoryDto> categories = [];
        if (rules > 0)
            categories.Add(
                new BlastRadiusCategoryDto(
                    BlastRadiusCategoryKeys.DiscordNotificationRules,
                    rules,
                    []
                )
            );
        if (roleButtons > 0)
            categories.Add(
                new BlastRadiusCategoryDto(
                    BlastRadiusCategoryKeys.DiscordRoleButtons,
                    roleButtons,
                    []
                )
            );
        if (scan.Value.MatchCount > 0)
            categories.Add(
                new BlastRadiusCategoryDto(
                    BlastRadiusCategoryKeys.PipelineSteps,
                    scan.Value.MatchCount,
                    scan.Value.PipelineNames
                )
            );

        return Result<BlastRadiusDto>.Success(new BlastRadiusDto(categories, scan.Value.IsMinimum));
    }
}
