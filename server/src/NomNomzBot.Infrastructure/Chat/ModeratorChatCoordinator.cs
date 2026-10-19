// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Services;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Holds one <see cref="ModeratorChatReader"/> per signed-in moderator and gives every moderated channel to
/// exactly one reader, so two moderators of one channel share one set of eight subscriptions.
/// </summary>
public sealed class ModeratorChatCoordinator : IModeratorChatReaders
{
    private const string UnmoddedReason =
        "Twitch no longer lists you as a moderator of this channel.";
    private const string NoTokenReason =
        "Your Twitch connection is missing or expired. Sign in again to read this channel.";

    private const string TenantFailedReason =
        "NomNomzBot could not prepare this channel for reading. It tries again on the next sync.";

    private static readonly string MissingScopeReason =
        $"Your Twitch connection lacks the {TwitchScopes.UserReadChat} permission. Allow chat reading to read this channel.";

    private static readonly string OverCapReason =
        $"Twitch carries at most {ModeratorChatReader.MaxChannels} channels on one connection; this channel is not read.";

    private readonly IViewerEventSubSessions _sessions;
    private readonly IEventSubNotificationSink _pipeline;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ModeratorChatCoordinator> _logger;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly Lock _state = new();

    private readonly Dictionary<Guid, ModeratorChatReader> _readers = [];
    private readonly Dictionary<string, ModeratorChatReaderStatus> _statuses = [];

    public ModeratorChatCoordinator(
        IViewerEventSubSessions sessions,
        IEventSubNotificationSink pipeline,
        IServiceScopeFactory scopes,
        ILogger<ModeratorChatCoordinator> logger
    )
    {
        _sessions = sessions;
        _pipeline = pipeline;
        _scopes = scopes;
        _logger = logger;
    }

    public IReadOnlyList<ModeratorChatReaderStatus> Snapshot()
    {
        lock (_state)
            return [.. _statuses.Values];
    }

    public async Task SyncUserAsync(Guid userId, CancellationToken ct = default)
    {
        await _sync.WaitAsync(ct);
        try
        {
            await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
            await SyncCoreAsync(userId, scope.ServiceProvider, ct);
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task SyncCoreAsync(Guid userId, IServiceProvider services, CancellationToken ct)
    {
        string? moderatorTwitchId = await services
            .GetRequiredService<ITwitchIdentityResolver>()
            .GetTwitchUserIdAsync(userId, ct);
        if (moderatorTwitchId is null)
        {
            await ReleaseAllAsync(userId, ModeratorChatReaderState.NoToken, NoTokenReason);
            return;
        }

        Result<IReadOnlyList<TwitchModeratedChannel>> listed = await services
            .GetRequiredService<IOperatorModeratedChannelResolver>()
            .ResolveAsync(userId, ct);
        if (listed.IsFailure)
        {
            if (listed.ErrorCode == TwitchErrorCodes.NoToken)
                await ReleaseAllAsync(userId, ModeratorChatReaderState.NoToken, NoTokenReason);
            else
                _logger.LogWarning(
                    "Moderator chat: listing the channels of {UserId} failed: {Error}",
                    userId,
                    listed.ErrorMessage
                );
            return;
        }

        // The moderator's own channel comes first in the list and is never read here.
        List<TwitchModeratedChannel> candidates =
        [
            .. listed.Value.Where(c => c.BroadcasterId != moderatorTwitchId),
        ];

        bool hasScope = await services
            .GetRequiredService<ITwitchTokenResolver>()
            .HasUserScopeAsync(userId, TwitchScopes.UserReadChat, ct);
        if (!hasScope)
        {
            await ReleaseAllAsync(
                userId,
                ModeratorChatReaderState.MissingScope,
                MissingScopeReason
            );
            foreach (TwitchModeratedChannel channel in candidates)
                SetStatusUnlessHeldByOther(
                    userId,
                    channel.BroadcasterId,
                    ModeratorChatReaderState.MissingScope,
                    MissingScopeReason
                );
            return;
        }

        await ReleaseLostAsync(userId, candidates);
        foreach (TwitchModeratedChannel channel in candidates)
            await ReadChannelAsync(userId, moderatorTwitchId, channel, services, ct);
    }

    private async Task ReadChannelAsync(
        Guid userId,
        string moderatorTwitchId,
        TwitchModeratedChannel channel,
        IServiceProvider services,
        CancellationToken ct
    )
    {
        string channelId = channel.BroadcasterId;
        if (IsHeld(channelId))
            return;

        Result<ChannelDto> tenant = await ProvisionTenantAsync(channel, services, ct);
        if (tenant.IsFailure)
        {
            _logger.LogWarning(
                "Moderator chat: preparing {Channel} for {UserId} failed: {Error}",
                channelId,
                userId,
                tenant.ErrorMessage
            );
            SetStatus(userId, channelId, ModeratorChatReaderState.TenantFailed, TenantFailedReason);
            return;
        }

        // A channel the bot already serves is read by the bot itself.
        if (tenant.Value.IsOnboarded)
        {
            ClearTenantFailed(channelId);
            return;
        }

        ModeratorChatReader? reader = ReaderOf(userId);
        if (reader is { ChannelCount: >= ModeratorChatReader.MaxChannels })
        {
            SetStatus(userId, channelId, ModeratorChatReaderState.OverCap, OverCapReason);
            return;
        }

        Result opened = await EnsureReaderAsync(userId, moderatorTwitchId, ct);
        if (opened.IsFailure)
        {
            SetStatus(userId, channelId, ModeratorChatReaderState.NoToken, opened.ErrorMessage);
            return;
        }

        reader = ReaderOf(userId)!;
        Result added = await reader.AddChannelAsync(channelId, ct);
        if (added.IsFailure)
        {
            _logger.LogWarning(
                "Moderator chat: subscribing {Channel} for {UserId} failed: {Error}",
                channelId,
                userId,
                added.ErrorMessage
            );
            SetStatus(userId, channelId, ModeratorChatReaderState.NoToken, added.ErrorMessage);
            return;
        }

        SetStatus(userId, channelId, ModeratorChatReaderState.Reading, null);
    }

    // Provisions the lightweight tenant the dashboard needs to admit the moderator (the owner's User row and the
    // Channel row) and returns the channel row, so the caller can tell whether the bot serves it.
    private static async Task<Result<ChannelDto>> ProvisionTenantAsync(
        TwitchModeratedChannel channel,
        IServiceProvider services,
        CancellationToken ct
    )
    {
        IUserService users = services.GetRequiredService<IUserService>();
        IChannelService channels = services.GetRequiredService<IChannelService>();

        Result<UserDto> owner = await users.GetOrCreateAsync(
            channel.BroadcasterId,
            channel.BroadcasterLogin,
            channel.BroadcasterName,
            cancellationToken: ct
        );
        if (owner.IsFailure)
            return owner.ToTyped<ChannelDto>();

        if (!Guid.TryParse(owner.Value.Id, out Guid ownerUserId))
            return Result.Failure<ChannelDto>(
                $"The owner id '{owner.Value.Id}' of {channel.BroadcasterLogin} is not a Guid."
            );

        Result<Guid> tenant = await channels.EnsureModeratedTenantAsync(
            channel.BroadcasterId,
            channel.BroadcasterLogin,
            channel.BroadcasterName,
            ownerUserId,
            ct
        );
        if (tenant.IsFailure)
            return tenant.ToTyped<ChannelDto>();

        return await channels.GetAsync(tenant.Value.ToString(), ct);
    }

    private async Task<Result> EnsureReaderAsync(
        Guid userId,
        string moderatorTwitchId,
        CancellationToken ct
    )
    {
        if (ReaderOf(userId) is not null)
            return Result.Success();

        ModeratorChatReader reader = new(
            userId,
            moderatorTwitchId,
            _sessions,
            _pipeline,
            (channel, state) => OnLostAsync(userId, channel, state),
            _logger
        );
        Result<string> session = await _sessions.OpenAsync(reader.OwnerKey, reader, ct);
        if (session.IsFailure)
            return session;

        reader.SessionId = session.Value;
        lock (_state)
            _readers[userId] = reader;
        return Result.Success();
    }

    private Task OnLostAsync(Guid userId, string channel, ModeratorChatReaderState state)
    {
        string reason = state == ModeratorChatReaderState.Unmodded ? UnmoddedReason : NoTokenReason;
        SetStatus(userId, channel, state, reason);
        return Task.CompletedTask;
    }

    private async Task ReleaseLostAsync(Guid userId, List<TwitchModeratedChannel> candidates)
    {
        ModeratorChatReader? reader = ReaderOf(userId);
        if (reader is null)
            return;

        HashSet<string> current = [.. candidates.Select(c => c.BroadcasterId)];
        foreach (string held in reader.HeldChannels().Where(h => !current.Contains(h)))
        {
            reader.Remove(held);
            SetStatus(userId, held, ModeratorChatReaderState.Unmodded, UnmoddedReason);
        }

        await CloseIfIdleAsync(userId, reader);
    }

    private async Task ReleaseAllAsync(Guid userId, ModeratorChatReaderState state, string reason)
    {
        ModeratorChatReader? reader = ReaderOf(userId);
        if (reader is null)
            return;

        foreach (string held in reader.HeldChannels())
        {
            reader.Remove(held);
            SetStatus(userId, held, state, reason);
        }

        await CloseIfIdleAsync(userId, reader);
    }

    private async Task CloseIfIdleAsync(Guid userId, ModeratorChatReader reader)
    {
        if (reader.ChannelCount > 0)
            return;

        lock (_state)
            _readers.Remove(userId);
        await _sessions.CloseAsync(reader.OwnerKey);
    }

    private ModeratorChatReader? ReaderOf(Guid userId)
    {
        lock (_state)
            return _readers.GetValueOrDefault(userId);
    }

    private bool IsHeld(string channelId)
    {
        lock (_state)
            return _readers.Values.Any(r => r.Holds(channelId));
    }

    private void SetStatus(
        Guid userId,
        string channelId,
        ModeratorChatReaderState state,
        string? reason
    )
    {
        lock (_state)
            _statuses[channelId] = new(channelId, state, userId, reason);
    }

    // A channel that failed to prepare and was onboarded since has nothing left to report.
    private void ClearTenantFailed(string channelId)
    {
        lock (_state)
            if (
                _statuses.GetValueOrDefault(channelId)?.State
                == ModeratorChatReaderState.TenantFailed
            )
                _statuses.Remove(channelId);
    }

    private void SetStatusUnlessHeldByOther(
        Guid userId,
        string channelId,
        ModeratorChatReaderState state,
        string reason
    )
    {
        if (!IsHeld(channelId))
            SetStatus(userId, channelId, state, reason);
    }
}
