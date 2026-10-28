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

namespace NomNomzBot.Application.Contracts.Discord;

/// <summary>
/// Notification rules — event → channel → template (discord.md §3.2). The only consumer of
/// <c>ConfigSchemaVersion</c> (the <c>EmbedConfig</c> upcast anchor): on read it forward-migrates a stale
/// <c>EmbedConfig</c> to the current shape; the upgrade persists on the next write.
/// </summary>
public interface IDiscordNotificationConfigService
{
    /// <summary>The events the server fires a Discord post for, with each one's placeholders.</summary>
    Result<IReadOnlyList<DiscordNotificationTriggerDto>> GetTriggers();

    /// <summary>All rules for one guild connection, tenant-scoped. Read-only.</summary>
    Task<Result<IReadOnlyList<DiscordNotificationConfigDto>>> GetConfigsAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Creates a rule for (GuildConnectionId, TriggerType). <c>ALREADY_EXISTS</c> if that pair exists.
    /// Validates target channel, ping-role ownership, and milestone fields. Persists via <c>IUnitOfWork</c>.
    /// </summary>
    Task<Result<DiscordNotificationConfigDto>> CreateConfigAsync(
        Guid broadcasterId,
        Guid connectionId,
        CreateDiscordNotificationConfigRequest request,
        CancellationToken ct = default
    );

    /// <summary>Updates an existing rule. <c>NOT_FOUND</c> if absent/other-tenant. Re-validates as Create.</summary>
    Task<Result<DiscordNotificationConfigDto>> UpdateConfigAsync(
        Guid broadcasterId,
        Guid configId,
        UpdateDiscordNotificationConfigRequest request,
        CancellationToken ct = default
    );

    /// <summary>Soft-deletes the rule. <c>NOT_FOUND</c> if absent.</summary>
    Task<Result> DeleteConfigAsync(
        Guid broadcasterId,
        Guid configId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Renders <c>MessageTemplate</c> + <c>EmbedConfig</c> against sample data WITHOUT posting. Pure; the
    /// dashboard preview button. No state change.
    /// </summary>
    Task<Result<DiscordNotificationPreviewDto>> PreviewAsync(
        Guid broadcasterId,
        Guid configId,
        CancellationToken ct = default
    );
}
