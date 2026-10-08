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
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation.EventHandlers;

/// <summary>
/// Runs the channel's custom chat filters (moderation.md J.6) against every incoming Twitch chat message and
/// enforces the first one that matches on a non-exempt sender:
/// <list type="bullet">
///   <item><c>delete</c> — removes the message via Helix.</item>
///   <item><c>timeout</c> — times the sender out for the filter's configured duration.</item>
///   <item><c>escalate</c> — removes the message, then records one offense on the channel's escalation ladder
///   (§3.11) and applies the ladder's decision (warn / timeout / ban); with the ladder off it times the sender
///   out for the filter's configured duration.</item>
///   <item><c>hold</c> — removes the message via Helix and queues it for moderator review.</item>
///   <item><c>flag</c> — leaves the message in chat and queues it for moderator review.</item>
/// </list>
/// Enforcement rides Helix, so non-Twitch messages are skipped; the broadcaster and moderators are never
/// filtered, as is any sender at or above a filter's <see cref="ChatFilter.ExemptMinRoleLevel"/>.
/// </summary>
public sealed partial class ChatFilterExecutionHandler(
    IApplicationDbContext db,
    ITwitchModerationApi moderation,
    IModerationEscalationService escalation,
    IModerationQueueService queue,
    IUserService users,
    NomNomzBot.Application.Contracts.Security.IOutboundSanctionAccessor sanctions,
    ILogger<ChatFilterExecutionHandler> logger
) : IEventHandler<ChatMessageReceivedEvent>
{
    private const int DefaultTimeoutSeconds = 600;
    private const int QueueCategoryMaxLength = 50;

    public async Task HandleAsync(
        ChatMessageReceivedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        // The channel's own saved filters are what authorise every enforcement below; without this the
        // transport refuses the timeout, which is the correct default for anything acting on its own.
        using IDisposable sanction = sanctions.Begin(
            NomNomzBot.Application.Contracts.Security.OutboundSanction.ChannelConfiguration(
                "chat_filter"
            )
        );

        // Enforcement rides Helix (Twitch-only), and the broadcaster + moderators are never auto-filtered.
        if (@event.IsBroadcaster || @event.IsModerator)
            return;
        if (@event.Provider != AuthEnums.Platform.Twitch)
            return;

        Guid broadcasterId = @event.BroadcasterId;
        if (broadcasterId == Guid.Empty || string.IsNullOrEmpty(@event.Message))
            return;

        List<ChatFilter> filters = await db
            .ChatFilters.Where(f => f.BroadcasterId == broadcasterId && f.IsEnabled)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync(cancellationToken);
        if (filters.Count == 0)
            return;

        int senderLevel = ChatRole
            .Resolve(
                @event.IsBroadcaster,
                @event.IsModerator,
                @event.IsVip,
                @event.IsSubscriber,
                @event.Badges
            )
            .ToLevelValue();

        foreach (ChatFilter filter in filters)
        {
            if (senderLevel >= filter.ExemptMinRoleLevel)
                continue; // this sender outranks the filter's exemption floor
            if (!Matches(filter, @event.Message))
                continue;

            logger.LogInformation(
                "Chat filter '{Filter}' ({Type}/{Action}) matched user {User} in channel {Channel}",
                filter.Name,
                filter.FilterType,
                filter.Action,
                @event.UserLogin,
                broadcasterId
            );

            await EnforceAsync(filter, @event, broadcasterId, cancellationToken);

            filter.MatchCount++;
            await db.SaveChangesAsync(cancellationToken);
            return; // enforce only the first matching filter
        }
    }

    private async Task EnforceAsync(
        ChatFilter filter,
        ChatMessageReceivedEvent @event,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        string reason = $"Chat filter: {filter.Name}";
        switch (filter.Action)
        {
            case ChatFilterAction.Delete:
                await moderation.DeleteChatMessageAsync(broadcasterId, @event.MessageId, ct);
                break;

            case ChatFilterAction.Timeout:
                await moderation.TimeoutUserAsync(
                    broadcasterId,
                    @event.UserId,
                    filter.TimeoutSeconds ?? DefaultTimeoutSeconds,
                    reason,
                    ct
                );
                break;

            case ChatFilterAction.Escalate:
                await EscalateAsync(filter, @event, broadcasterId, reason, ct);
                break;

            case ChatFilterAction.Hold:
                Result deleted = await moderation.DeleteChatMessageAsync(
                    broadcasterId,
                    @event.MessageId,
                    ct
                );
                if (deleted.IsFailure)
                    logger.LogWarning(
                        "Chat filter '{Filter}' could not remove the held message {MessageId}: {Error}",
                        filter.Name,
                        @event.MessageId,
                        deleted.ErrorMessage
                    );
                await QueueForReviewAsync(filter, @event, broadcasterId, ct);
                break;

            case ChatFilterAction.Flag:
                await QueueForReviewAsync(filter, @event, broadcasterId, ct);
                break;
        }
    }

    private async Task QueueForReviewAsync(
        ChatFilter filter,
        ChatMessageReceivedEvent @event,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        Result<Guid> queued = await queue.EnqueueHeldMessageAsync(
            broadcasterId,
            @event.MessageId,
            @event.UserId,
            @event.UserLogin,
            @event.Message,
            filter.Name.Length <= QueueCategoryMaxLength
                ? filter.Name
                : filter.Name[..QueueCategoryMaxLength],
            ct,
            ModerationQueueSource.ChatFilter
        );
        if (queued.IsFailure)
            logger.LogWarning(
                "Chat filter '{Filter}' could not queue the message {MessageId} for review: {Error}",
                filter.Name,
                @event.MessageId,
                queued.ErrorMessage
            );
    }

    /// <summary>
    /// The escalate path: the message is always removed first. Then the sender is resolved to their internal
    /// user id, one offense is recorded on the ladder, and the ladder's decision is applied. When the ladder is
    /// off (or cannot answer) the filter's own timeout is applied instead, so an escalate rule always acts.
    /// </summary>
    private async Task EscalateAsync(
        ChatFilter filter,
        ChatMessageReceivedEvent @event,
        Guid broadcasterId,
        string reason,
        CancellationToken ct
    )
    {
        Result deleted = await moderation.DeleteChatMessageAsync(
            broadcasterId,
            @event.MessageId,
            ct
        );
        if (deleted.IsFailure)
            logger.LogWarning(
                "Chat filter '{Filter}' could not remove the escalated message {MessageId}: {Error}",
                filter.Name,
                @event.MessageId,
                deleted.ErrorMessage
            );

        Result<UserDto> subject = await users.GetOrCreateAsync(
            @event.UserId,
            @event.UserLogin,
            @event.UserDisplayName,
            @event.Provider,
            ct
        );
        if (subject.IsFailure || !Guid.TryParse(subject.Value.Id, out Guid subjectUserId))
        {
            logger.LogWarning(
                "Chat filter escalate could not resolve user {User} to an internal id",
                @event.UserLogin
            );
            await TimeoutForFilterAsync(filter, @event, broadcasterId, reason, ct);
            return;
        }

        Result<EscalationDecision> decision = await escalation.ResolveAndRecordAsync(
            broadcasterId,
            subjectUserId,
            @event.UserId,
            ct
        );
        if (decision.IsFailure)
        {
            logger.LogDebug(
                "Escalation ladder declined to act for {User}: {Error}; using the filter's own timeout",
                @event.UserLogin,
                decision.ErrorMessage
            );
            await TimeoutForFilterAsync(filter, @event, broadcasterId, reason, ct);
            return;
        }

        switch (decision.Value.Action)
        {
            case "warn":
                await moderation.WarnChatUserAsync(broadcasterId, @event.UserId, reason, ct);
                break;
            case "timeout":
                await moderation.TimeoutUserAsync(
                    broadcasterId,
                    @event.UserId,
                    decision.Value.TimeoutSeconds ?? DefaultTimeoutSeconds,
                    reason,
                    ct
                );
                break;
            case "ban":
                await moderation.BanUserAsync(broadcasterId, @event.UserId, reason, ct);
                break;
            default:
                logger.LogWarning(
                    "Escalation ladder returned an unknown action '{Action}'",
                    decision.Value.Action
                );
                break;
        }
    }

    private async Task TimeoutForFilterAsync(
        ChatFilter filter,
        ChatMessageReceivedEvent @event,
        Guid broadcasterId,
        string reason,
        CancellationToken ct
    ) =>
        await moderation.TimeoutUserAsync(
            broadcasterId,
            @event.UserId,
            filter.TimeoutSeconds ?? DefaultTimeoutSeconds,
            reason,
            ct
        );

    private static bool Matches(ChatFilter filter, string message) =>
        filter.FilterType switch
        {
            ChatFilterType.Regex => MatchesRegex(filter, message),
            ChatFilterType.Blocklist => MatchesBlocklist(filter, message),
            ChatFilterType.LinkPolicy => UrlPattern().IsMatch(message),
            _ => false,
        };

    private static bool MatchesRegex(ChatFilter filter, string message)
    {
        if (string.IsNullOrEmpty(filter.Pattern))
            return false;
        try
        {
            RegexOptions options = filter.IsCaseSensitive
                ? RegexOptions.None
                : RegexOptions.IgnoreCase;
            return Regex.IsMatch(message, filter.Pattern, options, ChatFilterService.MatchTimeout);
        }
        catch (ArgumentException)
        {
            return false; // a malformed pattern never matches (validated at create time)
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool MatchesBlocklist(ChatFilter filter, string message)
    {
        List<string>? terms = DeserializeTerms(filter.TermsJson);
        if (terms is not { Count: > 0 })
            return false;

        StringComparison comparison = filter.IsCaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        foreach (string term in terms)
            if (!string.IsNullOrEmpty(term) && message.Contains(term, comparison))
                return true;

        return false;
    }

    private static List<string>? DeserializeTerms(string? termsJson)
    {
        if (string.IsNullOrEmpty(termsJson))
            return null;
        try
        {
            return JsonSerializer.Deserialize<List<string>>(termsJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"https?://[^\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
}
