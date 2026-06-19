// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;

namespace NomNomzBot.Api.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/integrations")]
[Authorize]
[Tags("Integrations")]
public class IntegrationsController : BaseController
{
    private readonly IApplicationDbContext _db;
    private readonly IConfiguration _config;

    public IntegrationsController(IApplicationDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public record IntegrationDto(
        string Id,
        string Name,
        string Category,
        string Description,
        bool Connected,
        string? ConnectedAs,
        string? OauthUrl,
        string? LastSync
    );

    public record IntegrationsResponse(List<IntegrationDto> Integrations);

    // ── Known integrations metadata ───────────────────────────────────────────

    private static readonly Dictionary<
        string,
        (string Name, string Category, string Description)
    > _meta = new()
    {
        ["twitch"] = ("Twitch", "Platform", "Primary Twitch account — always connected"),
        // custom_bot = white-label bot for this channel (Pro tier). Uses BroadcasterId=channelId.
        // The global platform bot (NomNomzBot) is managed in the admin panel, not here.
        ["custom_bot"] = ("Custom Bot", "Platform", "White-label bot — messages appear from your own bot account instead of NomNomzBot"),
        ["spotify"] = ("Spotify", "Music", "Now playing overlays and song request commands"),
        ["discord"] = ("Discord", "Social", "Cross-post alerts and notifications to Discord"),
        ["youtube"] = ("YouTube", "Video", "YouTube live stream management and stats"),
        ["obs"] = ("OBS", "Streaming", "Scene switching, sources, and OBS remote control"),
    };

    // ── List integrations ─────────────────────────────────────────────────────

    [HttpGet]
    [ProducesResponseType<StatusResponseDto<IntegrationsResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListIntegrations(string channelId, CancellationToken ct)
    {
        // Load all Service records for this channel in one query
        List<string> connectedServiceNames = await _db
            .Services.Where(s => s.BroadcasterId == channelId && s.Enabled && s.AccessToken != null)
            .Select(s => s.Name.ToLower())
            .ToListAsync(ct);

        bool discordConnected = await _db.DiscordServerAuthorizations.AnyAsync(
            d => d.BroadcasterId == channelId,
            ct
        );

        if (discordConnected && !connectedServiceNames.Contains("discord"))
            connectedServiceNames.Add("discord");

        // Twitch is always connected when the channel exists
        var channel = await _db.Channels
            .Where(c => c.Id == channelId)
            .Select(c => new { c.Id, c.Name })
            .FirstOrDefaultAsync(ct);
        bool twitchConnected = channel is not null;

        // White-label custom bot is per-channel (BroadcasterId=channelId, Name="twitch_bot")
        var customBotService = await _db.Services
            .Where(s => s.Name == "twitch_bot" && s.BroadcasterId == channelId && s.Enabled && s.AccessToken != null)
            .Select(s => new { s.UserId })
            .FirstOrDefaultAsync(ct);

        string? customBotLogin = null;
        if (customBotService?.UserId is not null)
        {
            customBotLogin = await _db.Users
                .Where(u => u.Id == customBotService.UserId)
                .Select(u => u.Username)
                .FirstOrDefaultAsync(ct);
        }

        string baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";

        var result = _meta
            .Select(kvp =>
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

                string? oauthUrl = id switch
                {
                    "obs" => null,
                    "twitch" => null,
                    "custom_bot" => $"{baseUrl}/api/v1/channels/{channelId}/bot/connect",
                    _ => BuildOauthUrl(id, channelId),
                };

                return new IntegrationDto(
                    id,
                    Name,
                    Category,
                    Description,
                    isConnected,
                    connectedAs,
                    oauthUrl,
                    null
                );
            })
            .ToList();

        return Ok(
            new StatusResponseDto<IntegrationsResponse> { Data = new IntegrationsResponse(result) }
        );
    }

    // ── Disconnect integration ────────────────────────────────────────────────

    [HttpDelete("{integrationId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Disconnect(
        string channelId,
        string integrationId,
        CancellationToken ct
    )
    {
        string id = integrationId.ToLower();

        if (id == "twitch")
            return BadRequestResponse("Cannot disconnect the primary Twitch account.");

        // White-label custom bot is per-channel
        if (id == "custom_bot")
        {
            var botService = await _db.Services.FirstOrDefaultAsync(
                s => s.Name == "twitch_bot" && s.BroadcasterId == channelId, ct);
            if (botService is not null)
            {
                _db.Services.Remove(botService);
                await _db.SaveChangesAsync(ct);
            }
            return NoContent();
        }

        if (id == "discord")
        {
            var discordAuth = await _db.DiscordServerAuthorizations.FirstOrDefaultAsync(
                d => d.BroadcasterId == channelId,
                ct
            );
            if (discordAuth is not null)
            {
                _db.DiscordServerAuthorizations.Remove(discordAuth);
                await _db.SaveChangesAsync(ct);
            }
            return NoContent();
        }

        var service = await _db.Services.FirstOrDefaultAsync(
            s => s.BroadcasterId == channelId && s.Name.ToLower() == id,
            ct
        );

        if (service is null)
            return NotFoundResponse($"Integration '{integrationId}' is not connected.");

        _db.Services.Remove(service);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Get OAuth connect URL ─────────────────────────────────────────────────

    [HttpGet("{integrationId}/connect")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public IActionResult GetConnectUrl(string channelId, string integrationId)
    {
        string id = integrationId.ToLower();
        string? url = BuildOauthUrl(id, channelId);

        if (url is null)
        {
            return BadRequestResponse(
                id == "obs"
                    ? "OBS uses WebSocket — install the OBS WebSocket plugin and configure it in your OBS settings."
                    : $"OAuth is not available for '{integrationId}'."
            );
        }

        return Ok(new StatusResponseDto<object> { Data = new { oauthUrl = url } });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string? BuildOauthUrl(string integrationId, string channelId)
    {
        string baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        string apiBase = $"{baseUrl}/api/v1";

        return integrationId switch
        {
            "custom_bot" => $"{baseUrl}/api/v1/channels/{channelId}/bot/connect",
            "spotify" => $"{apiBase}/channels/{channelId}/integrations/spotify/callback/start",
            "discord" => $"{apiBase}/channels/{channelId}/integrations/discord/callback/start",
            "youtube" => $"{apiBase}/channels/{channelId}/integrations/youtube/callback/start",
            _ => null,
        };
    }
}
