// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Reads off-bot channels for signed-in viewers (chat-client.md "Any Twitch channel"). Each viewer gets one
/// <see cref="ExternalChatReader"/>: one EventSub session on the viewer's own token, shared by all the
/// viewer's connections. The reader is that session's sink, so the lines never reach the bot pipeline.
/// </summary>
public sealed class ExternalChatRelay : IExternalChatRelay
{
    private readonly IViewerEventSubSessions _sessions;
    private readonly IExternalChatPush _push;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ExternalChatRelay> _logger;
    private readonly ConcurrentDictionary<Guid, ExternalChatReader> _readers = new();
    private readonly ConcurrentDictionary<string, Guid> _connectionViewers = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ExternalChatRelay(
        IViewerEventSubSessions sessions,
        IExternalChatPush push,
        IServiceScopeFactory scopes,
        ILogger<ExternalChatRelay> logger
    )
    {
        _sessions = sessions;
        _push = push;
        _scopes = scopes;
        _logger = logger;
    }

    public async Task<Result> JoinAsync(
        Guid viewerUserId,
        string connectionId,
        string twitchBroadcasterId,
        CancellationToken ct = default
    )
    {
        await _gate.WaitAsync(ct);
        try
        {
            Result<ExternalChatReader> reader = await GetOrOpenReaderAsync(viewerUserId, ct);
            if (reader.IsFailure)
                return reader;

            _connectionViewers[connectionId] = viewerUserId;
            Result joined = await reader.Value.JoinAsync(connectionId, twitchBroadcasterId, ct);
            if (joined.IsFailure)
                await RemoveAsync(reader.Value, connectionId, twitchBroadcasterId);
            return joined;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task LeaveAsync(string connectionId, string twitchBroadcasterId)
    {
        await _gate.WaitAsync();
        try
        {
            if (
                _connectionViewers.TryGetValue(connectionId, out Guid viewer)
                && _readers.TryGetValue(viewer, out ExternalChatReader? reader)
            )
            {
                await RemoveAsync(reader, connectionId, twitchBroadcasterId);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DisconnectAsync(string connectionId)
    {
        await _gate.WaitAsync();
        try
        {
            if (
                _connectionViewers.TryRemove(connectionId, out Guid viewer)
                && _readers.TryGetValue(viewer, out ExternalChatReader? reader)
            )
            {
                reader.DropConnection(connectionId);
                await CloseWhenIdleAsync(reader);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Result<ExternalChatReader>> GetOrOpenReaderAsync(
        Guid viewerUserId,
        CancellationToken ct
    )
    {
        if (_readers.TryGetValue(viewerUserId, out ExternalChatReader? existing))
            return Result.Success(existing);

        string? viewerTwitchId = await ResolveTwitchUserIdAsync(viewerUserId, ct);
        if (viewerTwitchId is null)
            return Result.Failure<ExternalChatReader>(
                "Your Twitch account is not linked.",
                "NOT_FOUND"
            );

        ExternalChatReader reader = new(viewerUserId, viewerTwitchId, _sessions, _push, _logger);
        Result<string> opened = await _sessions.OpenAsync(reader.OwnerKey, reader, ct);
        if (opened.IsFailure)
        {
            await _sessions.CloseAsync(reader.OwnerKey);
            return Result.Failure<ExternalChatReader>(opened.ErrorMessage!, opened.ErrorCode);
        }

        reader.SessionId = opened.Value;
        _readers[viewerUserId] = reader;
        return Result.Success(reader);
    }

    private async Task<string?> ResolveTwitchUserIdAsync(Guid viewerUserId, CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
        ITwitchIdentityResolver identity =
            scope.ServiceProvider.GetRequiredService<ITwitchIdentityResolver>();
        return await identity.GetTwitchUserIdAsync(viewerUserId, ct);
    }

    private async Task RemoveAsync(
        ExternalChatReader reader,
        string connectionId,
        string twitchBroadcasterId
    )
    {
        reader.Leave(connectionId, twitchBroadcasterId);
        if (!reader.HasConnection(connectionId))
            _connectionViewers.TryRemove(connectionId, out _);
        await CloseWhenIdleAsync(reader);
    }

    private async Task CloseWhenIdleAsync(ExternalChatReader reader)
    {
        if (!reader.IsIdle)
            return;

        _readers.TryRemove(reader.ViewerUserId, out _);
        await _sessions.CloseAsync(reader.OwnerKey);
    }
}
