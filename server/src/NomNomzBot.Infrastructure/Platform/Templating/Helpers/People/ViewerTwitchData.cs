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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

/// <summary>
/// The Twitch side of one person: the user record, the chat colour, the channel and the live stream. Each is
/// one call per render, made only when a row asks. A person without a Twitch id reads as null everywhere.
/// </summary>
internal sealed class ViewerTwitchData
{
    private readonly Lazy<Task<TwitchUser?>> _user;
    private readonly Lazy<Task<string?>> _color;
    private readonly Lazy<Task<TwitchChannelInformation?>> _channel;
    private readonly Lazy<Task<TwitchStream?>> _stream;

    public ViewerTwitchData(IServiceScopeFactory scopes, ViewerRef who)
    {
        _user = new Lazy<Task<TwitchUser?>>(() => LoadUserAsync(scopes, who));
        _color = new Lazy<Task<string?>>(() => LoadColorAsync(scopes, who));
        _channel = new Lazy<Task<TwitchChannelInformation?>>(() => LoadChannelAsync(scopes, who));
        _stream = new Lazy<Task<TwitchStream?>>(() => LoadStreamAsync(scopes, who));
    }

    public Task<TwitchUser?> UserAsync() => _user.Value;

    /// <summary>The chat colour as Twitch holds it (<c>#RRGGBB</c>); null when the person never set one.</summary>
    public Task<string?> ColorAsync() => _color.Value;

    public Task<TwitchChannelInformation?> ChannelAsync() => _channel.Value;

    /// <summary>The running stream of the person; null when they are offline.</summary>
    public Task<TwitchStream?> StreamAsync() => _stream.Value;

    private static async Task<TwitchUser?> LoadUserAsync(IServiceScopeFactory scopes, ViewerRef who)
    {
        if (who.PlatformUserId is not { } id)
            return null;

        using IServiceScope scope = scopes.CreateScope();
        ITwitchUsersApi users = scope.ServiceProvider.GetRequiredService<ITwitchUsersApi>();
        Result<IReadOnlyList<TwitchUser>> found = await users.GetUsersByIdsAsync([id]);
        return found.IsSuccess ? found.Value.FirstOrDefault() : null;
    }

    private static async Task<string?> LoadColorAsync(IServiceScopeFactory scopes, ViewerRef who)
    {
        if (who.PlatformUserId is not { } id)
            return null;

        using IServiceScope scope = scopes.CreateScope();
        ITwitchChatApi chat = scope.ServiceProvider.GetRequiredService<ITwitchChatApi>();
        Result<IReadOnlyList<TwitchUserChatColor>> found = await chat.GetUserChatColorsAsync([id]);
        string? color = found.IsSuccess ? found.Value.FirstOrDefault()?.Color : null;
        return string.IsNullOrEmpty(color) ? null : color;
    }

    private static async Task<TwitchChannelInformation?> LoadChannelAsync(
        IServiceScopeFactory scopes,
        ViewerRef who
    )
    {
        if (who.PlatformUserId is not { } id)
            return null;

        using IServiceScope scope = scopes.CreateScope();
        ITwitchChannelsApi channels =
            scope.ServiceProvider.GetRequiredService<ITwitchChannelsApi>();
        Result<IReadOnlyList<TwitchChannelInformation>> found =
            await channels.GetChannelInformationByTwitchIdsAsync([id]);
        return found.IsSuccess ? found.Value.FirstOrDefault() : null;
    }

    private static async Task<TwitchStream?> LoadStreamAsync(
        IServiceScopeFactory scopes,
        ViewerRef who
    )
    {
        if (who.PlatformUserId is not { } id)
            return null;

        using IServiceScope scope = scopes.CreateScope();
        ITwitchStreamsApi streams = scope.ServiceProvider.GetRequiredService<ITwitchStreamsApi>();
        Result<TwitchPage<TwitchStream>> found = await streams.GetStreamsAsync(
            new TwitchStreamsFilter(UserIds: [id]),
            new TwitchPageRequest(PageSize: 1)
        );
        return found.IsSuccess ? found.Value.Items.FirstOrDefault() : null;
    }
}
