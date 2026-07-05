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
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// A recording <see cref="IIntegrationTokenVault"/>: it captures every connection upsert, token store, and
/// revoke so a test can assert the bot token went to the vault (never a plaintext column). It returns a fresh
/// connection id from <see cref="UpsertConnectionAsync"/> so the caller's StoreTokens lands against it.
/// </summary>
internal sealed class RecordingVault : IIntegrationTokenVault
{
    public List<string> UpsertedProviders { get; } = [];
    public List<StoreTokensDto> StoredTokens { get; } = [];
    public List<string> RevokedReasons { get; } = [];
    public Guid LastUpsertedConnectionId { get; private set; }

    public Task<Result<IntegrationConnectionDto>> UpsertConnectionAsync(
        UpsertConnectionDto request,
        CancellationToken cancellationToken = default
    )
    {
        UpsertedProviders.Add(request.Provider);
        LastUpsertedConnectionId = Guid.CreateVersion7();
        return Task.FromResult(
            Result.Success(
                new IntegrationConnectionDto(
                    LastUpsertedConnectionId,
                    request.BroadcasterId,
                    request.Provider,
                    request.ProviderAccountId,
                    request.ProviderAccountName,
                    "connected",
                    request.Scopes,
                    request.IsByok,
                    null,
                    null,
                    0
                )
            )
        );
    }

    public Task<Result> StoreTokensAsync(
        Guid connectionId,
        StoreTokensDto tokens,
        IReadOnlyList<string>? grantedScopes = null,
        CancellationToken cancellationToken = default
    )
    {
        StoredTokens.Add(tokens);
        return Task.FromResult(Result.Success());
    }

    public Task<Result> RevokeConnectionAsync(
        Guid connectionId,
        string reason,
        CancellationToken cancellationToken = default
    )
    {
        RevokedReasons.Add(reason);
        return Task.FromResult(Result.Success());
    }

    public Task<Result<DecryptedTokenDto>> GetAccessTokenAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success(new DecryptedTokenDto("token", "access", null, false)));

    public Task<Result<DecryptedTokenDto>> GetRefreshTokenAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success(new DecryptedTokenDto("token", "refresh", null, false)));

    public Task<Result> MarkRefreshFailureAsync(
        Guid connectionId,
        string error,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success());

    public Task<Result<IReadOnlyList<IntegrationConnectionDto>>> ListConnectionsAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<IReadOnlyList<IntegrationConnectionDto>>([]));
}

/// <summary>
/// A recording <see cref="IDiscordBotGateway"/>: records every post / role change and replays a scripted
/// outcome, so dispatcher/role tests can assert WHAT was posted and to WHICH channel without a live Discord.
/// </summary>
internal sealed class RecordingGateway : IDiscordBotGateway
{
    public List<(string ChannelId, DiscordOutboundMessage Message)> Posts { get; } = [];
    public List<(string GuildId, string MemberId, string RoleId)> RoleAdds { get; } = [];
    public List<(string GuildId, string MemberId, string RoleId)> RoleRemoves { get; } = [];
    public List<(string ChannelId, DiscordOptInButton Button)> Buttons { get; } = [];

    public Result<string> NextPostResult { get; set; } = Result.Success("posted-msg-id");

    public Task<Result<string>> PostMessageAsync(
        Guid broadcasterId,
        string targetChannelId,
        DiscordOutboundMessage message,
        CancellationToken ct = default
    )
    {
        Posts.Add((targetChannelId, message));
        return Task.FromResult(NextPostResult);
    }

    public Task<Result<string>> PostButtonMessageAsync(
        Guid broadcasterId,
        string targetChannelId,
        DiscordOptInButton button,
        CancellationToken ct = default
    )
    {
        Buttons.Add((targetChannelId, button));
        return Task.FromResult(Result.Success("button-msg-id"));
    }

    public Task<Result> AddMemberRoleAsync(
        Guid broadcasterId,
        string guildId,
        string discordMemberId,
        string discordRoleId,
        CancellationToken ct = default
    )
    {
        RoleAdds.Add((guildId, discordMemberId, discordRoleId));
        return Task.FromResult(Result.Success());
    }

    public Task<Result> RemoveMemberRoleAsync(
        Guid broadcasterId,
        string guildId,
        string discordMemberId,
        string discordRoleId,
        CancellationToken ct = default
    )
    {
        RoleRemoves.Add((guildId, discordMemberId, discordRoleId));
        return Task.FromResult(Result.Success());
    }

    // ── Guild directory reads (scripted replay + recorded guild ids) ────────

    public List<string> GuildReads { get; } = [];
    public Result<DiscordGuildInfoDto> NextGuildResult { get; set; } =
        Result.Success(new DiscordGuildInfoDto("guild1", "Guild One", null, null));
    public Result<IReadOnlyList<DiscordGuildRoleDto>> NextGuildRolesResult { get; set; } =
        Result.Success<IReadOnlyList<DiscordGuildRoleDto>>([]);
    public Result<IReadOnlyList<DiscordGuildChannelDto>> NextGuildChannelsResult { get; set; } =
        Result.Success<IReadOnlyList<DiscordGuildChannelDto>>([]);

    public Task<Result<DiscordGuildInfoDto>> GetGuildAsync(
        Guid broadcasterId,
        string guildId,
        CancellationToken ct = default
    )
    {
        GuildReads.Add($"guild:{guildId}");
        return Task.FromResult(NextGuildResult);
    }

    public Task<Result<IReadOnlyList<DiscordGuildRoleDto>>> GetGuildRolesAsync(
        Guid broadcasterId,
        string guildId,
        CancellationToken ct = default
    )
    {
        GuildReads.Add($"roles:{guildId}");
        return Task.FromResult(NextGuildRolesResult);
    }

    public Task<Result<IReadOnlyList<DiscordGuildChannelDto>>> GetGuildChannelsAsync(
        Guid broadcasterId,
        string guildId,
        CancellationToken ct = default
    )
    {
        GuildReads.Add($"channels:{guildId}");
        return Task.FromResult(NextGuildChannelsResult);
    }
}
