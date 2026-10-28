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

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Emotes;

/// <summary>The reads an emote token can need; a row names the ones it uses so a render fetches each once.</summary>
[Flags]
internal enum EmoteSources
{
    None = 0,
    Channel = 1,
    Global = 2,
    ThirdParty = 4,
    Cheermotes = 8,
    Usage = 16,
    Favorites = 32,
    Catalogue = Channel | Global | ThirdParty,
}

/// <summary>
/// The emote reads of ONE render. Each source is fetched at most once, however many tokens use it, and is kept
/// in the render's item bag so a later token finds the same result. A failed source reads as empty.
/// </summary>
internal sealed class EmoteData
{
    private const string ItemKey = "emote.data";

    /// <summary>How many of the newest chat lines the usage rows count (one read per render).</summary>
    public const int UsageWindow = 5000;

    private readonly IServiceScopeFactory _scopes;
    private readonly IViewerDirectory _directory;
    private readonly IEmoteChatSource _chat;
    private readonly Guid _broadcasterId;
    private readonly IReadOnlyDictionary<string, string> _seeds;
    private readonly Lazy<Task<IReadOnlyList<EmoteItem>>> _channel;
    private readonly Lazy<Task<IReadOnlyList<EmoteItem>>> _global;
    private readonly Lazy<Task<IReadOnlyList<EmoteItem>>> _thirdParty;
    private readonly Lazy<Task<IReadOnlyList<TwitchCheermote>>> _cheermotes;
    private readonly Lazy<Task<IReadOnlyList<EmoteChatLine>>> _lines;
    private readonly Lazy<Task<IReadOnlyList<FavoriteEmoteDto>>> _favorites;
    private readonly Lazy<Task<string?>> _viewerPlatformId;

    private EmoteData(
        IServiceScopeFactory scopes,
        IViewerDirectory directory,
        IEmoteChatSource chat,
        Guid broadcasterId,
        IReadOnlyDictionary<string, string> seeds
    )
    {
        _scopes = scopes;
        _directory = directory;
        _chat = chat;
        _broadcasterId = broadcasterId;
        _seeds = seeds;
        _channel = new Lazy<Task<IReadOnlyList<EmoteItem>>>(LoadChannelAsync);
        _global = new Lazy<Task<IReadOnlyList<EmoteItem>>>(LoadGlobalAsync);
        _thirdParty = new Lazy<Task<IReadOnlyList<EmoteItem>>>(LoadThirdPartyAsync);
        _cheermotes = new Lazy<Task<IReadOnlyList<TwitchCheermote>>>(LoadCheermotesAsync);
        _lines = new Lazy<Task<IReadOnlyList<EmoteChatLine>>>(LoadLinesAsync);
        _favorites = new Lazy<Task<IReadOnlyList<FavoriteEmoteDto>>>(LoadFavoritesAsync);
        _viewerPlatformId = new Lazy<Task<string?>>(LoadViewerPlatformIdAsync);
    }

    /// <summary>The render's one emote data; null when the render has no channel.</summary>
    public static EmoteData? For(
        TemplateRenderScope scope,
        IServiceScopeFactory scopes,
        IViewerDirectory directory,
        IEmoteChatSource chat
    )
    {
        if (scope.BroadcasterId is not { } broadcasterId)
            return null;

        if (scope.Items.TryGetValue(ItemKey, out object? kept) && kept is EmoteData data)
            return data;

        EmoteData created = new(scopes, directory, chat, broadcasterId, scope.Variables);
        scope.Items[ItemKey] = created;
        return created;
    }

    /// <summary>The channel's own Twitch emotes (subscriber, follower and bits tier).</summary>
    public Task<IReadOnlyList<EmoteItem>> ChannelAsync() => _channel.Value;

    /// <summary>Twitch's global emotes.</summary>
    public Task<IReadOnlyList<EmoteItem>> GlobalAsync() => _global.Value;

    /// <summary>The 7TV, BTTV and FFZ channel sets and globals the chat warmer keeps hot.</summary>
    public Task<IReadOnlyList<EmoteItem>> ThirdPartyAsync() => _thirdParty.Value;

    public Task<IReadOnlyList<TwitchCheermote>> CheermotesAsync() => _cheermotes.Value;

    /// <summary>The newest chat lines of the channel, newest first.</summary>
    public Task<IReadOnlyList<EmoteChatLine>> LinesAsync() => _lines.Value;

    /// <summary>The channel owner's favorite emotes, newest first.</summary>
    public Task<IReadOnlyList<FavoriteEmoteDto>> FavoritesAsync() => _favorites.Value;

    /// <summary>The viewer's id on the platform; null when the render names nobody.</summary>
    public Task<string?> ViewerPlatformIdAsync() => _viewerPlatformId.Value;

    /// <summary>Every emote the channel can show, the first one winning a repeated code.</summary>
    public async Task<IReadOnlyList<EmoteItem>> AllAsync()
    {
        Task<IReadOnlyList<EmoteItem>>[] sets = [ChannelAsync(), GlobalAsync(), ThirdPartyAsync()];
        IReadOnlyList<EmoteItem>[] loaded = await Task.WhenAll(sets);

        Dictionary<string, EmoteItem> byCode = new(StringComparer.Ordinal);
        foreach (EmoteItem emote in loaded.SelectMany(set => set))
            byCode.TryAdd(emote.Code, emote);
        return [.. byCode.Values];
    }

    /// <summary>The seed text of the command or event message; empty when the render has none.</summary>
    public string Message => _seeds.TryGetValue("message", out string? text) ? text : string.Empty;

    /// <summary>Starts the reads of the given sources side by side.</summary>
    public Task WarmAsync(EmoteSources sources)
    {
        List<Task> started = [];
        if (sources.HasFlag(EmoteSources.Channel))
            started.Add(ChannelAsync());
        if (sources.HasFlag(EmoteSources.Global))
            started.Add(GlobalAsync());
        if (sources.HasFlag(EmoteSources.ThirdParty))
            started.Add(ThirdPartyAsync());
        if (sources.HasFlag(EmoteSources.Cheermotes))
            started.Add(CheermotesAsync());
        if (sources.HasFlag(EmoteSources.Usage))
            started.Add(LinesAsync());
        if (sources.HasFlag(EmoteSources.Favorites))
            started.Add(FavoritesAsync());
        return Task.WhenAll(started);
    }

    private async Task<IReadOnlyList<EmoteItem>> LoadChannelAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        ITwitchChatAssetsApi assets =
            scope.ServiceProvider.GetRequiredService<ITwitchChatAssetsApi>();
        Result<IReadOnlyList<TwitchChannelEmote>> found = await assets.GetChannelEmotesAsync(
            _broadcasterId
        );
        return found.IsFailure
            ? []
            :
            [
                .. found.Value.Select(e => new EmoteItem(
                    e.Name,
                    EmoteOrigin.TwitchChannel,
                    true,
                    e.EmoteType,
                    e.Images.Url2x
                )),
            ];
    }

    private async Task<IReadOnlyList<EmoteItem>> LoadGlobalAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        ITwitchChatAssetsApi assets =
            scope.ServiceProvider.GetRequiredService<ITwitchChatAssetsApi>();
        Result<IReadOnlyList<TwitchGlobalEmote>> found = await assets.GetGlobalEmotesAsync();
        return found.IsFailure
            ? []
            :
            [
                .. found.Value.Select(e => new EmoteItem(
                    e.Name,
                    EmoteOrigin.TwitchGlobal,
                    false,
                    null,
                    e.Images.Url2x
                )),
            ];
    }

    private async Task<IReadOnlyList<EmoteItem>> LoadThirdPartyAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        IChatEmoteCatalogue catalogue =
            scope.ServiceProvider.GetRequiredService<IChatEmoteCatalogue>();
        Result<IReadOnlyList<ChatEmote>> found = await catalogue.GetForChannelAsync(
            _broadcasterId,
            Guid.Empty,
            ChatEmoteSender.Bot
        );
        return found.IsFailure
            ? []
            : [.. found.Value.Where(e => e.Provider != EmoteProvider.Twitch).Select(ToItem)];
    }

    private async Task<IReadOnlyList<TwitchCheermote>> LoadCheermotesAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        ITwitchBitsApi bits = scope.ServiceProvider.GetRequiredService<ITwitchBitsApi>();
        Result<IReadOnlyList<TwitchCheermote>> found = await bits.GetCheermotesAsync(
            _broadcasterId
        );
        return found.IsFailure ? [] : found.Value;
    }

    private async Task<IReadOnlyList<EmoteChatLine>> LoadLinesAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        return await _chat.RecentLinesAsync(
            scope.ServiceProvider,
            _broadcasterId,
            UsageWindow,
            CancellationToken.None
        );
    }

    private async Task<IReadOnlyList<FavoriteEmoteDto>> LoadFavoritesAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        Guid? owner = await _chat.OwnerUserIdAsync(
            scope.ServiceProvider,
            _broadcasterId,
            CancellationToken.None
        );
        if (owner is not { } ownerId)
            return [];

        IFavoriteEmoteService favorites =
            scope.ServiceProvider.GetRequiredService<IFavoriteEmoteService>();
        Result<IReadOnlyList<FavoriteEmoteDto>> found = await favorites.ListAsync(ownerId);
        return found.IsFailure ? [] : found.Value;
    }

    private async Task<string?> LoadViewerPlatformIdAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        ViewerRef? who = await _directory.ResolveAsync(
            scope.ServiceProvider,
            ViewerSide.User,
            _seeds,
            CancellationToken.None
        );
        return who?.PlatformUserId;
    }

    private static EmoteItem ToItem(ChatEmote emote) =>
        new(
            emote.Code,
            emote.Provider switch
            {
                EmoteProvider.SevenTv => EmoteOrigin.SevenTv,
                EmoteProvider.Bttv => EmoteOrigin.Bttv,
                _ => EmoteOrigin.Ffz,
            },
            emote.Origin == ChatEmoteOrigin.Channel,
            null,
            ImageOf(emote.Urls)
        );

    /// <summary>The medium picture (scale 2) when the network has one, else the largest it offers.</summary>
    private static string? ImageOf(IReadOnlyDictionary<string, string> urls) =>
        urls.TryGetValue("2", out string? medium)
            ? medium
            : urls.OrderByDescending(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value)
                .FirstOrDefault();
}
