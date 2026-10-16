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
using NomNomzBot.Api.Identifiers;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The public viewer profile (chat-client.md §9a "Public profile"): only what Twitch's own viewer card shows
/// every viewer. Never notes, warnings, bans, timeouts, trust or the bot's counters.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/chat/viewer/users")]
[Authorize]
[Tags("Chat")]
public class ViewerProfileController : BaseController
{
    public const int RecentMessageCount = 10;

    private readonly IApplicationDbContext _db;
    private readonly ITwitchChannelsApi _channels;
    private readonly IChatEmoteCatalogue _emoteCatalogue;

    public ViewerProfileController(
        IApplicationDbContext db,
        ITwitchChannelsApi channels,
        IChatEmoteCatalogue emoteCatalogue
    )
    {
        _db = db;
        _channels = channels;
        _emoteCatalogue = emoteCatalogue;
    }

    public record PublicBadgeDto(string SetId, string Id, string? Info);

    public record PublicMessageDto(string Id, string Message, DateTime SentAt);

    public record PublicProfileDto(
        string TwitchUserId,
        Guid InternalUserId,
        string Username,
        string DisplayName,
        string? ProfileImageUrl,
        string? Color,
        string? Pronouns,
        IReadOnlyList<PublicBadgeDto> Badges,
        DateTime? AccountCreatedAt,
        DateTimeOffset? FollowedAt,
        string? SubTier,
        IReadOnlyList<ChatController.ChatEmoteCatalogueDto> Emotes,
        IReadOnlyList<PublicMessageDto> RecentMessages
    );

    /// <summary>One person's public profile in this channel: what any viewer of the channel can see.</summary>
    [RequireAction("chat:viewer:read")]
    [HttpGet("{userId}/profile")]
    [ProducesResponseType<StatusResponseDto<PublicProfileDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfile(
        string channelId,
        string userId,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        User? user = await FindUserAsync(userId, ct);
        if (user?.TwitchUserId is null)
            return NotFoundResponse("User not found.");

        string twitchId = user.TwitchUserId;

        List<ChatMessage> lines = await _db
            .ChatMessages.AsNoTracking()
            .Where(m =>
                m.BroadcasterId == broadcasterId
                && m.UserId == twitchId
                && m.DeletedAt == null
                && !m.IsCommand
            )
            .OrderByDescending(m => m.CreatedAt)
            .Take(RecentMessageCount)
            .ToListAsync(ct);

        ChatMessage? newest = lines.FirstOrDefault();

        string? subTier = await _db
            .ViewerProfiles.AsNoTracking()
            .Where(p =>
                p.BroadcasterId == broadcasterId
                && p.ViewerTwitchUserId == twitchId
                && p.IsSubscriber
            )
            .Select(p => p.SubTier)
            .FirstOrDefaultAsync(ct);

        PublicProfileDto profile = new(
            twitchId,
            user.Id,
            user.Username,
            user.DisplayName,
            user.ProfileImageUrl,
            user.Color ?? newest?.ColorHex,
            await PronounTextAsync(user.PronounId, ct),
            [.. (newest?.Badges ?? []).Select(b => new PublicBadgeDto(b.SetId, b.Id, b.Info))],
            user.AccountCreatedAt,
            await FollowedAtAsync(broadcasterId, twitchId, ct),
            subTier,
            await EmotesAsync(user.Id, ct),
            [.. lines.Select(m => new PublicMessageDto(m.Id, m.Message, m.CreatedAt))]
        );

        return Ok(new StatusResponseDto<PublicProfileDto> { Data = profile });
    }

    // A chat line carries the Twitch id; a link may carry the internal User.Id. A Twitch id never parses as a Guid.
    private Task<User?> FindUserAsync(string userId, CancellationToken ct)
    {
        IQueryable<User> users = _db.Users.AsNoTracking();

        return GuidUlidCodec.TryDecode(userId, out Guid internalId)
            ? users.FirstOrDefaultAsync(u => u.Id == internalId, ct)
            : users.FirstOrDefaultAsync(u => u.TwitchUserId == userId, ct);
    }

    private async Task<string?> PronounTextAsync(int? pronounId, CancellationToken ct)
    {
        if (pronounId is null)
            return null;

        Pronoun? pronoun = await _db
            .Pronouns.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pronounId, ct);

        return pronoun is null ? null : $"{pronoun.Subject}/{pronoun.Object}";
    }

    // Null when the person does not follow or Twitch could not answer: the card then shows no follow date.
    private async Task<DateTimeOffset?> FollowedAtAsync(
        Guid broadcasterId,
        string twitchId,
        CancellationToken ct
    )
    {
        Result<TwitchChannelFollower?> follower = await _channels.GetChannelFollowerAsync(
            broadcasterId,
            twitchId,
            ct
        );

        return follower.IsSuccess ? follower.Value?.FollowedAt : null;
    }

    private async Task<IReadOnlyList<ChatController.ChatEmoteCatalogueDto>> EmotesAsync(
        Guid internalUserId,
        CancellationToken ct
    )
    {
        Result<IReadOnlyList<ChatEmote>> emotes = await _emoteCatalogue.GetForPersonAsync(
            internalUserId,
            ct
        );

        return emotes.IsSuccess
            ? [.. emotes.Value.Select(ChatController.ChatEmoteCatalogueDto.From)]
            : [];
    }
}
