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

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Discord;

/// <summary>
/// The link placeholders a Discord post can use: where the channel lives on its platform, the streamer's
/// picture and the live preview picture. The values come from the channel row and its owner, so a post
/// never needs a call to the platform. A value the platform does not have is an empty string, and an empty
/// link or picture field then drops out of the posted embed.
/// </summary>
internal static class DiscordChannelLinks
{
    public const string ChannelUrl = "channel.url";
    public const string StreamUrl = "stream.url";
    public const string ChannelLogin = "channel.login";
    public const string ChannelAvatar = "channel.avatar";
    public const string StreamThumbnail = "stream.thumbnail";

    private const string TwitchPreviewHost = "https://static-cdn.jtvnw.net/previews-ttv";

    /// <summary>
    /// The five link placeholders for the tenant's channel. <paramref name="liveSince"/> stamps the preview
    /// picture's address, because Discord keeps an embed picture by its exact address and the platform
    /// replaces the preview about every five minutes. An unknown channel gives no values.
    /// </summary>
    public static async Task<Dictionary<string, string>> LoadAsync(
        IApplicationDbContext db,
        Guid broadcasterId,
        DateTimeOffset liveSince,
        CancellationToken ct
    )
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

        ChannelLinkRow? channel = await db
            .Channels.Where(c => c.Id == broadcasterId)
            .Select(c => new ChannelLinkRow(c.Provider, c.NameNormalized, c.OwnerUserId))
            .FirstOrDefaultAsync(ct);
        if (channel is null)
            return values;

        string? avatar = await db
            .Users.Where(u => u.Id == channel.OwnerUserId)
            .Select(u => u.ProfileImageUrl)
            .FirstOrDefaultAsync(ct);

        string url = UrlOf(channel.Provider, channel.Login);
        values[ChannelUrl] = url;
        values[StreamUrl] = url;
        values[ChannelLogin] = channel.Login;
        values[ChannelAvatar] = avatar ?? string.Empty;
        values[StreamThumbnail] = ThumbnailOf(channel.Provider, channel.Login, liveSince);
        return values;
    }

    /// <summary>The channel page on its platform, or empty when the platform has no public page address.</summary>
    internal static string UrlOf(string provider, string login) =>
        provider switch
        {
            AuthEnums.Platform.Twitch => $"https://www.twitch.tv/{login}",
            AuthEnums.Platform.Kick => $"https://kick.com/{login}",
            _ => string.Empty,
        };

    /// <summary>The live preview picture. Only Twitch publishes one at an address built from the login.</summary>
    internal static string ThumbnailOf(string provider, string login, DateTimeOffset liveSince) =>
        provider == AuthEnums.Platform.Twitch
            ? $"{TwitchPreviewHost}/live_user_{login}-1280x720.jpg?t={liveSince.ToUnixTimeSeconds()}"
            : string.Empty;

    private sealed record ChannelLinkRow(string Provider, string Login, Guid OwnerUserId);
}
