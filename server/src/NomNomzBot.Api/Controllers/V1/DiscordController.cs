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
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The per-channel Discord subsystem surface (discord.md §5): guild links (both-opt-in handshake), notification
/// rules, self-assign notify roles + member opt-in, and the dispatch log. Tenant <c>channelId</c> is resolved
/// and authorized by <c>TenantResolutionMiddleware</c> (Gate 1); the per-route floor is the
/// <c>[RequireAction]</c> Gate-2 key (<c>discord:*:read</c> = Moderator, <c>discord:*:write</c> /
/// <c>discord:optin:write</c> = SuperMod). The OAuth bot-install <c>/connect</c> + callback live in
/// <see cref="DiscordOAuthController"/>, not here.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId:guid}/discord")]
[Authorize]
[Tags("Discord")]
public class DiscordController : BaseController
{
    private readonly IDiscordGuildService _guilds;
    private readonly IDiscordNotificationConfigService _configs;
    private readonly IDiscordNotificationRoleService _roles;
    private readonly IDiscordNotificationDispatcher _dispatcher;
    private readonly IDiscordGuildDirectoryService _directory;

    public DiscordController(
        IDiscordGuildService guilds,
        IDiscordNotificationConfigService configs,
        IDiscordNotificationRoleService roles,
        IDiscordNotificationDispatcher dispatcher,
        IDiscordGuildDirectoryService directory
    )
    {
        _guilds = guilds;
        _configs = configs;
        _roles = roles;
        _dispatcher = dispatcher;
        _directory = directory;
    }

    // ── Connections ─────────────────────────────────────────────────────────

    /// <summary>List the channel's linked Discord guild connections.</summary>
    [RequireAction("discord:connection:read")]
    [HttpGet("connections")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<DiscordGuildConnectionDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetConnections(Guid channelId, CancellationToken ct) =>
        ResultResponse(await _guilds.GetConnectionsAsync(channelId, ct));

    /// <summary>Fetch a single Discord guild connection by id.</summary>
    [RequireAction("discord:connection:read")]
    [HttpGet("connections/{connectionId:guid}")]
    [ProducesResponseType<StatusResponseDto<DiscordGuildConnectionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConnection(
        Guid channelId,
        Guid connectionId,
        CancellationToken ct
    ) => ResultResponse(await _guilds.GetConnectionAsync(channelId, connectionId, ct));

    /// <summary>Record the Discord server admin's consent on a guild connection — the server half of the both-opt-in handshake.</summary>
    [RequireAction("discord:connection:write")]
    [HttpPost("connections/{connectionId:guid}/server-consent")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ApproveServerConsent(
        Guid channelId,
        Guid connectionId,
        [FromBody] ServerConsentRequest request,
        CancellationToken ct
    ) =>
        ResultResponse(
            await _guilds.ApproveServerConsentAsync(
                channelId,
                connectionId,
                request.ApprovedByDiscordUserId,
                ct
            )
        );

    /// <summary>Withdraw the Discord server's consent from a guild connection, halting notifications until re-approved.</summary>
    [RequireAction("discord:connection:write")]
    [HttpDelete("connections/{connectionId:guid}/server-consent")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeServerConsent(
        Guid channelId,
        Guid connectionId,
        CancellationToken ct
    ) => ResultResponse(await _guilds.RevokeServerConsentAsync(channelId, connectionId, ct));

    /// <summary>Toggle the streamer's side of a guild connection on or off — the streamer half of the both-opt-in handshake.</summary>
    [RequireAction("discord:connection:write")]
    [HttpPut("connections/{connectionId:guid}/streamer-enabled")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetStreamerEnabled(
        Guid channelId,
        Guid connectionId,
        [FromBody] StreamerEnabledRequest request,
        CancellationToken ct
    ) =>
        ResultResponse(
            await _guilds.SetStreamerEnabledAsync(channelId, connectionId, request.Enabled, ct)
        );

    /// <summary>Disconnect a Discord guild from the channel entirely.</summary>
    [RequireAction("discord:connection:write")]
    [HttpDelete("connections/{connectionId:guid}")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Disconnect(
        Guid channelId,
        Guid connectionId,
        CancellationToken ct
    ) => ResultResponse(await _guilds.DisconnectAsync(channelId, connectionId, ct));

    // ── Guild directory (live pickers — proxied reads, nothing persisted) ────

    /// <summary>Read the linked guild's live profile from Discord.</summary>
    [RequireAction("discord:connection:read")]
    [HttpGet("connections/{connectionId:guid}/guild")]
    [ProducesResponseType<StatusResponseDto<DiscordGuildInfoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGuild(
        Guid channelId,
        Guid connectionId,
        CancellationToken ct
    ) => ResultResponse(await _directory.GetGuildAsync(channelId, connectionId, ct));

    /// <summary>List the linked guild's live roles, for the notify-role picker.</summary>
    [RequireAction("discord:role:read")]
    [HttpGet("connections/{connectionId:guid}/guild/roles")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<DiscordGuildRoleDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetGuildRoles(
        Guid channelId,
        Guid connectionId,
        CancellationToken ct
    ) => ResultResponse(await _directory.GetGuildRolesAsync(channelId, connectionId, ct));

    /// <summary>List the linked guild's live channels, for the target/button channel pickers.</summary>
    [RequireAction("discord:connection:read")]
    [HttpGet("connections/{connectionId:guid}/guild/channels")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<DiscordGuildChannelDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetGuildChannels(
        Guid channelId,
        Guid connectionId,
        CancellationToken ct
    ) => ResultResponse(await _directory.GetGuildChannelsAsync(channelId, connectionId, ct));

    // ── Notification configs ──────────────────────────────────────────────────

    /// <summary>List the notification rules configured on a guild connection.</summary>
    [RequireAction("discord:config:read")]
    [HttpGet("connections/{connectionId:guid}/configs")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<DiscordNotificationConfigDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetConfigs(
        Guid channelId,
        Guid connectionId,
        CancellationToken ct
    ) => ResultResponse(await _configs.GetConfigsAsync(channelId, connectionId, ct));

    /// <summary>Create a notification rule on a guild connection.</summary>
    [RequireAction("discord:config:write")]
    [HttpPost("connections/{connectionId:guid}/configs")]
    [ProducesResponseType<StatusResponseDto<DiscordNotificationConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateConfig(
        Guid channelId,
        Guid connectionId,
        [FromBody] CreateDiscordNotificationConfigRequest request,
        CancellationToken ct
    ) => ResultResponse(await _configs.CreateConfigAsync(channelId, connectionId, request, ct));

    /// <summary>Update an existing notification rule.</summary>
    [RequireAction("discord:config:write")]
    [HttpPut("configs/{configId:guid}")]
    [ProducesResponseType<StatusResponseDto<DiscordNotificationConfigDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateConfig(
        Guid channelId,
        Guid configId,
        [FromBody] UpdateDiscordNotificationConfigRequest request,
        CancellationToken ct
    ) => ResultResponse(await _configs.UpdateConfigAsync(channelId, configId, request, ct));

    /// <summary>Delete a notification rule from the channel.</summary>
    [RequireAction("discord:config:write")]
    [HttpDelete("configs/{configId:guid}")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteConfig(
        Guid channelId,
        Guid configId,
        CancellationToken ct
    ) => ResultResponse(await _configs.DeleteConfigAsync(channelId, configId, ct));

    /// <summary>Render a preview of a notification rule's message without dispatching it to Discord.</summary>
    [RequireAction("discord:config:read")]
    [HttpGet("configs/{configId:guid}/preview")]
    [ProducesResponseType<StatusResponseDto<DiscordNotificationPreviewDto>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> PreviewConfig(
        Guid channelId,
        Guid configId,
        CancellationToken ct
    ) => ResultResponse(await _configs.PreviewAsync(channelId, configId, ct));

    // ── Notify roles + opt-in ─────────────────────────────────────────────────

    /// <summary>List the self-assign notify roles defined on a guild connection.</summary>
    [RequireAction("discord:role:read")]
    [HttpGet("connections/{connectionId:guid}/roles")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<DiscordNotificationRoleDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetRoles(
        Guid channelId,
        Guid connectionId,
        CancellationToken ct
    ) => ResultResponse(await _roles.GetRolesAsync(channelId, connectionId, ct));

    /// <summary>Create a self-assign notify role on a guild connection.</summary>
    [RequireAction("discord:role:write")]
    [HttpPost("connections/{connectionId:guid}/roles")]
    [ProducesResponseType<StatusResponseDto<DiscordNotificationRoleDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateRole(
        Guid channelId,
        Guid connectionId,
        [FromBody] CreateDiscordNotificationRoleRequest request,
        CancellationToken ct
    ) => ResultResponse(await _roles.CreateRoleAsync(channelId, connectionId, request, ct));

    /// <summary>Update a self-assign notify role's settings.</summary>
    [RequireAction("discord:role:write")]
    [HttpPut("roles/{roleId:guid}")]
    [ProducesResponseType<StatusResponseDto<DiscordNotificationRoleDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateRole(
        Guid channelId,
        Guid roleId,
        [FromBody] UpdateDiscordNotificationRoleRequest request,
        CancellationToken ct
    ) => ResultResponse(await _roles.UpdateRoleAsync(channelId, roleId, request, ct));

    /// <summary>Delete a self-assign notify role.</summary>
    [RequireAction("discord:role:write")]
    [HttpDelete("roles/{roleId:guid}")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteRole(
        Guid channelId,
        Guid roleId,
        CancellationToken ct
    ) => ResultResponse(await _roles.DeleteRoleAsync(channelId, roleId, ct));

    /// <summary>Post the role's opt-in button message to a chosen Discord channel.</summary>
    [RequireAction("discord:role:write")]
    [HttpPost("roles/{roleId:guid}/button")]
    [ProducesResponseType<StatusResponseDto<DiscordNotificationRoleDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PostOptInButton(
        Guid channelId,
        Guid roleId,
        [FromBody] PostOptInButtonRequest request,
        CancellationToken ct
    ) =>
        ResultResponse(
            await _roles.PostOptInButtonAsync(channelId, roleId, request.ButtonChannelId, ct)
        );

    /// <summary>Opt a Discord member into a notify role, recording the opt-in source.</summary>
    [RequireAction("discord:optin:write")]
    [HttpPost("roles/{roleId:guid}/opt-in")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> OptIn(
        Guid channelId,
        Guid roleId,
        [FromBody] DiscordMemberOptInRequest request,
        CancellationToken ct
    ) =>
        ResultResponse(
            await _roles.OptInMemberAsync(
                channelId,
                roleId,
                request.DiscordMemberId,
                request.Source,
                ct
            )
        );

    /// <summary>Opt a Discord member out of a notify role, recording the opt-out source.</summary>
    [RequireAction("discord:optin:write")]
    [HttpPost("roles/{roleId:guid}/opt-out")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> OptOut(
        Guid channelId,
        Guid roleId,
        [FromBody] DiscordMemberOptInRequest request,
        CancellationToken ct
    ) =>
        ResultResponse(
            await _roles.OptOutMemberAsync(
                channelId,
                roleId,
                request.DiscordMemberId,
                request.Source,
                ct
            )
        );

    // ── Dispatch log ──────────────────────────────────────────────────────────

    /// <summary>List a guild connection's notification dispatch log, paginated.</summary>
    [RequireAction("discord:dispatch:read")]
    [HttpGet("connections/{connectionId:guid}/dispatch-log")]
    [ProducesResponseType<PaginatedResponse<DiscordDispatchLogDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDispatchLog(
        Guid channelId,
        Guid connectionId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        Result<PagedList<DiscordDispatchLogDto>> result = await _dispatcher.GetDispatchLogAsync(
            channelId,
            connectionId,
            request.Page,
            request.Take,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }
}
