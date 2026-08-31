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
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.BackgroundServices;

namespace NomNomzBot.Infrastructure.Identity;

public class ChannelService : IChannelService
{
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IEventBus _eventBus;
    private readonly IChannelRegistry _registry;
    private readonly ITwitchEventSubService _eventSub;
    private readonly IChatProvider _chatProvider;
    private readonly IBuiltinResponseComposer _responseComposer;

    public ChannelService(
        IApplicationDbContext db,
        TimeProvider timeProvider,
        IEventBus eventBus,
        IChannelRegistry registry,
        ITwitchEventSubService eventSub,
        IChatProvider chatProvider,
        IBuiltinResponseComposer responseComposer
    )
    {
        _db = db;
        _timeProvider = timeProvider;
        _eventBus = eventBus;
        _registry = registry;
        _eventSub = eventSub;
        _chatProvider = chatProvider;
        _responseComposer = responseComposer;
    }

    public async Task<Result> JoinAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound(broadcasterId);

        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.Id == broadcasterGuid,
            cancellationToken
        );

        if (channel is null)
            return Errors.ChannelNotFound(broadcasterId);

        channel.Enabled = true;
        channel.BotJoinedAt = _timeProvider.GetUtcNow().UtcDateTime;

        await _db.SaveChangesAsync(cancellationToken);

        // Take effect now, not on BotLifecycleService's next 5-minute reconcile tick — EnsureSubscribedAsync
        // is idempotent so it is safe even if the periodic reconcile also picks this channel up.
        await _eventSub.EnsureSubscribedAsync(
            broadcasterGuid,
            BotLifecycleService.ChannelEventTypes,
            cancellationToken
        );

        // Opt-in, default OFF (house rule: opt-in/default-deny) — announce the bot's own connect. Distinct
        // from the operator-configured "stream.online" event response (that fires on the STREAM going live);
        // this fires once, right here, on the real connect — never on BotLifecycleService's periodic no-op
        // reconcile of an already-joined channel, since JoinAsync only runs on an actual join.
        if (channel.AnnounceOnConnect)
        {
            string announcement = await _responseComposer.ComposeAsync(
                new()
                {
                    BroadcasterId = broadcasterGuid,
                    Personality = channel.Personality,
                    BuiltinKey = "connect",
                    Slot = "announce",
                    NeutralFallback = "I'm now active in this channel!",
                },
                cancellationToken
            );
            if (!string.IsNullOrEmpty(announcement))
                await _chatProvider.SendMessageAsync(
                    broadcasterGuid,
                    announcement,
                    cancellationToken
                );
        }

        return Result.Success();
    }

    public async Task<Result> LeaveAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound(broadcasterId);

        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.Id == broadcasterGuid,
            cancellationToken
        );

        if (channel is null)
            return Errors.ChannelNotFound(broadcasterId);

        channel.Enabled = false;

        await _db.SaveChangesAsync(cancellationToken);

        // Take effect now, not on BotLifecycleService's next 5-minute reconcile tick.
        await _eventSub.UnsubscribeAllAsync(broadcasterGuid, cancellationToken);

        return Result.Success();
    }

    public async Task<Result<ChannelDto>> GetAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound<ChannelDto>(broadcasterId);

        Channel? channel = await _db
            .Channels.Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == broadcasterGuid, cancellationToken);

        if (channel is null)
            return Errors.ChannelNotFound<ChannelDto>(broadcasterId);

        return Result.Success(ToDto(channel));
    }

    public async Task<Result<IReadOnlyList<ChannelSummaryDto>>> GetAllActiveAsync(
        CancellationToken cancellationToken = default
    )
    {
        List<ChannelSummaryDto> channels = await _db
            .Channels.Include(c => c.User)
            .Where(c => c.Enabled && c.IsOnboarded)
            .OrderBy(c => c.Name)
            .Select(c => new ChannelSummaryDto(
                c.Id.ToString(),
                c.Name,
                c.User.DisplayName,
                c.User.ProfileImageUrl,
                c.IsLive,
                "broadcaster",
                null,
                c.OverlayToken,
                c.Provider,
                c.User.Color
            ))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<ChannelSummaryDto>>(channels);
    }

    public async Task<Result<PagedList<ChannelSummaryDto>>> GetChannelsAsync(
        string userId,
        PaginationParams pagination,
        IReadOnlyList<string>? additionalChannelIds = null,
        CancellationToken cancellationToken = default
    )
    {
        // userId is the internal user Guid in string form. additionalChannelIds are external Twitch
        // channel ids (from the Twitch moderation API) and resolve against Channels.TwitchChannelId.
        Guid.TryParse(userId, out Guid userGuid);

        IReadOnlyList<string> extraTwitchChannelIds = additionalChannelIds ?? [];

        // Return channels where the user is the broadcaster (owner), a DB-tracked moderator,
        // or present in the caller-supplied Twitch-id list.
        IQueryable<Channel> query = _db
            .Channels.Include(c => c.User)
            .Where(c =>
                c.OwnerUserId == userGuid
                || c.Moderators.Any(m => m.UserId == userGuid)
                || extraTwitchChannelIds.Contains(c.TwitchChannelId)
            );

        int total = await query.CountAsync(cancellationToken);

        // The caller's OWN channel(s) sort first, then alphabetical. This is load-bearing: the dashboard defaults
        // its active channel to the first item, and resolves the caller's role against it. An owner who also
        // moderates a channel whose name sorts earlier (e.g. owns "stoney_eagle" but moderates "aaoa_") would
        // otherwise default to that moderated channel and be resolved as a mere viewer there — landing on the
        // participant surface instead of their own management dashboard. Owning a channel is the strongest claim,
        // so it wins the default.
        List<ChannelSummaryDto> items = await query
            .OrderByDescending(c => c.OwnerUserId == userGuid)
            .ThenBy(c => c.Name)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(c => new ChannelSummaryDto(
                c.Id.ToString(),
                c.Name,
                c.User.DisplayName,
                c.User.ProfileImageUrl,
                c.IsLive,
                c.OwnerUserId == userGuid ? "broadcaster" : "moderator",
                null,
                c.OverlayToken,
                c.Provider,
                c.User.Color
            ))
            .ToListAsync(cancellationToken);

        return Result.Success(
            new PagedList<ChannelSummaryDto>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<ChannelDto>> UpdateSettingsAsync(
        string broadcasterId,
        UpdateChannelSettingsDto request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound<ChannelDto>(broadcasterId);

        Channel? channel = await _db
            .Channels.Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == broadcasterGuid, cancellationToken);

        if (channel is null)
            return Errors.ChannelNotFound<ChannelDto>(broadcasterId);

        string? prefixError = ValidatePrefix(request.Prefix);
        if (prefixError is not null)
            return Result.Failure<ChannelDto>(prefixError, "VALIDATION_FAILED");

        string? botLinePrefixError = ValidateBotLinePrefix(request.BotLinePrefix);
        if (botLinePrefixError is not null)
            return Result.Failure<ChannelDto>(botLinePrefixError, "VALIDATION_FAILED");

        await ApplyAndPersistAsync(broadcasterGuid, channel, request, cancellationToken);
        return Result.Success(ToDto(channel));
    }

    public async Task<Result<ChannelBasicsDto>> GetBasicsAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound<ChannelBasicsDto>(broadcasterId);

        // Projection avoids loading the whole aggregate; the anonymous shape lets us map the User-owned
        // timezone alongside the channel scalars in one round trip.
        var row = await _db
            .Channels.Where(c => c.Id == broadcasterGuid)
            .Select(c => new
            {
                c.CommandPrefix,
                c.BotLinePrefix,
                c.Language,
                c.Enabled,
                c.User.Timezone,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return Errors.ChannelNotFound<ChannelBasicsDto>(broadcasterId);

        return Result.Success(
            new ChannelBasicsDto(
                row.CommandPrefix,
                row.BotLinePrefix,
                row.Language,
                row.Enabled,
                row.Timezone
            )
        );
    }

    public async Task<Result<ChannelBasicsDto>> UpdateBasicsAsync(
        string broadcasterId,
        UpdateChannelSettingsDto request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound<ChannelBasicsDto>(broadcasterId);

        Channel? channel = await _db
            .Channels.Include(c => c.User)
            .FirstOrDefaultAsync(c => c.Id == broadcasterGuid, cancellationToken);

        if (channel is null)
            return Errors.ChannelNotFound<ChannelBasicsDto>(broadcasterId);

        string? prefixError = ValidatePrefix(request.Prefix);
        if (prefixError is not null)
            return Result.Failure<ChannelBasicsDto>(prefixError, "VALIDATION_FAILED");

        string? botLinePrefixError = ValidateBotLinePrefix(request.BotLinePrefix);
        if (botLinePrefixError is not null)
            return Result.Failure<ChannelBasicsDto>(botLinePrefixError, "VALIDATION_FAILED");

        await ApplyAndPersistAsync(broadcasterGuid, channel, request, cancellationToken);
        return Result.Success(
            new ChannelBasicsDto(
                channel.CommandPrefix,
                channel.BotLinePrefix,
                channel.Language,
                channel.Enabled,
                channel.User.Timezone
            )
        );
    }

    /// <summary>
    /// Validates the command prefix: null leaves it unchanged; otherwise it must be 1-5 non-whitespace
    /// characters. Returns an error message when invalid, or null when valid/unchanged.
    /// </summary>
    private static string? ValidatePrefix(string? prefix)
    {
        if (prefix is null)
            return null;

        string trimmed = prefix.Trim();
        if (trimmed.Length is < 1 or > 5 || trimmed.Any(char.IsWhiteSpace))
            return "Command prefix must be 1-5 non-whitespace characters (e.g. \"!\").";

        return null;
    }

    /// <summary>
    /// Validates the bot-line prefix (D5): null leaves it unchanged; empty string clears it to "none";
    /// otherwise it must be 1-4 non-whitespace characters (a symbol or a single emoji, including
    /// multi-codepoint sequences). Returns an error message when invalid, or null when valid/unchanged.
    /// </summary>
    private static string? ValidateBotLinePrefix(string? prefix)
    {
        if (prefix is null || prefix.Length == 0)
            return null;

        if (prefix.Length > 4 || prefix.Any(char.IsWhiteSpace))
            return "Bot line prefix must be 1-4 non-whitespace characters (e.g. \"*\" or an emoji).";

        return null;
    }

    /// <summary>
    /// Applies the supplied (already-validated) settings to the loaded channel, persists them, refreshes the
    /// in-memory registry so the chat hot path picks up a prefix/locale change without a restart, and fans out
    /// the change for other consumers. The caller must have loaded <c>channel.User</c>.
    /// </summary>
    private async Task ApplyAndPersistAsync(
        Guid broadcasterGuid,
        Channel channel,
        UpdateChannelSettingsDto request,
        CancellationToken cancellationToken
    )
    {
        if (request.DisplayName is not null)
            channel.User.DisplayName = request.DisplayName;
        if (request.Prefix is not null)
            channel.CommandPrefix = request.Prefix.Trim();
        if (request.BotLinePrefix is not null)
            channel.BotLinePrefix =
                request.BotLinePrefix.Length == 0 ? null : request.BotLinePrefix;
        if (request.Locale is not null)
            channel.Language = request.Locale;
        bool? autoJoinChangedTo = null;
        if (request.AutoJoin.HasValue && request.AutoJoin.Value != channel.Enabled)
            autoJoinChangedTo = channel.Enabled = request.AutoJoin.Value;
        if (request.Timezone is not null)
            channel.User.Timezone = request.Timezone.Length == 0 ? null : request.Timezone;

        await _db.SaveChangesAsync(cancellationToken);
        await _registry.InvalidateSettingsAsync(broadcasterGuid, cancellationToken);

        // Take effect now, not on BotLifecycleService's next 5-minute reconcile tick — the dashboard's
        // auto-join toggle must actually connect/disconnect the channel's EventSub presence immediately.
        if (autoJoinChangedTo == true)
            await _eventSub.EnsureSubscribedAsync(
                broadcasterGuid,
                BotLifecycleService.ChannelEventTypes,
                cancellationToken
            );
        else if (autoJoinChangedTo == false)
            await _eventSub.UnsubscribeAllAsync(broadcasterGuid, cancellationToken);

        await _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterGuid,
                Domain = "channel-settings",
                Action = "updated",
            },
            cancellationToken
        );
    }

    public async Task<Result<ChannelPersonalityDto>> GetPersonalityAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound<ChannelPersonalityDto>(broadcasterId);

        // Projection avoids loading the whole aggregate for a single scalar. FirstOrDefault on a value type
        // needs the nullable projection to distinguish "no such channel" from a (never-null) tone column.
        string? personality = await _db
            .Channels.Where(c => c.Id == broadcasterGuid)
            .Select(c => c.Personality)
            .FirstOrDefaultAsync(cancellationToken);

        if (personality is null)
            return Errors.ChannelNotFound<ChannelPersonalityDto>(broadcasterId);

        return Result.Success(
            new ChannelPersonalityDto(PersonalityTone.Normalize(personality), PersonalityTone.All)
        );
    }

    public async Task<Result<ChannelPersonalityDto>> SetPersonalityAsync(
        string broadcasterId,
        string personality,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound<ChannelPersonalityDto>(broadcasterId);

        if (!PersonalityTone.IsValid(personality))
            return Result.Failure<ChannelPersonalityDto>(
                $"Unknown personality tone '{personality}'. Valid tones: {string.Join(", ", PersonalityTone.All)}.",
                "VALIDATION_FAILED"
            );

        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.Id == broadcasterGuid,
            cancellationToken
        );
        if (channel is null)
            return Errors.ChannelNotFound<ChannelPersonalityDto>(broadcasterId);

        string normalized = PersonalityTone.Normalize(personality);
        channel.Personality = normalized;
        await _db.SaveChangesAsync(cancellationToken);

        // Refresh the in-memory registry so the live chat hot path phrases built-ins in the new tone
        // without a restart, then fan the change out for any other consumers (dashboard live push).
        await _registry.InvalidateSettingsAsync(broadcasterGuid, cancellationToken);
        await _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterGuid,
                Domain = "channel-settings",
                EntityId = "personality",
                Action = "updated",
            },
            cancellationToken
        );

        return Result.Success(new ChannelPersonalityDto(normalized, PersonalityTone.All));
    }

    public async Task<Result<ChannelDto>> OnboardAsync(
        string broadcasterId,
        CreateChannelRequest request,
        CancellationToken cancellationToken = default
    )
    {
        // broadcasterId identifies the owning user (internal User Guid in string form). The channel's
        // own surrogate id is generated on creation; its Twitch id comes from the owner's TwitchUserId.
        if (!Guid.TryParse(broadcasterId, out Guid ownerGuid))
            return Result.Failure<ChannelDto>(
                "User not found. Cannot onboard channel.",
                "NOT_FOUND"
            );

        Channel? existing = await _db
            .Channels.Include(c => c.User)
            .FirstOrDefaultAsync(c => c.OwnerUserId == ownerGuid, cancellationToken);

        if (existing is not null)
        {
            existing.IsOnboarded = true;
            existing.BotJoinedAt ??= _timeProvider.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync(cancellationToken);
            await PublishOnboardedAsync(existing, cancellationToken);
            return Result.Success(ToDto(existing));
        }

        // Check if user exists
        User? user = await _db.Users.FirstOrDefaultAsync(u => u.Id == ownerGuid, cancellationToken);
        if (user is null)
            return Result.Failure<ChannelDto>(
                "User not found. Cannot onboard channel.",
                "NOT_FOUND"
            );

        Channel channel = new()
        {
            OwnerUserId = user.Id,
            TwitchChannelId = user.TwitchUserId,
            ExternalChannelId = user.TwitchUserId!,
            Name = user.Username,
            NameNormalized = user.Username.ToLowerInvariant(),
            IsOnboarded = true,
            Enabled = true,
            BotJoinedAt = _timeProvider.GetUtcNow().UtcDateTime,
            User = user,
        };

        _db.Channels.Add(channel);
        await _db.SaveChangesAsync(cancellationToken);
        await PublishOnboardedAsync(channel, cancellationToken);

        return Result.Success(ToDto(channel));
    }

    /// <summary>
    /// Publishes <see cref="ChannelOnboardedEvent"/> so the auto-discovered onboarding seed handlers (rewards,
    /// moderator roster, memberships, subscriber/VIP standing, channel info, owner profile, event responses,
    /// banned-user import, bot mod-join, default commands, EventSub subscribe) run for this channel — the same
    /// event the Twitch-OAuth login path publishes, and the one <see cref="BackgroundServices.OnboardedChannelSeedBackfillService"/>
    /// re-fires at startup. Every handler is documented idempotent, so publishing on both a brand-new channel
    /// and a repaired (already-existing but re-onboarded) one is always safe.
    /// </summary>
    private Task PublishOnboardedAsync(Channel channel, CancellationToken cancellationToken) =>
        _eventBus.PublishAsync(
            new ChannelOnboardedEvent
            {
                BroadcasterId = channel.Id,
                OwnerUserId = channel.OwnerUserId,
                TwitchChannelId = channel.TwitchChannelId!,
                Name = channel.Name,
            },
            cancellationToken
        );

    public async Task<Result<Guid>> EnsureModeratedTenantAsync(
        string twitchBroadcasterId,
        string login,
        string displayName,
        Guid ownerUserId,
        CancellationToken cancellationToken = default
    )
    {
        // Cross-tenant lookup: the global tenant filter scopes reads to the request's resolved tenant (the
        // caller's own channel), so it would hide every OTHER channel — including the moderated one we're
        // provisioning. IgnoreQueryFilters lets us see it (and dodge a duplicate insert against the unique
        // TwitchChannelId index); soft-delete is not re-applied because a returning tenant is used as-is.
        Channel? existing = await _db
            .Channels.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TwitchChannelId == twitchBroadcasterId, cancellationToken);

        if (existing is not null)
            return Result.Success(existing.Id);

        // Deliberately NOT onboarded and NO ChannelOnboardedEvent: the bot is not installed here, so there is
        // no presence to seed. The row exists solely so Gate 1 can resolve this channel as a tenant for the
        // moderator; onboarding (and its seed fan-out) happens only if/when the broadcaster installs the bot.
        Channel channel = new()
        {
            OwnerUserId = ownerUserId,
            TwitchChannelId = twitchBroadcasterId,
            ExternalChannelId = twitchBroadcasterId,
            Name = login,
            NameNormalized = login.ToLowerInvariant(),
            IsOnboarded = false,
        };
        _db.Channels.Add(channel);
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(channel.Id);
    }

    public async Task<Result> DeleteAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound(broadcasterId);

        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.Id == broadcasterGuid,
            cancellationToken
        );

        if (channel is null)
            return Errors.ChannelNotFound(broadcasterId);

        // A SOFT delete. Stamping DeletedAt hides the tenant behind the global query filter — the channel and
        // everything under it disappear from every read — while the rows survive for the restore window. The
        // bot must also stop serving the channel immediately, which is what Enabled=false does; a restore
        // turns it back on. Setting DeletedAt is what makes DeletedBy get stamped by SoftDeleteInterceptor.
        channel.Enabled = false;
        channel.DeletedAt = _timeProvider.GetUtcNow().UtcDateTime;

        await _db.SaveChangesAsync(cancellationToken);
        await _registry.RemoveAsync(channel.Id, cancellationToken);

        return Result.Success();
    }

    public async Task<Result> RestoreAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound(broadcasterId);

        // A deleted channel is invisible to every filtered read by design, so the restore path is the one
        // place that must look past the filter to find it.
        Channel? channel = await _db
            .Channels.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == broadcasterGuid, cancellationToken);

        if (channel is null)
            return Errors.ChannelNotFound(broadcasterId);

        if (channel.DeletedAt is null)
            return Result.Success();

        // Past the window the promise the delete dialog made has expired; the data is being purged and a
        // half-restore would resurrect a tenant whose rows are already going away.
        DateTime permanentAfter = ChannelDeletionPolicy.PermanentAfter(channel.DeletedAt.Value);
        if (_timeProvider.GetUtcNow().UtcDateTime >= permanentAfter)
            return Errors.ChannelRestoreWindowExpired(broadcasterId, permanentAfter);

        channel.DeletedAt = null;
        channel.DeletedBy = null;
        channel.Enabled = true;

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    public async Task<Result<string>> GetOverlayTokenAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound<string>(broadcasterId);

        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.Id == broadcasterGuid,
            cancellationToken
        );
        if (channel is null)
            return Errors.ChannelNotFound<string>(broadcasterId);

        return Result.Success(channel.OverlayToken);
    }

    public async Task<Result<string>> RotateOverlayTokenAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcasterGuid))
            return Errors.ChannelNotFound<string>(broadcasterId);

        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.Id == broadcasterGuid,
            cancellationToken
        );
        if (channel is null)
            return Errors.ChannelNotFound<string>(broadcasterId);

        channel.OverlayToken = Guid.NewGuid().ToString();
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(channel.OverlayToken);
    }

    public async Task<ChannelOverlayInfo?> GetByOverlayTokenAsync(
        string token,
        CancellationToken cancellationToken = default
    )
    {
        return await _db
            .Channels.Include(c => c.User)
            .Where(c => c.OverlayToken == token)
            .Select(c => new ChannelOverlayInfo(c.Id.ToString(), c.User.DisplayName))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static ChannelDto ToDto(Channel c) =>
        new(
            c.Id.ToString(),
            c.Name,
            c.User?.DisplayName ?? c.Name,
            c.User?.ProfileImageUrl,
            c.IsLive,
            c.IsOnboarded,
            c.Title,
            c.GameName,
            null,
            c.BotJoinedAt,
            "free",
            c.Language,
            c.CreatedAt
        );
}
