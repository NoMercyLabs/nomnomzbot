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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Emotes;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Chat;

/// <summary>The reads a chat or tts token can need; a row names the ones it uses so a render fetches each once.</summary>
[Flags]
internal enum ChatSources
{
    None = 0,
    Settings = 1,
    Pinned = 2,
    Shared = 4,
    Emotes = 8,

    /// <summary>The in-memory shared chat tracker; nothing to fetch ahead.</summary>
    Tracker = 16,

    /// <summary>The channel's pronunciation list; applied per text, nothing to fetch ahead.</summary>
    Lexicon = 32,
}

/// <summary>The answer of a read: <see cref="Ok"/> is false when the source failed, so the token prints as written.</summary>
internal readonly record struct ChatRead<T>(bool Ok, T? Value)
{
    public static ChatRead<T> Failed => new(false, default);

    public static ChatRead<T> Of(T? value) => new(true, value);
}

/// <summary>
/// The chat reads of ONE render. Each source is fetched at most once, however many tokens use it, and is kept
/// in the render's item bag so a later token finds the same result.
/// </summary>
internal sealed class ChatData
{
    private const string ItemKey = "chat.data";

    private readonly IServiceScopeFactory _scopes;
    private readonly Guid _broadcasterId;
    private readonly string? _twitchChannelId;
    private readonly EmoteData? _emotes;
    private readonly Lazy<Task<ChatRead<TwitchChatSettings>>> _settings;
    private readonly Lazy<Task<ChatRead<TwitchPinnedChatMessage>>> _pinned;
    private readonly Lazy<Task<ChatRead<IReadOnlyList<string>>>> _shared;
    private readonly Lazy<Task<HashSet<string>>> _emoteCodes;

    private ChatData(
        IServiceScopeFactory scopes,
        Guid broadcasterId,
        string? twitchChannelId,
        EmoteData? emotes
    )
    {
        _scopes = scopes;
        _broadcasterId = broadcasterId;
        _twitchChannelId = twitchChannelId;
        _emotes = emotes;
        _settings = new Lazy<Task<ChatRead<TwitchChatSettings>>>(LoadSettingsAsync);
        _pinned = new Lazy<Task<ChatRead<TwitchPinnedChatMessage>>>(LoadPinnedAsync);
        _shared = new Lazy<Task<ChatRead<IReadOnlyList<string>>>>(LoadSharedAsync);
        _emoteCodes = new Lazy<Task<HashSet<string>>>(LoadEmoteCodesAsync);
    }

    /// <summary>The reads of this render, or null when the render has no channel.</summary>
    public static ChatData? For(TemplateRenderScope scope, ChatDataSource source)
    {
        if (scope.BroadcasterId is not { } broadcasterId)
            return null;

        if (scope.Items.TryGetValue(ItemKey, out object? kept) && kept is ChatData data)
            return data;

        ChatData created = new(
            source.Scopes,
            broadcasterId,
            scope.Channel?.TwitchChannelId,
            EmoteData.For(scope, source.Scopes, source.Directory, source.EmoteChat)
        );
        scope.Items[ItemKey] = created;
        return created;
    }

    /// <summary>The channel's chat modes (slow, followers-only, subscribers-only, emote-only).</summary>
    public Task<ChatRead<TwitchChatSettings>> SettingsAsync() => _settings.Value;

    /// <summary>The pinned message; a successful read with no value means nothing is pinned.</summary>
    public Task<ChatRead<TwitchPinnedChatMessage>> PinnedAsync() => _pinned.Value;

    /// <summary>The display names of the other channels in the shared chat session; empty when not in one.</summary>
    public Task<ChatRead<IReadOnlyList<string>>> SharedChannelsAsync() => _shared.Value;

    /// <summary>Every emote code the channel can show; empty when the emote sources fail.</summary>
    public Task<HashSet<string>> EmoteCodesAsync() => _emoteCodes.Value;

    /// <summary>True when the tracker holds an active shared chat session for the channel (an in-memory read).</summary>
    public bool IsShared()
    {
        using IServiceScope scope = _scopes.CreateScope();
        return scope
            .ServiceProvider.GetRequiredService<ISharedChatSessionTracker>()
            .GetActiveSession(_broadcasterId)
            is not null;
    }

    /// <summary>The channel's pronunciation list applied to the text.</summary>
    public async Task<string> ApplyLexiconAsync(string text, CancellationToken ct)
    {
        using IServiceScope scope = _scopes.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<ITtsLexiconService>()
            .ApplyAsync(_broadcasterId, text, ct);
    }

    /// <summary>Starts the reads of the given sources side by side.</summary>
    public Task WarmAsync(ChatSources sources)
    {
        List<Task> started = [];
        if (sources.HasFlag(ChatSources.Settings))
            started.Add(SettingsAsync());
        if (sources.HasFlag(ChatSources.Pinned))
            started.Add(PinnedAsync());
        if (sources.HasFlag(ChatSources.Shared))
            started.Add(SharedChannelsAsync());
        if (sources.HasFlag(ChatSources.Emotes))
            started.Add(EmoteCodesAsync());
        return Task.WhenAll(started);
    }

    private async Task<ChatRead<TwitchChatSettings>> LoadSettingsAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        Result<TwitchChatSettings> found = await scope
            .ServiceProvider.GetRequiredService<ITwitchChatApi>()
            .GetChatSettingsAsync(_broadcasterId);
        return found.IsFailure
            ? ChatRead<TwitchChatSettings>.Failed
            : ChatRead<TwitchChatSettings>.Of(found.Value);
    }

    private async Task<ChatRead<TwitchPinnedChatMessage>> LoadPinnedAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        Result<TwitchPinnedChatMessage> found = await scope
            .ServiceProvider.GetRequiredService<ITwitchChatApi>()
            .GetPinnedMessagesAsync(_broadcasterId);
        if (found.IsSuccess)
            return ChatRead<TwitchPinnedChatMessage>.Of(found.Value);
        return found.ErrorCode == TwitchErrorCodes.NotFound
            ? ChatRead<TwitchPinnedChatMessage>.Of(null)
            : ChatRead<TwitchPinnedChatMessage>.Failed;
    }

    private async Task<ChatRead<IReadOnlyList<string>>> LoadSharedAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        Result<TwitchSharedChatSession> session = await scope
            .ServiceProvider.GetRequiredService<ITwitchChatAssetsApi>()
            .GetSharedChatSessionAsync(_broadcasterId);
        if (session.IsFailure)
            return session.ErrorCode == TwitchErrorCodes.NotFound
                ? ChatRead<IReadOnlyList<string>>.Of([])
                : ChatRead<IReadOnlyList<string>>.Failed;

        List<string> others =
        [
            .. session
                .Value.Participants.Select(p => p.BroadcasterId)
                .Where(id => id != _twitchChannelId),
        ];
        if (others.Count == 0)
            return ChatRead<IReadOnlyList<string>>.Of([]);

        Result<IReadOnlyList<TwitchUser>> users = await scope
            .ServiceProvider.GetRequiredService<ITwitchUsersApi>()
            .GetUsersByIdsAsync(others);
        return users.IsFailure
            ? ChatRead<IReadOnlyList<string>>.Failed
            : ChatRead<IReadOnlyList<string>>.Of([
                .. others
                    .Select(id => users.Value.FirstOrDefault(u => u.Id == id)?.DisplayName)
                    .OfType<string>(),
            ]);
    }

    private async Task<HashSet<string>> LoadEmoteCodesAsync() =>
        _emotes is null ? [] : [.. (await _emotes.AllAsync()).Select(emote => emote.Code)];
}
