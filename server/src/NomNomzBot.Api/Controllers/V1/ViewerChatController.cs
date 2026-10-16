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
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Viewer chat (chat-client.md §9a): a signed-in viewer reads the channel's recent chat and sends in their OWN
/// voice, on their own token. No search, no per-person history, no moderation and never the bot.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/chat/viewer")]
[Authorize]
[Tags("Chat")]
public class ViewerChatController : BaseController
{
    public const int MaxMessages = 100;
    public const int MaxMessageLength = 500;

    private readonly IApplicationDbContext _db;
    private readonly IChatMessageDecorator _decorator;
    private readonly IHubUserEnricher _enricher;
    private readonly ICurrentUserService _currentUser;
    private readonly IOperatorChatSender _sender;
    private readonly IChatEmoteCatalogue _emoteCatalogue;

    public ViewerChatController(
        IApplicationDbContext db,
        IChatMessageDecorator decorator,
        IHubUserEnricher enricher,
        ICurrentUserService currentUser,
        IOperatorChatSender sender,
        IChatEmoteCatalogue emoteCatalogue
    )
    {
        _db = db;
        _decorator = decorator;
        _enricher = enricher;
        _currentUser = currentUser;
        _sender = sender;
        _emoteCatalogue = emoteCatalogue;
    }

    // No sender choice on purpose: a viewer only ever speaks as themselves.
    public record ViewerChatSendRequest(string Message, string? ReplyToMessageId = null);

    /// <summary>The channel's newest chat lines (deleted lines left out), oldest first.</summary>
    [RequireAction("chat:viewer:read")]
    [HttpGet("messages")]
    [ProducesResponseType<StatusResponseDto<List<DashboardChatMessageDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetMessages(
        string channelId,
        [FromQuery] int limit = 50,
        CancellationToken ct = default
    )
    {
        limit = Math.Clamp(limit, 1, MaxMessages);

        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        List<ChatMessage> rows = await _db
            .ChatMessages.Where(m => m.BroadcasterId == broadcasterId && m.DeletedAt == null)
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
        rows.Reverse();

        List<DashboardChatMessageDto> messages = await ChatHistoryMapper.MapAsync(
            _db,
            _decorator,
            _enricher,
            broadcasterId,
            channelId,
            rows,
            ct
        );

        return Ok(new StatusResponseDto<List<DashboardChatMessageDto>> { Data = messages });
    }

    /// <summary>Send one chat message as yourself, on your own Twitch account, optionally as a reply.</summary>
    [RequireAction("chat:viewer:send")]
    [EnableRateLimiting(RateLimitPolicyNames.ViewerChatSend)]
    [HttpPost("messages")]
    [ProducesResponseType<StatusResponseDto<bool>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendMessage(
        string channelId,
        [FromBody] ViewerChatSendRequest request,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        string message = request.Message?.Trim() ?? string.Empty;
        if (message.Length == 0)
            return BadRequestResponse("Message cannot be empty.");
        if (message.Length > MaxMessageLength)
            return BadRequestResponse("Message exceeds the 500-character limit.");

        if (!Guid.TryParse(_currentUser.UserId, out Guid viewerUserId))
            return UnauthenticatedResponse();

        Result result = await _sender.SendAsUserAsync(
            viewerUserId,
            broadcasterId,
            message,
            request.ReplyToMessageId,
            ct
        );
        if (result.IsFailure)
            return TwitchResultResponse(result);

        return Ok(new StatusResponseDto<bool> { Data = true });
    }

    /// <summary>The emotes you can use in this channel: Twitch global + channel, your own, and BTTV/FFZ/7TV.</summary>
    [RequireAction("chat:viewer:read")]
    [HttpGet("emotes")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<ChatController.ChatEmoteCatalogueDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetEmotes(string channelId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        if (!Guid.TryParse(_currentUser.UserId, out Guid viewerUserId))
            return UnauthenticatedResponse();

        Result<IReadOnlyList<ChatEmote>> catalogue = await _emoteCatalogue.GetForChannelAsync(
            broadcasterId,
            viewerUserId,
            ChatEmoteSender.Operator,
            ct
        );
        if (catalogue.IsFailure)
            return TwitchResultResponse(catalogue);

        List<ChatController.ChatEmoteCatalogueDto> emotes =
        [
            .. catalogue.Value.Select(ChatController.ChatEmoteCatalogueDto.From),
        ];
        return Ok(
            new StatusResponseDto<IReadOnlyList<ChatController.ChatEmoteCatalogueDto>>
            {
                Data = emotes,
            }
        );
    }
}
