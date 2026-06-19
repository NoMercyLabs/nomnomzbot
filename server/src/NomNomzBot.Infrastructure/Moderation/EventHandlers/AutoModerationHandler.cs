// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Entities;
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
    private static readonly TimeSpan RuleCacheExpiry = TimeSpan.FromMinutes(5);

    // Per-channel rule cache: key = broadcasterId
    private readonly ConcurrentDictionary<string, CachedRules> _ruleCache = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AutoModerationHandler> _logger;

    public AutoModerationHandler(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<AutoModerationHandler> logger
    )
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
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

        string broadcasterId = @event.BroadcasterId;
        if (string.IsNullOrEmpty(broadcasterId) || string.IsNullOrEmpty(@event.Message))
            return;

        IReadOnlyList<AutoModRule> rules = await GetRulesAsync(broadcasterId, cancellationToken);
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
            && t is JsonElement te
            && te.ValueKind == JsonValueKind.Number
                ? te.GetDouble()
                : 0.7; // Default: 70% caps

        int minLength =
            rule.Settings.TryGetValue("min_length", out object? ml)
            && ml is JsonElement mle
            && mle.ValueKind == JsonValueKind.Number
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
        IReadOnlyList<NomNomzBot.Domain.Chat.ValueObjects.ChatMessageFragment> fragments,
        AutoModRule rule
    )
    {
        int maxEmotes =
            rule.Settings.TryGetValue("max_emotes", out object? maxObj)
            && maxObj is JsonElement maxElem
            && maxElem.ValueKind == JsonValueKind.Number
                ? maxElem.GetInt32()
                : 10; // Default: 10 emotes max

        int emoteCount = fragments.Count(f =>
            f.Type.Equals("emote", StringComparison.OrdinalIgnoreCase)
        );
        return emoteCount > maxEmotes;
    }

    private static bool ShouldApply(AutoModRule rule, ChatMessageReceivedEvent @event)
    {
        // Check exempt roles
        if (rule.ExemptRoles.Count == 0)
            return true;

        foreach (string role in rule.ExemptRoles)
        {
            bool exempt = role.ToLowerInvariant() switch
            {
                "subscriber" or "sub" => @event.IsSubscriber,
                "vip" => @event.IsVip,
                "moderator" or "mod" => @event.IsModerator,
                "broadcaster" => @event.IsBroadcaster,
                _ => false,
            };
            if (exempt)
                return false;
        }

        return true;
    }

    // ─── Action dispatch ──────────────────────────────────────────────────────

    private async Task ApplyActionAsync(
        AutoModRule rule,
        string broadcasterId,
        string userId,
        string messageId,
        CancellationToken ct
    )
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ITwitchApiService twitchApi =
                scope.ServiceProvider.GetRequiredService<ITwitchApiService>();

            switch (rule.Action.ToLowerInvariant())
            {
                case "timeout":
                    int duration = rule.DurationSeconds ?? 60;
                    await twitchApi.TimeoutUserAsync(
                        broadcasterId,
                        userId,
                        duration,
                        rule.Reason ?? rule.Name,
                        ct
                    );
                    break;

                case "ban":
                    await twitchApi.BanUserAsync(
                        broadcasterId,
                        userId,
                        rule.Reason ?? rule.Name,
                        ct
                    );
                    break;

                case "delete":
                    await twitchApi.DeleteChatMessageAsync(broadcasterId, messageId, ct);
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

    // ─── Rule loading (cached) ────────────────────────────────────────────────

    private async Task<IReadOnlyList<AutoModRule>> GetRulesAsync(
        string broadcasterId,
        CancellationToken ct
    )
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        if (
            _ruleCache.TryGetValue(broadcasterId, out CachedRules? cached)
            && now - cached.CachedAt < RuleCacheExpiry
        )
        {
            return cached.Rules;
        }

        IReadOnlyList<AutoModRule> rules = await LoadRulesFromDbAsync(broadcasterId, ct);
        _ruleCache[broadcasterId] = new(rules, now);
        return rules;
    }

    private async Task<IReadOnlyList<AutoModRule>> LoadRulesFromDbAsync(
        string broadcasterId,
        CancellationToken ct
    )
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            List<Record> records = await db
                .Records.Where(r =>
                    r.BroadcasterId == broadcasterId && r.RecordType == "moderation_rule"
                )
                .ToListAsync(ct);

            return records
                .Select(r =>
                {
                    try
                    {
                        return ParseRule(r.Data);
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(r => r is not null)
                .Select(r => r!)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to load auto-mod rules for {BroadcasterId}",
                broadcasterId
            );
            return [];
        }
    }

    private static AutoModRule ParseRule(string data)
    {
        using JsonDocument doc = JsonDocument.Parse(data);
        JsonElement root = doc.RootElement;

        return new()
        {
            Name = root.TryGetProperty("Name", out JsonElement n)
                ? n.GetString() ?? string.Empty
                : string.Empty,
            Type = root.TryGetProperty("Type", out JsonElement t)
                ? t.GetString() ?? string.Empty
                : string.Empty,
            Action = root.TryGetProperty("Action", out JsonElement a)
                ? a.GetString() ?? "timeout"
                : "timeout",
            IsEnabled = !root.TryGetProperty("IsEnabled", out JsonElement e) || e.GetBoolean(),
            DurationSeconds =
                root.TryGetProperty("DurationSeconds", out JsonElement d)
                && d.ValueKind == JsonValueKind.Number
                    ? d.GetInt32()
                    : (int?)null,
            Reason = root.TryGetProperty("Reason", out JsonElement r) ? r.GetString() : null,
            Settings =
                root.TryGetProperty("Settings", out JsonElement s)
                && s.ValueKind == JsonValueKind.Object
                    ? s.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value.Clone())
                    : new(),
            ExemptRoles =
                root.TryGetProperty("ExemptRoles", out JsonElement er)
                && er.ValueKind == JsonValueKind.Array
                    ? er.EnumerateArray()
                        .Select(x => x.GetString() ?? string.Empty)
                        .Where(x => x.Length > 0)
                        .ToList()
                    : [],
        };
    }

    // ─── Inner types ──────────────────────────────────────────────────────────

    private sealed class AutoModRule
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Action { get; set; } = "timeout";
        public bool IsEnabled { get; set; } = true;
        public int? DurationSeconds { get; set; }
        public string? Reason { get; set; }
        public Dictionary<string, object> Settings { get; set; } = new();
        public List<string> ExemptRoles { get; set; } = [];
    }

    private sealed record CachedRules(IReadOnlyList<AutoModRule> Rules, DateTimeOffset CachedAt);

    [GeneratedRegex(@"https?://[^\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
}
