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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Integrations.Dtos;
using NomNomzBot.Application.Integrations.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Integrations;

/// <summary>
/// Resolves the connection state of every known integration for a channel from the stored service
/// tokens (<c>Service</c>), the crypto vault (<c>IntegrationConnection</c> — YouTube, S036c-b), and
/// Discord guild connections. Owns the integration catalogue and the connection detection; the
/// request-specific OAuth connect URLs are layered on by the integrations controller. Shared by the
/// integrations list endpoint and the dashboard render manifest.
/// </summary>
public sealed class IntegrationStatusService : IIntegrationStatusService
{
    private readonly IApplicationDbContext _db;
    private readonly IMusicService _music;

    public IntegrationStatusService(IApplicationDbContext db, IMusicService music)
    {
        _db = db;
        _music = music;
    }

    private static readonly Dictionary<
        string,
        (string Name, string Category, string Description)
    > Meta = new()
    {
        ["twitch"] = ("Twitch", "Platform", "Primary Twitch account — always connected"),
        // custom_bot = white-label bot for this channel (Pro tier). Uses BroadcasterId=channelId.
        // The global platform bot (NomNomzBot) is managed in the admin panel, not here.
        ["custom_bot"] = (
            "Custom Bot",
            "Platform",
            "White-label bot — messages appear from your own bot account instead of NomNomzBot"
        ),
        ["spotify"] = ("Spotify", "Music", "Now playing overlays and song request commands"),
        ["discord"] = ("Discord", "Social", "Cross-post alerts and notifications to Discord"),
        ["youtube"] = ("YouTube", "Video", "YouTube live stream management and stats"),
        ["obs"] = ("OBS", "Streaming", "Scene switching, sources, and OBS remote control"),
    };

    public async Task<Result<List<ChannelIntegrationDto>>> GetStatusesAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        // Load all Service records for this channel in one query.
        List<string> connectedServiceNames = await _db
            .Services.Where(s =>
                s.BroadcasterId == broadcasterId && s.Enabled && s.AccessToken != null
            )
            .Select(s => s.Name.ToLower())
            .ToListAsync(cancellationToken);

        bool discordConnected = await _db.DiscordGuildConnections.AnyAsync(
            d => d.BroadcasterId == broadcasterId,
            cancellationToken
        );

        if (discordConnected && !connectedServiceNames.Contains("discord"))
            connectedServiceNames.Add("discord");

        // S036c-b — YouTube's custody moved to the vault; it no longer writes the legacy Service row,
        // so its connectivity is resolved from IntegrationConnection instead of connectedServiceNames.
        bool youtubeConnected = await _db.IntegrationConnections.AnyAsync(
            c =>
                c.BroadcasterId == broadcasterId
                && c.Provider == AuthEnums.IntegrationProvider.YouTube
                && c.Status == AuthEnums.IntegrationStatus.Connected,
            cancellationToken
        );
        connectedServiceNames.Remove("youtube");
        if (youtubeConnected)
            connectedServiceNames.Add("youtube");

        // Twitch is always connected when the channel exists.
        var channel = await _db
            .Channels.Where(c => c.Id == broadcasterId)
            .Select(c => new { c.Id, c.Name })
            .FirstOrDefaultAsync(cancellationToken);
        bool twitchConnected = channel is not null;

        // The channel's own bot: an active authorization to a CUSTOM bot account. The shared system bot
        // is never one, so a channel on the shared bot reads as not connected here.
        string? customBotLogin = await _db
            .ChannelBotAuthorizations.IgnoreQueryFilters()
            .Where(a => a.BroadcasterId == broadcasterId && a.IsActive && a.DeletedAt == null)
            .Join(
                _db.BotAccounts.IgnoreQueryFilters()
                    .Where(b => b.IdentityType == AuthEnums.BotIdentityType.Custom),
                a => a.BotAccountId,
                b => b.Id,
                (_, b) => b.BotUsername
            )
            .FirstOrDefaultAsync(cancellationToken);

        // S003 — only Spotify observes/reports a live auth status today; other providers surface null
        // until they wire the same classification. Skipped entirely when Spotify isn't connected —
        // AuthStatus only ever qualifies an already-connected entry.
        string? spotifyAuthStatus = connectedServiceNames.Contains("spotify")
            ? await _music.GetActiveProviderAuthStatusAsync(
                broadcasterId.ToString(),
                cancellationToken
            )
            : null;

        List<ChannelIntegrationDto> result =
        [
            .. Meta.Select(kvp =>
            {
                string id = kvp.Key;
                (string Name, string Category, string Description) = kvp.Value;

                bool isConnected = id switch
                {
                    "twitch" => twitchConnected,
                    "custom_bot" => customBotLogin is not null,
                    _ => connectedServiceNames.Contains(id),
                };

                string? connectedAs = id switch
                {
                    "custom_bot" => customBotLogin,
                    "twitch" => channel?.Name,
                    _ => null,
                };

                string? authStatus = id == "spotify" ? spotifyAuthStatus : null;

                return new ChannelIntegrationDto(
                    id,
                    Name,
                    Category,
                    Description,
                    isConnected,
                    connectedAs,
                    authStatus
                );
            }),
        ];

        return Result.Success(result);
    }
}
