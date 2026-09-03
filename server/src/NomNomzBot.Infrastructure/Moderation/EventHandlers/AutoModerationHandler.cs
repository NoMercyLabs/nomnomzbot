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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation.EventHandlers;

/// <summary>
/// Auto-moderation handler that runs on every incoming chat message.
///
/// Supported rule types (stored in Record.Data JSON via ModerationService):
///   - "caps"           — timeout if caps percentage exceeds threshold
///   - "links"          — timeout/ban if message contains a URL
///   - "banned_phrases" — timeout/ban if message contains a banned phrase
///
/// Rules are loaded from the DB per-channel and cached for 5 minutes to avoid hot-path DB hits.
/// Exemptions: moderators and the broadcaster are never auto-moderated.
/// </summary>
public sealed partial class AutoModerationHandler : IEventHandler<ChatMessageReceivedEvent>
{
    /// <summary>Used when a timeout rule carries no duration of its own.</summary>
    private const int DefaultTimeoutSeconds = 60;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAutoModRuleCache _rules;
    private readonly ILogger<AutoModerationHandler> _logger;

    public AutoModerationHandler(
        IServiceScopeFactory scopeFactory,
        IAutoModRuleCache rules,
        ILogger<AutoModerationHandler> logger
    )
    {
        _scopeFactory = scopeFactory;
        _rules = rules;
        _logger = logger;
    }

    public async Task HandleAsync(
        ChatMessageReceivedEvent @event,
        CancellationToken cancellationToken
    )
    {
        // Exempt moderators and broadcaster from auto-mod
        if (@event.IsModerator || @event.IsBroadcaster)
            return;

        // Enforcement rides Helix (timeout/ban/delete) — Twitch-only until a per-platform moderation
        // seam exists. Flagging without the ability to act would be a lie, so non-Twitch skips entirely.
        if (@event.Provider != AuthEnums.Platform.Twitch)
            return;

        Guid broadcasterId = @event.BroadcasterId;
        if (broadcasterId == Guid.Empty || string.IsNullOrEmpty(@event.Message))
            return;

        IReadOnlyList<AutoModRule> rules = await _rules.GetAsync(broadcasterId, cancellationToken);
        if (rules.Count == 0)
            return;

        string message = @event.Message;

        foreach (AutoModRule rule in rules)
        {
            if (!rule.IsEnabled)
                continue;
            if (!ShouldApply(rule, @event))
                continue;

            bool triggered = rule.Type switch
            {
                "caps" => CheckCaps(message, rule),
                "links" => CheckLinks(message),
                "banned_phrases" => CheckBannedPhrases(message, rule),
                "emote_spam" => CheckEmoteSpam(@event.Fragments, rule),
                _ => false,
            };

            if (!triggered)
                continue;

            _logger.LogInformation(
                "AutoMod rule '{Rule}' ({Type}) triggered for user {User} in channel {Channel}: \"{Message}\"",
                rule.Name,
                rule.Type,
                @event.UserLogin,
                broadcasterId,
                message
            );

            // The moderation sub-client resolves the tenant Guid → Twitch id internally;
            // @event.UserId is already the Twitch user id.
            await ApplyActionAsync(
                rule,
                broadcasterId,
                @event.UserId,
                @event.MessageId,
                cancellationToken
            );

            // Stop after first matching rule
            return;
        }
    }

    // ─── Rule checks ──────────────────────────────────────────────────────────

    private static bool CheckCaps(string message, AutoModRule rule)
    {
        // Only test alphabetic characters
        int letters = message.Count(char.IsLetter);
        if (letters < 5)
            return false; // Too short to enforce

        int upper = message.Count(char.IsUpper);
        double ratio = (double)upper / letters;

        double threshold =
            rule.Settings.TryGetValue("threshold", out object? t)
            && t is JsonElement { ValueKind: JsonValueKind.Number } te
                ? te.GetDouble()
                : 0.7; // Default: 70% caps

        int minLength =
            rule.Settings.TryGetValue("min_length", out object? ml)
            && ml is JsonElement { ValueKind: JsonValueKind.Number } mle
                ? mle.GetInt32()
                : 10;

        return message.Length >= minLength && ratio >= threshold;
    }

    private static bool CheckLinks(string message) => UrlPattern().IsMatch(message);

    private static bool CheckBannedPhrases(string message, AutoModRule rule)
    {
        if (!rule.Settings.TryGetValue("phrases", out object? phrasesObj))
            return false;
        if (
            phrasesObj is not JsonElement phrasesElem
            || phrasesElem.ValueKind != JsonValueKind.Array
        )
            return false;

        string lower = message.ToLowerInvariant();
        foreach (JsonElement phrase in phrasesElem.EnumerateArray())
        {
            string? p = phrase.GetString();
            if (!string.IsNullOrEmpty(p) && lower.Contains(p.ToLowerInvariant()))
                return true;
        }

        return false;
    }

    private static bool CheckEmoteSpam(
        IReadOnlyList<Domain.Chat.ValueObjects.ChatMessageFragment> fragments,
        AutoModRule rule
    )
    {
        int maxEmotes =
            rule.Settings.TryGetValue("max_emotes", out object? maxObj)
            && maxObj is JsonElement { ValueKind: JsonValueKind.Number } maxElem
                ? maxElem.GetInt32()
                : 10; // Default: 10 emotes max

        int emoteCount = fragments.Count(f =>
            f.Type.Equals("emote", StringComparison.OrdinalIgnoreCase)
        );
        return emoteCount > maxEmotes;
    }

    private static bool ShouldApply(AutoModRule rule, ChatMessageReceivedEvent @event)
    {
        if (rule.ExemptRoles.Count == 0)
            return true;

        // Exempt the sender if they are at or above any listed role on the unified ladder — the same resolution the
        // chat command gate uses, so a Lead Moderator is exempt wherever a Moderator is, never silently missed.
        int senderLevel = ChatRole
            .Resolve(
                @event.IsBroadcaster,
                @event.IsModerator,
                @event.IsVip,
                @event.IsSubscriber,
                @event.Badges
            )
            .ToLevelValue();

        foreach (string role in rule.ExemptRoles)
            if (senderLevel >= ChatRole.Parse(role).ToLevelValue())
                return false;

        return true;
    }

    // ─── Action dispatch ──────────────────────────────────────────────────────

    private async Task ApplyActionAsync(
        AutoModRule rule,
        Guid broadcasterId,
        string userId,
        string messageId,
        CancellationToken ct
    )
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();

            switch (rule.Action.ToLowerInvariant())
            {
                case "timeout":
                case "ban":
                    await ApplyAccountActionAsync(scope, rule, broadcasterId, userId, ct);
                    break;

                case "delete":
                    // Deleting a message is not an action against the account, so it goes straight to
                    // Helix. Only timeouts and bans are offences the ladder needs to remember.
                    await scope
                        .ServiceProvider.GetRequiredService<ITwitchModerationApi>()
                        .DeleteChatMessageAsync(broadcasterId, messageId, ct);
                    break;

                default:
                    _logger.LogWarning("Unknown auto-mod action '{Action}'", rule.Action);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to apply auto-mod action '{Action}' for user {UserId}",
                rule.Action,
                userId
            );
        }
    }

    /// <summary>
    /// Timeouts and bans go through <see cref="IModerationService"/>, never straight to Helix.
    ///
    /// <para>The direct-to-Helix path used to be the whole of this method, and it silently cost the
    /// channel every offence automod caught: <c>IModerationService</c> is what emits
    /// <c>UserTimedOut</c>/<c>UserBanned</c>, and those events are what <c>ModerationProjectionService</c>
    /// turns into heat. Acting outside it meant the escalation ladder never saw the very offences the
    /// bot had just acted on, so a repeat offender kept arriving at the ladder as a first-timer.</para>
    ///
    /// <para>Issued as the channel owner, matching the spam executor: this is the channel's own
    /// automation, and no dashboard user is in the loop to attribute it to.</para>
    /// </summary>
    private async Task ApplyAccountActionAsync(
        IServiceScope scope,
        AutoModRule rule,
        Guid broadcasterId,
        string userId,
        CancellationToken ct
    )
    {
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        Guid ownerUserId = await db
            .Channels.Where(c => c.Id == broadcasterId)
            .Select(c => c.OwnerUserId)
            .FirstOrDefaultAsync(ct);

        if (ownerUserId == Guid.Empty)
        {
            _logger.LogWarning(
                "Auto-mod could not resolve the owner of channel {BroadcasterId}; '{Action}' not applied",
                broadcasterId,
                rule.Action
            );
            return;
        }

        IModerationService moderation =
            scope.ServiceProvider.GetRequiredService<IModerationService>();

        string reason = rule.Reason ?? rule.Name;

        Result<ModerationActionResult> result =
            rule.Action.ToLowerInvariant() == "ban"
                ? await moderation.BanAsync(
                    broadcasterId.ToString(),
                    ownerUserId,
                    userId,
                    reason,
                    null,
                    ct
                )
                : await moderation.TimeoutAsync(
                    broadcasterId.ToString(),
                    ownerUserId,
                    userId,
                    rule.DurationSeconds ?? DefaultTimeoutSeconds,
                    reason,
                    null,
                    ct
                );

        if (result.IsFailure)
            _logger.LogWarning(
                "Auto-mod '{Action}' failed for user {UserId} in {BroadcasterId}: {Error}",
                rule.Action,
                userId,
                broadcasterId,
                result.ErrorMessage
            );
    }

    [GeneratedRegex(@"https?://[^\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
}
