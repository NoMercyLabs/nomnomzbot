// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Integrations.Dtos;
using NomNomzBot.Application.Integrations.Services;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Integrations;

/// <summary>
/// Resolves the connection state of every known integration for a channel from the stored service
/// tokens (<c>Service</c>) and Discord guild connections. Owns the integration catalogue and the
/// connection detection; the request-specific OAuth connect URLs are layered on by the integrations
/// controller. Shared by the integrations list endpoint and the dashboard render manifest.
/// </summary>
public sealed class IntegrationStatusService : IIntegrationStatusService
{
    private readonly IApplicationDbContext _db;

    public IntegrationStatusService(IApplicationDbContext db)
    {
        _db = db;
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

        // Twitch is always connected when the channel exists.
        var channel = await _db
            .Channels.Where(c => c.Id == broadcasterId)
            .Select(c => new { c.Id, c.Name })
            .FirstOrDefaultAsync(cancellationToken);
        bool twitchConnected = channel is not null;

        // White-label custom bot is per-channel (BroadcasterId=broadcasterId, Name="twitch_bot").
        var customBotService = await _db
            .Services.Where(s =>
                s.Name == "twitch_bot"
                && s.BroadcasterId == broadcasterId
                && s.Enabled
                && s.AccessToken != null
            )
            .Select(s => new { s.UserId })
            .FirstOrDefaultAsync(cancellationToken);

        // Service.UserId holds the Twitch user string id — join on User.TwitchUserId.
        string? customBotLogin = null;
        if (customBotService?.UserId is not null)
        {
            customBotLogin = await _db
                .Users.Where(u => u.TwitchUserId == customBotService.UserId)
                .Select(u => u.Username)
                .FirstOrDefaultAsync(cancellationToken);
        }

        List<ChannelIntegrationDto> result = Meta.Select(kvp =>
            {
                string id = kvp.Key;
                (string Name, string Category, string Description) = kvp.Value;

                bool isConnected = id switch
                {
                    "twitch" => twitchConnected,
                    "custom_bot" => customBotService is not null,
                    _ => connectedServiceNames.Contains(id),
                };

                string? connectedAs = id switch
                {
                    "custom_bot" => customBotLogin,
                    "twitch" => channel?.Name,
                    _ => null,
                };

                return new ChannelIntegrationDto(
                    id,
                    Name,
                    Category,
                    Description,
                    isConnected,
                    connectedAs
                );
            })
            .ToList();

        return Result.Success(result);
    }
}
