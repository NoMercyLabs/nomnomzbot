// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Chat.ValueObjects;
using ConfigEntity = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Chat message history, sending, moderation, and chat-mode settings for a channel — the
/// dashboard operator's chat page.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/chat")]
[Authorize]
[Tags("Chat")]
public class ChatController : BaseController
{
    private readonly IApplicationDbContext _db;
    private readonly IChatProvider _chat;
    private readonly ITwitchChatApi _chatApi;
    private readonly IChatMessageDecorator _decorator;
    private readonly ICurrentUserService _currentUser;
    private readonly IOperatorChatSender _operatorSender;
    private readonly IOperatorMessageDeleter _operatorDeleter;
    private readonly IChatEmoteCatalogue _emoteCatalogue;
    private readonly IHubUserEnricher _enricher;

    public ChatController(
        IApplicationDbContext db,
        IChatProvider chat,
        ITwitchChatApi chatApi,
        IChatMessageDecorator decorator,
        ICurrentUserService currentUser,
        IOperatorChatSender operatorSender,
        IOperatorMessageDeleter operatorDeleter,
        IChatEmoteCatalogue emoteCatalogue,
        IHubUserEnricher enricher
    )
    {
        _db = db;
        _chat = chat;
        _chatApi = chatApi;
        _decorator = decorator;
        _currentUser = currentUser;
        _operatorSender = operatorSender;
        _operatorDeleter = operatorDeleter;
        _emoteCatalogue = emoteCatalogue;
        _enricher = enricher;
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    // Chat history (GET messages) returns the SAME DashboardChatMessageDto the live DashboardHub broadcast emits
    // (chat-client.md §3.6 / §9·9) — one decorated + enriched shape (fragments, badges, pronouns, avatar, real
    // timestamp), so scrollback and the live feed render identically with no drift.

    // SenderIdentity ∈ "you" (default — the logged-in operator's own Twitch account) | "bot" (the bot account).
    public record SendChatMessageRequest(
        string Message,
        string SenderIdentity = "you",
        string? ReplyToMessageId = null
    );

    // One composer-catalogue emote — the ChatEmote shape flattened for the wire (camelCase via the API's JSON
    // options). Provider ∈ Twitch|Bttv|Ffz|SevenTv; Urls keyed by scale "1".."4"; ZeroWidth stacks 7TV overlays.
    public record ChatEmoteCatalogueDto(
        string Code,
        string Provider,
        IReadOnlyDictionary<string, string> Urls,
        bool Animated,
        bool ZeroWidth,
        string? SetId
    );

    public record ChatSettingsDto(
        bool SlowMode,
        int SlowModeDelay,
        bool SubscriberOnly,
        bool EmotesOnly,
        bool FollowersOnly,
        int FollowersOnlyDuration
    );

    private static readonly ChatSettingsDto DefaultSettings = new(
        SlowMode: false,
        SlowModeDelay: 0,
        SubscriberOnly: false,
        EmotesOnly: false,
        FollowersOnly: false,
        FollowersOnlyDuration: 0
    );

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ── GET messages ──────────────────────────────────────────────────────────

    /// <summary>Get recent chat messages for the dashboard's chat feed, returned oldest first.</summary>
    [RequireAction("chat:read")]
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
        limit = Math.Clamp(limit, 1, 200);

        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        List<ChatMessage> rows = await _db
            .ChatMessages.Where(m => m.BroadcasterId == broadcasterId && m.DeletedAt == null)
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        // Chronological order (oldest first), matching the hub's live-append order.
        rows.Reverse();

        // One extra lookup for the channel's Twitch id (once per request, not once per message) so the
        // third-party emote adapters can match this channel's OWN emote set, not just the global ones
        // (chat-decoration spec §3.2). Empty when the channel row is somehow missing — decoration then simply
        // falls back to global-only sets, exactly like the live hub path degrades on an unknown channel.
        string twitchBroadcasterId =
            await _db
                .Channels.Where(c => c.Id == broadcasterId)
                .Select(c => c.TwitchChannelId)
                .FirstOrDefaultAsync(ct)
            ?? string.Empty;

        // Decorate + enrich sequentially into the SAME DashboardChatMessageDto the live hub emits, so scrollback
        // and the live feed are one shape (chat-client.md §3.6). The decorator reads only ICacheService and the
        // enricher is cache-gated (30s), so a page of 25-50 rows stays cheap; sequential also keeps each message
        // on the SAME request-scoped DbContext one at a time, avoiding the "second operation on this context"
        // hazard a concurrent fan-out would risk.
        List<DashboardChatMessageDto> messages = new(rows.Count);
        foreach (ChatMessage row in rows)
        {
            DecoratedChatMessage decorated = await _decorator.DecorateAsync(
                ToDecorationEvent(row, twitchBroadcasterId),
                ct
            );
            HubUserEnrichment? enrichment = await _enricher.EnrichAsync(
                broadcasterId,
                row.UserId,
                ct
            );
            (bool isBroadcaster, bool isModerator, bool isVip, bool isSubscriber) = ParseUserType(
                row.UserType
            );

            messages.Add(
                new DashboardChatMessageDto(
                    Id: row.Id,
                    ChannelId: channelId,
                    UserId: row.UserId,
                    DisplayName: row.DisplayName,
                    Username: row.Username,
                    Message: row.Message,
                    Fragments: decorated.Fragments.Select(ChatFragmentMapper.MapFragment).ToList(),
                    UserType: row.UserType,
                    IsSubscriber: isSubscriber,
                    IsVip: isVip,
                    IsModerator: isModerator,
                    IsBroadcaster: isBroadcaster,
                    IsCheer: row.IsCheer,
                    IsCommand: row.IsCommand,
                    Badges: decorated.Badges.Select(ChatFragmentMapper.MapBadge).ToList(),
                    BitsAmount: row.BitsAmount ?? 0,
                    Color: row.ColorHex,
                    MessageType: row.MessageType,
                    ReplyToMessageId: row.ReplyToMessageId,
                    ReplyParentMessageBody: null,
                    ReplyParentUserName: null,
                    Timestamp: row.CreatedAt.ToString("O"),
                    AvatarUrl: enrichment?.AvatarUrl,
                    Pronouns: enrichment?.Pronouns
                )
            );
        }

        return Ok(new StatusResponseDto<List<DashboardChatMessageDto>> { Data = messages });
    }

    // Rebuilds the minimal ChatMessageReceivedEvent the decorator needs from a persisted (raw, un-enriched)
    // ChatMessage row, so the exact same IChatMessageDecorator pipeline that decorates the live hub broadcast
    // also decorates chat history — no separate/duplicated enrichment logic for the REST page.
    private static ChatMessageReceivedEvent ToDecorationEvent(
        ChatMessage row,
        string twitchBroadcasterId
    )
    {
        (bool isBroadcaster, bool isModerator, bool isVip, bool isSubscriber) = ParseUserType(
            row.UserType
        );

        return new ChatMessageReceivedEvent
        {
            BroadcasterId = row.BroadcasterId,
            MessageId = row.Id,
            TwitchBroadcasterId = twitchBroadcasterId,
            UserId = row.UserId,
            UserDisplayName = row.DisplayName,
            UserLogin = row.Username,
            Message = row.Message,
            Fragments = row.Fragments,
            Badges = row.Badges,
            ColorHex = row.ColorHex,
            MessageType = row.MessageType,
            IsSubscriber = isSubscriber,
            IsVip = isVip,
            IsModerator = isModerator,
            IsBroadcaster = isBroadcaster,
            Bits = row.BitsAmount ?? 0,
            ReplyParentMessageId = row.ReplyToMessageId,
        };
    }

    // The reverse of ChatMessagePersistenceHandler.ResolveUserType's priority (broadcaster > moderator > vip >
    // subscriber), recovering the sender's role flags the decorator's link-preview standing gate needs
    // (chat-decoration spec §9·9) from the single UserType string persisted on the row.
    private static (
        bool IsBroadcaster,
        bool IsModerator,
        bool IsVip,
        bool IsSubscriber
    ) ParseUserType(string userType) =>
        userType switch
        {
            "broadcaster" => (true, false, false, false),
            "moderator" => (false, true, false, false),
            "vip" => (false, false, true, false),
            "subscriber" => (false, false, false, true),
            _ => (false, false, false, false),
        };

    // ── POST message (send as the operator, or the bot) ────────────────────────
    // The dashboard's REST send path — the same Helix Send Chat Message that DashboardHub.SendChatMessage
    // performs, exposed over REST so the chat page can send without holding a hub connection. Defaults to the
    // logged-in operator's own Twitch account (chat-client.md §3.1); SenderIdentity="bot" sends as the bot.

    /// <summary>Send a chat message to the channel — as the logged-in operator by default, or as the bot.</summary>
    [RequireAction("chat:send")]
    [HttpPost("messages")]
    [ProducesResponseType<StatusResponseDto<bool>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SendMessage(
        string channelId,
        [FromBody] SendChatMessageRequest request,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        string message = request.Message?.Trim() ?? string.Empty;
        if (message.Length == 0)
            return BadRequestResponse("Message cannot be empty.");
        if (message.Length > 500)
            return BadRequestResponse("Message exceeds the 500-character limit.");

        // "bot" sends as the bot account (bot-voice posts); the default "you" sends as the logged-in operator's
        // own Twitch identity, so a moderator appears as themselves in the channel (chat-client.md §3.1).
        if (string.Equals(request.SenderIdentity, "bot", StringComparison.OrdinalIgnoreCase))
        {
            bool sent = await _chat.SendMessageAsync(broadcasterId, message, ct);
            if (!sent)
                // The bot couldn't deliver the message to Twitch (no/unhealthy connection, dead token). Report it
                // honestly — a 503 the chat page surfaces — instead of a {data:true} that lies the send succeeded.
                return ServiceUnavailableResponse(
                    "The message could not be sent to Twitch. Your Twitch connection may need to be reconnected."
                );

            return Ok(new StatusResponseDto<bool> { Data = true });
        }

        if (!Guid.TryParse(_currentUser.UserId, out Guid operatorUserId))
            return UnauthenticatedResponse();

        Result operatorResult = await _operatorSender.SendAsUserAsync(
            operatorUserId,
            broadcasterId,
            message,
            request.ReplyToMessageId,
            ct
        );
        if (operatorResult.IsFailure)
            // Surface the real Twitch reason (dead token, or the operator is banned/timed-out here) via the Helix
            // error mapping — never a {data:true} that lies the send landed.
            return TwitchResultResponse(operatorResult);

        return Ok(new StatusResponseDto<bool> { Data = true });
    }

    // ── GET emote catalogue (composer autocomplete + inline rendering) ─────────

    /// <summary>Get the emotes usable in this channel — Twitch global+channel and BTTV/FFZ/7TV — for the composer.</summary>
    [RequireAction("chat:read")]
    [HttpGet("emotes")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<ChatEmoteCatalogueDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetEmotes(string channelId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<IReadOnlyList<ChatEmote>> catalogue = await _emoteCatalogue.GetForChannelAsync(
            broadcasterId,
            ct
        );
        if (catalogue.IsFailure)
            return TwitchResultResponse(catalogue);

        List<ChatEmoteCatalogueDto> emotes = catalogue
            .Value.Select(e => new ChatEmoteCatalogueDto(
                e.Code,
                e.Provider.ToString(),
                e.Urls,
                e.Animated,
                e.ZeroWidth,
                e.SetId
            ))
            .ToList();
        return Ok(new StatusResponseDto<IReadOnlyList<ChatEmoteCatalogueDto>> { Data = emotes });
    }

    // ── DELETE message (moderation quick-action) ───────────────────────────────

    /// <summary>Delete a chat message — a moderator's quick-action from the dashboard's chat page, attributed to them.</summary>
    [RequireAction("moderation:delete_message")]
    [HttpDelete("messages/{messageId}")]
    [ProducesResponseType<StatusResponseDto<bool>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteMessage(
        string channelId,
        string messageId,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        if (string.IsNullOrWhiteSpace(messageId))
            return BadRequestResponse("Missing message id.");

        if (!Guid.TryParse(_currentUser.UserId, out Guid operatorUserId))
            return UnauthenticatedResponse();

        // Delete AS THE LOGGED-IN OPERATOR (their own token, moderator_id = them) so Twitch attributes the removal to
        // the moderator who clicked — not the broadcaster (chat-client.md §3.5). Mirrors the operator send path above;
        // the bot's IChatProvider stays the automation (pipeline / AutoMod) delete path.
        Result result = await _operatorDeleter.DeleteAsUserAsync(
            operatorUserId,
            broadcasterId,
            messageId,
            ct
        );

        // Edge case ONLY: the operator has no linked Twitch identity to act as (no usable operator token). Rather than
        // fail the moderation outright, fall back to the tenant delete (broadcaster's token, broadcaster as moderator_id).
        // This is the single path that mis-attributes to the broadcaster, and only when acting AS the operator is
        // impossible — every other failure is surfaced honestly below, never a silent broadcaster-attributed retry.
        if (result.IsFailure && result.ErrorCode == TwitchErrorCodes.NoToken)
        {
            await _chat.DeleteMessageAsync(broadcasterId, messageId, ct);
            return Ok(new StatusResponseDto<bool> { Data = true });
        }

        if (result.IsFailure)
            return TwitchResultResponse(result);

        return Ok(new StatusResponseDto<bool> { Data = true });
    }

    // ── GET settings ─────────────────────────────────────────────────────────

    /// <summary>Get the channel's chat-mode settings (slow mode, sub-only, emote-only, followers-only).</summary>
    [RequireAction("moderation:chat:settings:read")]
    [HttpGet("settings")]
    [ProducesResponseType<StatusResponseDto<ChatSettingsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(string channelId, CancellationToken ct)
    {
        Guid? broadcasterId = Guid.TryParse(channelId, out Guid g) ? g : null;

        ConfigEntity? config = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Key == "chat.settings",
            ct
        );

        ChatSettingsDto settings = config?.Value is not null
            ? JsonSerializer.Deserialize<ChatSettingsDto>(config.Value, JsonOptions)
                ?? DefaultSettings
            : DefaultSettings;

        return Ok(new StatusResponseDto<ChatSettingsDto> { Data = settings });
    }

    // ── PUT settings ──────────────────────────────────────────────────────────

    /// <summary>Replace the channel's chat-mode settings.</summary>
    [RequireAction("moderation:chat:settings:write")]
    [HttpPut("settings")]
    [ProducesResponseType<StatusResponseDto<ChatSettingsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSettings(
        string channelId,
        [FromBody] ChatSettingsDto request,
        CancellationToken ct
    ) => await SaveSettings(channelId, request, ct);

    // ── PATCH settings (partial update) ───────────────────────────────────────

    /// <summary>Partially update the channel's chat-mode settings, merging only the supplied fields.</summary>
    [RequireAction("moderation:chat:settings:write")]
    [HttpPatch("settings")]
    [ProducesResponseType<StatusResponseDto<ChatSettingsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PatchSettings(
        string channelId,
        [FromBody] JsonElement patch,
        CancellationToken ct
    )
    {
        Guid? broadcasterId = Guid.TryParse(channelId, out Guid g) ? g : null;

        // Load existing, apply partial override from patch body
        ConfigEntity? config = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Key == "chat.settings",
            ct
        );

        ChatSettingsDto current = config?.Value is not null
            ? JsonSerializer.Deserialize<ChatSettingsDto>(config.Value, JsonOptions)
                ?? DefaultSettings
            : DefaultSettings;

        bool slowMode = patch.TryGetProperty("slowMode", out JsonElement sm)
            ? sm.GetBoolean()
            : current.SlowMode;
        int slowModeDelay = patch.TryGetProperty("slowModeDelay", out JsonElement smd)
            ? smd.GetInt32()
            : current.SlowModeDelay;
        bool subscriberOnly = patch.TryGetProperty("subscriberOnly", out JsonElement so)
            ? so.GetBoolean()
            : current.SubscriberOnly;
        bool emotesOnly = patch.TryGetProperty("emotesOnly", out JsonElement eo)
            ? eo.GetBoolean()
            : current.EmotesOnly;
        bool followersOnly = patch.TryGetProperty("followersOnly", out JsonElement fo)
            ? fo.GetBoolean()
            : current.FollowersOnly;
        int followersOnlyDuration = patch.TryGetProperty(
            "followersOnlyDuration",
            out JsonElement fod
        )
            ? fod.GetInt32()
            : current.FollowersOnlyDuration;

        ChatSettingsDto merged = new ChatSettingsDto(
            slowMode,
            slowModeDelay,
            subscriberOnly,
            emotesOnly,
            followersOnly,
            followersOnlyDuration
        );
        return await SaveSettings(channelId, merged, ct);
    }

    private async Task<IActionResult> SaveSettings(
        string channelId,
        ChatSettingsDto settings,
        CancellationToken ct
    )
    {
        Guid? broadcasterId = Guid.TryParse(channelId, out Guid g) ? g : null;

        ConfigEntity? config = await _db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Key == "chat.settings",
            ct
        );

        string json = JsonSerializer.Serialize(settings, JsonOptions);

        if (config is null)
        {
            _db.Configurations.Add(
                new ConfigEntity
                {
                    BroadcasterId = broadcasterId,
                    Key = "chat.settings",
                    Value = json,
                }
            );
        }
        else
        {
            config.Value = json;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new StatusResponseDto<ChatSettingsDto> { Data = settings });
    }

    // ── Announcement ──────────────────────────────────────────────────────────

    public record AnnounceRequest(string Message, string? Color = null);

    /// <summary>Send a Twitch chat announcement message with an optional highlight color.</summary>
    [RequireAction("chat:announce")]
    [HttpPost("announce")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Announce(
        string channelId,
        [FromBody] AnnounceRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result result = await _chatApi.SendAnnouncementAsync(
            broadcasterId,
            request.Message,
            request.Color,
            ct
        );
        return result.IsFailure ? TwitchResultResponse(result) : NoContent();
    }
}
