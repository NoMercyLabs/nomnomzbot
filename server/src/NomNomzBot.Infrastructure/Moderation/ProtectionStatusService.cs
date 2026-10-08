// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// Answers "is protection running here?" from the rows that decide it. Every check is read on its own, so one
/// unreadable source degrades only its own check to <c>unknown</c> and never reports <c>ok</c> by default.
/// The bot-moderator read and the platform decision are the ones the inbox and the enforcement already use,
/// so this screen cannot disagree with them.
/// </summary>
public sealed class ProtectionStatusService(
    IApplicationDbContext db,
    IBotModeratorStatusReader botStatus,
    ISpamDefenseService spam,
    TimeProvider time
) : IProtectionStatusService
{
    /// <summary>The EventSub topics the moderation features listen on. All are in the channel's subscribed catalogue.</summary>
    internal static readonly string[] ModerationTopics =
    [
        "channel.moderate",
        "automod.message.hold",
        "channel.suspicious_user.message",
        "channel.suspicious_user.update",
        "channel.unban_request.create",
    ];

    private const string ActiveSubscriptionStatus = "enabled";

    public async Task<Result<ProtectionStatusDto>> GetAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    )
    {
        string? channelProvider = await db
            .Channels.IgnoreQueryFilters()
            .Where(c => c.Id == channelId && c.DeletedAt == null)
            .Select(c => c.Provider)
            .FirstOrDefaultAsync(cancellationToken);
        if (channelProvider is null)
            return Errors.ChannelNotFound<ProtectionStatusDto>(channelId.ToString());

        List<string> platforms = await ConnectedPlatformsAsync(
            channelId,
            channelProvider,
            cancellationToken
        );
        bool onTwitch = platforms.Contains(AuthEnums.Platform.Twitch);

        List<ProtectionCheckDto> checks = [];
        if (onTwitch)
            checks.Add(
                await RunAsync(
                    ProtectionCheckKeys.BotModerator,
                    AuthEnums.Platform.Twitch,
                    () => BotModeratorAsync(channelId, cancellationToken)
                )
            );

        checks.Add(
            await RunAsync(
                ProtectionCheckKeys.SpamDefenseMode,
                null,
                () => SpamDefenseModeAsync(channelId, cancellationToken)
            )
        );

        foreach (string platform in platforms)
            checks.Add(AutomaticAction(platform));

        if (onTwitch)
            checks.Add(
                await RunAsync(
                    ProtectionCheckKeys.EventSubModeration,
                    AuthEnums.Platform.Twitch,
                    () => EventSubModerationAsync(channelId, cancellationToken)
                )
            );

        return Result.Success(new ProtectionStatusDto(checks));
    }

    /// <summary>The platforms the channel is connected to; a channel with no connection row is judged on its own.</summary>
    private async Task<List<string>> ConnectedPlatformsAsync(
        Guid channelId,
        string channelProvider,
        CancellationToken ct
    )
    {
        List<string> connected = await db
            .PlatformConnections.IgnoreQueryFilters()
            .Where(p => p.ChannelId == channelId && p.DeletedAt == null)
            .Select(p => p.Provider)
            .Distinct()
            .ToListAsync(ct);

        return connected.Count == 0
            ? [channelProvider.ToLowerInvariant()]
            : [.. connected.Select(p => p.ToLowerInvariant()).Distinct().Order()];
    }

    private async Task<ProtectionCheckDto> BotModeratorAsync(Guid channelId, CancellationToken ct)
    {
        BotModeratorReading reading = await botStatus.ReadAsync(channelId, ct);
        string name = reading.Bot?.Username ?? "The bot";

        (string state, string reason) = reading.Standing switch
        {
            BotModeratorStanding.Moderator => (
                ProtectionCheckStates.Ok,
                $"{name} is a moderator in this channel."
            ),
            BotModeratorStanding.NotModerator => (
                ProtectionCheckStates.Warning,
                $"{name} is not a moderator in this channel, so it cannot moderate. Make it a moderator on Twitch."
            ),
            BotModeratorStanding.NotObserved => (
                ProtectionCheckStates.Unknown,
                $"Whether {name} is a moderator has not been checked yet."
            ),
            _ => (
                ProtectionCheckStates.Ok,
                "Your own account speaks in this channel, so no moderator role is needed."
            ),
        };

        return new(ProtectionCheckKeys.BotModerator, state, AuthEnums.Platform.Twitch, reason);
    }

    private async Task<ProtectionCheckDto> SpamDefenseModeAsync(
        Guid channelId,
        CancellationToken ct
    )
    {
        SpamDefenseSettings settings = await spam.GetSettingsAsync(channelId, ct);
        if (!settings.IsEnabled)
            return Mode(
                ProtectionCheckStates.Warning,
                "Spam defence is switched off, so spam is not being checked."
            );

        if (settings.DryRun)
            return Mode(
                ProtectionCheckStates.Warning,
                "Spam defence is in dry run. Actions are only recorded, not taken."
            );

        DateTime? eligibleAt = await SpamObservationWindow.EligibleAtAsync(db, channelId, ct);
        if (eligibleAt is not null && time.GetUtcNow().UtcDateTime < eligibleAt.Value)
            return Mode(
                ProtectionCheckStates.Warning,
                $"Spam defence is still in its observation week. Actions are only recorded until {eligibleAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}."
            );

        return Mode(ProtectionCheckStates.Ok, "Spam defence is on and acts on what it detects.");
    }

    private static ProtectionCheckDto Mode(string state, string reason) =>
        new(ProtectionCheckKeys.SpamDefenseMode, state, null, reason);

    private static ProtectionCheckDto AutomaticAction(string platform) =>
        SpamEnforcementExecutor.CanEnforceOn(platform)
            ? new(
                ProtectionCheckKeys.AutomaticAction,
                ProtectionCheckStates.Ok,
                platform,
                $"Automatic moderation can act on {platform}."
            )
            : new(
                ProtectionCheckKeys.AutomaticAction,
                ProtectionCheckStates.Warning,
                platform,
                $"Automatic moderation cannot act on {platform}. Detections there are only recorded."
            );

    private async Task<ProtectionCheckDto> EventSubModerationAsync(
        Guid channelId,
        CancellationToken ct
    )
    {
        List<string> active = await db
            .EventSubSubscriptions.IgnoreQueryFilters()
            .Where(s =>
                s.BroadcasterId == channelId
                && s.DeletedAt == null
                && s.Enabled
                && s.Status == ActiveSubscriptionStatus
                && ModerationTopics.Contains(s.EventType)
            )
            .Select(s => s.EventType)
            .Distinct()
            .ToListAsync(ct);

        List<string> missing = [.. ModerationTopics.Except(active)];
        return missing.Count == 0
            ? new(
                ProtectionCheckKeys.EventSubModeration,
                ProtectionCheckStates.Ok,
                AuthEnums.Platform.Twitch,
                "Twitch is sending every moderation event."
            )
            : new(
                ProtectionCheckKeys.EventSubModeration,
                ProtectionCheckStates.Failing,
                AuthEnums.Platform.Twitch,
                $"Twitch is not sending these moderation events: {string.Join(", ", missing)}."
            );
    }

    private static async Task<ProtectionCheckDto> RunAsync(
        string key,
        string? platform,
        Func<Task<ProtectionCheckDto>> read
    )
    {
        try
        {
            return await read();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(
                key,
                ProtectionCheckStates.Unknown,
                platform,
                $"This check could not be read: {ex.Message}"
            );
        }
    }
}
