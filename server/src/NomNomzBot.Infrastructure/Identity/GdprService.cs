// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// GDPR compliance service: data export (right of access) and deletion (right to erasure).
///
/// Export includes: profile, chat messages, moderation history, and the vaulted OAuth connections the user
/// established (provider/status/scopes only — never the token ciphertext).
/// Deletion: hard-deletes chat/records, anonymizes the profile, and revokes the user's vaulted OAuth
/// connections through <see cref="IIntegrationTokenVault"/> so the stored tokens are actually cleared.
/// </summary>
public sealed class GdprService : IGdprService
{
    private readonly IApplicationDbContext _db;
    private readonly IIntegrationTokenVault _vault;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GdprService> _logger;

    public GdprService(
        IApplicationDbContext db,
        IIntegrationTokenVault vault,
        TimeProvider timeProvider,
        ILogger<GdprService> logger
    )
    {
        _db = db;
        _vault = vault;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Exports all personal data for a user as a JSON document.
    /// Returns the raw JSON string suitable for file download.
    /// </summary>
    public async Task<Result<string>> ExportUserDataAsync(
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(userId, out Guid userGuid))
            return Result.Failure<string>($"User '{userId}' was not found.", "NOT_FOUND");

        User? user = await _db
            .Users.Include(u => u.Pronoun)
            .FirstOrDefaultAsync(u => u.Id == userGuid, cancellationToken);

        if (user is null)
            return Result.Failure<string>($"User '{userId}' was not found.", "NOT_FOUND");

        // Collect personal data across entities
        var chatMessages = await _db
            .ChatMessages.Where(m => m.UserId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new
            {
                m.Id,
                m.BroadcasterId,
                m.Message,
                m.MessageType,
                m.CreatedAt,
            })
            .Take(10_000)
            .ToListAsync(cancellationToken);

        var records = await _db
            .Records.Where(r => r.UserId == userId)
            .Select(r => new
            {
                r.BroadcasterId,
                r.RecordType,
                r.Data,
                r.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        // Service.UserId stores the external Twitch user id (not the internal key), so a user's
        // connected integrations are matched by their TwitchUserId.
        string twitchUserId = user.TwitchUserId;
        var services = await _db
            .Services.Where(s => s.UserId == twitchUserId)
            .Select(s => new
            {
                s.Name,
                s.BroadcasterId,
                s.Scopes,
                s.TokenExpiry,
            })
            .ToListAsync(cancellationToken);

        // The vaulted OAuth connections the user established (no secrets — provider/status/scopes only).
        var connections = await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c => c.ConnectedByUserId == userGuid && c.DeletedAt == null)
            .Select(c => new
            {
                c.Provider,
                c.ProviderAccountName,
                c.Status,
                c.Scopes,
                c.ConnectedAt,
            })
            .ToListAsync(cancellationToken);

        var export = new
        {
            ExportedAt = _timeProvider.GetUtcNow().UtcDateTime,
            ExportedForUserId = userId,
            Profile = new
            {
                user.Id,
                user.Username,
                user.DisplayName,
                user.ProfileImageUrl,
                user.BroadcasterType,
                Pronoun = user.Pronoun?.Name,
                user.CreatedAt,
                user.UpdatedAt,
            },
            ChatMessages = chatMessages,
            Records = records,
            ConnectedServices = services,
            Connections = connections,
        };

        string json = JsonSerializer.Serialize(
            export,
            new JsonSerializerOptions { WriteIndented = true }
        );

        _logger.LogInformation("GDPR: Exported data for user {UserId}", userId);
        return Result.Success(json);
    }

    /// <summary>
    /// Deletes all personal data for a user (right to erasure / right to be forgotten).
    ///
    /// Deleted: chat messages (hard delete), records, service tokens.
    /// Soft-deleted: user profile (marked as disabled with anonymized fields).
    ///
    /// Note: Some data may be retained for legal/compliance if the channel's
    /// retention policy requires it (future enhancement).
    /// </summary>
    public async Task<Result> DeleteUserDataAsync(
        string userId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(userId, out Guid userGuid))
            return Result.Failure($"User '{userId}' was not found.", "NOT_FOUND");

        User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userGuid, cancellationToken);

        if (user is null)
            return Result.Failure($"User '{userId}' was not found.", "NOT_FOUND");

        // Hard delete: chat messages
        List<ChatMessage> messages = await _db
            .ChatMessages.Where(m => m.UserId == userId)
            .ToListAsync(cancellationToken);
        _db.ChatMessages.RemoveRange(messages);

        // Hard delete: records
        List<Record> records = await _db
            .Records.Where(r => r.UserId == userId)
            .ToListAsync(cancellationToken);
        _db.Records.RemoveRange(records);

        // Hard delete: legacy service tokens (orphaned for Twitch, still live for Discord/Spotify).
        // Service.UserId stores the external Twitch user id, so match by the user's TwitchUserId.
        string twitchUserId = user.TwitchUserId;
        List<Service> services = await _db
            .Services.Where(s => s.UserId == twitchUserId)
            .ToListAsync(cancellationToken);
        _db.Services.RemoveRange(services);

        // Revoke the user's vaulted OAuth connections — the real token store. RevokeConnectionAsync
        // soft-deletes the IntegrationToken ciphertext and flips the connection to revoked, which is what
        // makes erasure actually clear the OAuth tokens (the legacy Service rows above are orphaned for Twitch).
        List<Guid> connectionIds = await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c => c.ConnectedByUserId == userGuid && c.DeletedAt == null)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        foreach (Guid connectionId in connectionIds)
            await _vault.RevokeConnectionAsync(connectionId, "gdpr_erasure", cancellationToken);

        // Anonymize user profile instead of hard deleting (preserves referential integrity). The normalized
        // username is an indexed lookup column, so it must be anonymized too or it leaks the original login.
        user.Username = $"deleted_{userId}";
        user.UsernameNormalized = $"deleted_{userId}";
        user.DisplayName = "Deleted User";
        user.ProfileImageUrl = null;
        user.Description = null;
        user.Color = null;
        user.Enabled = false;

        // Log deletion audit (hash the userId for privacy — audit trail without storing PII)
        string idHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(userId))
        )[..16];

        _db.DeletionAuditLogs.Add(
            new()
            {
                RequestType = "GDPR_ERASURE",
                SubjectIdHash = idHash,
                RequestedBy = userId,
                TablesAffected =
                [
                    "ChatMessages",
                    "Records",
                    "Services",
                    "IntegrationConnections",
                    "Users",
                ],
                RowsDeleted = messages.Count + records.Count + services.Count + connectionIds.Count,
                CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
                CompletedAt = _timeProvider.GetUtcNow().UtcDateTime,
            }
        );

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("GDPR: Deleted personal data for user {UserId}", userId);
        return Result.Success();
    }
}
