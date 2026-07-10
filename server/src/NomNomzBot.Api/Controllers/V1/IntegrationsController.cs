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
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Application.Integrations.Dtos;
using NomNomzBot.Application.Integrations.Services;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>Lists connected external integrations (Spotify, Discord, YouTube, OBS, etc.) and handles disconnection.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/integrations")]
[Authorize]
[Tags("Integrations")]
public class IntegrationsController : BaseController
{
    private readonly IApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly IDiscordGuildService _discord;
    private readonly IIntegrationStatusService _integrationStatus;

    public IntegrationsController(
        IApplicationDbContext db,
        IConfiguration config,
        IDiscordGuildService discord,
        IIntegrationStatusService integrationStatus
    )
    {
        _db = db;
        _config = config;
        _discord = discord;
        _integrationStatus = integrationStatus;
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

    // ── List integrations ─────────────────────────────────────────────────────

    /// <summary>List all available integrations and their connection state for the channel.</summary>
    [RequireAction("integration:read")]
    [HttpGet]
    [ProducesResponseType<StatusResponseDto<IntegrationsResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListIntegrations(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        // Connection state comes from the shared IIntegrationStatusService (single source of truth,
        // reused by the render manifest); the controller only layers on the request-derived OAuth
        // connect URL for the providers that expose one.
        Result<List<ChannelIntegrationDto>> statuses = await _integrationStatus.GetStatusesAsync(
            tenantId,
            ct
        );
        if (statuses.IsFailure)
            return ResultResponse(statuses);

        List<IntegrationDto> result = statuses
            .Value.Select(s => new IntegrationDto(
                s.Id,
                s.Name,
                s.Category,
                s.Description,
                s.Connected,
                s.ConnectedAs,
                BuildOauthUrl(s.Id, channelId),
                null
            ))
            .ToList();

        return Ok(
            new StatusResponseDto<IntegrationsResponse> { Data = new IntegrationsResponse(result) }
        );
    }

    // ── Disconnect integration ────────────────────────────────────────────────

    /// <summary>Disconnect an external integration (revokes tokens, removes connection state).</summary>
    [RequireAction("integration:write")]
    [HttpDelete("{integrationId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Disconnect(
        string channelId,
        string integrationId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        string id = integrationId.ToLower();

        if (id == "twitch")
            return BadRequestResponse("Cannot disconnect the primary Twitch account.");

        // White-label custom bot is per-channel
        if (id == "custom_bot")
        {
            Service? botService = await _db.Services.FirstOrDefaultAsync(
                s => s.Name == "twitch_bot" && s.BroadcasterId == tenantId,
                ct
            );
            if (botService is not null)
            {
                _db.Services.Remove(botService);
                await _db.SaveChangesAsync(ct);
            }
            return NoContent();
        }

        if (id == "discord")
        {
            // Disconnect every linked guild for this tenant through the Discord subsystem, which
            // soft-deletes the connection + its configs/roles and revokes the vaulted bot token.
            List<Guid> connectionIds = await _db
                .DiscordGuildConnections.Where(d => d.BroadcasterId == tenantId)
                .Select(d => d.Id)
                .ToListAsync(ct);

            foreach (Guid connectionId in connectionIds)
                await _discord.DisconnectAsync(tenantId, connectionId, ct);

            return NoContent();
        }

        Service? service = await _db.Services.FirstOrDefaultAsync(
            s => s.BroadcasterId == tenantId && s.Name.ToLower() == id,
            ct
        );

        if (service is null)
            return NotFoundResponse($"Integration '{integrationId}' is not connected.");

        _db.Services.Remove(service);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The browser-openable connect-start URL for the providers that expose one directly: the per-channel
    /// custom-bot OAuth, and Discord's bespoke bot-install start (both real, anonymous redirect routes). Spotify
    /// and YouTube are NOT here — they use the generic <c>POST …/integrations/{provider}/connect</c>
    /// (authenticated, returns the provider authorize URL via <see cref="IntegrationOAuthController"/>), so a
    /// GET start URL would point at a route that does not exist. Twitch/OBS have no OAuth-start.
    /// </summary>
    private string? BuildOauthUrl(string integrationId, string channelId)
    {
        string baseUrl = _config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        string apiBase = $"{baseUrl}/api/v1";

        return integrationId switch
        {
            "custom_bot" => $"{baseUrl}/api/v1/channels/{channelId}/bot/connect",
            "discord" => $"{apiBase}/channels/{channelId}/integrations/discord/callback/start",
            _ => null,
        };
    }
}
