// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Integrations.Events;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Twitch.Events;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// Twitch features that stopped working because the streamer's grant does not cover them, from the two places
/// the bot records a refusal: EventSub subscription rows Twitch refused (<c>failed</c> with a
/// "Missing required scope" or "missing proper authorization" <c>LastError</c>, see
/// <c>TwitchEventSubHostedService</c>) and the reactive <see cref="ChannelMissingScope"/> rows a refused Helix
/// call writes. Both are fixed by the one-click re-grant on the Integrations page.
/// <para>
/// One item per missing scope, naming the event feeds it blocks and the feature it belongs to. Its key embeds
/// when the gap was recorded: the row is removed when a re-grant restores the scope, so a later loss mints a new
/// key. Refusals that name no scope ("missing proper authorization") collapse into one item whose key embeds the
/// grant fingerprint stored at failure time, so a new refusal after a re-grant surfaces again.
/// </para>
/// </summary>
public sealed class TwitchGrantGapSource(IApplicationDbContext db) : IActionRequiredSource
{
    private const string ScopeKeyPrefix = "twitch-scope:";
    private const string UnauthorizedKeyPrefix = "eventsub-unauthorized:";
    private const string MissingScopePrefix = "Missing required scope ";
    private const string MissingAuthorizationMarker = "missing proper authorization";

    public IReadOnlyCollection<string> KeyPrefixes { get; } =
    [ScopeKeyPrefix, UnauthorizedKeyPrefix];

    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } =
    [
        nameof(EventSubSubscriptionStatusChangedEvent),
        nameof(TwitchHelixReauthRequiredEvent),
        nameof(IntegrationConnectedEvent),
    ];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        List<EventSubSubscription> refused = await db
            .EventSubSubscriptions.IgnoreQueryFilters()
            .Where(s =>
                s.BroadcasterId == channelId
                && s.DeletedAt == null
                && s.Enabled
                && s.Status == "failed"
                && s.LastError != null
            )
            .ToListAsync(cancellationToken);
        List<ChannelMissingScope> recordedGaps = await db
            .ChannelMissingScopes.IgnoreQueryFilters()
            .Where(m => m.BroadcasterId == channelId)
            .ToListAsync(cancellationToken);

        List<ActionRequiredItemDto> items = [.. ScopeItems(refused, recordedGaps)];
        ActionRequiredItemDto? unauthorized = UnauthorizedItem(refused);
        if (unauthorized is not null)
            items.Add(unauthorized);

        return Result.Success(items.Where(i => !dismissedKeys.Contains(i.Id)).ToList());
    }

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));

    private static IEnumerable<ActionRequiredItemDto> ScopeItems(
        List<EventSubSubscription> refused,
        List<ChannelMissingScope> recordedGaps
    )
    {
        Dictionary<string, List<EventSubSubscription>> topicsByScope = refused
            .Select(s => (Scope: ScopeOf(s.LastError), Row: s))
            .Where(x => x.Scope is not null)
            .GroupBy(x => x.Scope!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.Row).ToList(),
                StringComparer.OrdinalIgnoreCase
            );
        Dictionary<string, ChannelMissingScope> gapByScope = recordedGaps
            .GroupBy(g => g.Scope, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> scopes = topicsByScope
            .Keys.Concat(gapByScope.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.Ordinal);

        foreach (string scope in scopes)
        {
            List<EventSubSubscription> topics = topicsByScope.GetValueOrDefault(scope) ?? [];
            ChannelMissingScope? gap = gapByScope.GetValueOrDefault(scope);
            yield return ScopeItem(scope, topics, gap);
        }
    }

    private static ActionRequiredItemDto ScopeItem(
        string scope,
        List<EventSubSubscription> topics,
        ChannelMissingScope? gap
    )
    {
        string? feature = gap?.Feature ?? FeatureScopeMap.FeatureForScope(scope);
        Dictionary<string, string> parameters = new() { ["scope"] = scope };
        if (feature is not null)
            parameters["feature"] = feature;
        if (topics.Count > 0)
            parameters["topics"] = JoinTopics(topics);

        DateTime detectedAt =
            gap?.DetectedAt ?? topics.Select(t => t.UpdatedAt).DefaultIfEmpty().Min();
        long anchor = gap?.DetectedAt.Ticks ?? 0;

        return new ActionRequiredItemDto(
            Id: $"{ScopeKeyPrefix}{scope}:{anchor}",
            Kind: "twitch_scope_missing",
            // A refused event feed means events are not arriving at all; a gap found on a Helix read degrades one
            // feature while everything else keeps working.
            Severity: topics.Count > 0 ? "critical" : "warning",
            TitleKey: "attention_scope_missing_title",
            MessageKey: topics.Count > 0
                ? "attention_scope_missing_topics_message"
                : "attention_scope_missing_message",
            Parameters: parameters,
            DetectedAt: detectedAt,
            DeepLinkRoute: "integrations",
            SourceUserId: null,
            SourceUserName: null,
            Count: Math.Max(topics.Count, 1),
            QueueItemIds: []
        );
    }

    private static ActionRequiredItemDto? UnauthorizedItem(List<EventSubSubscription> refused)
    {
        List<EventSubSubscription> unauthorized =
        [
            .. refused
                .Where(s =>
                    ScopeOf(s.LastError) is null
                    && s.LastError!.Contains(
                        MissingAuthorizationMarker,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .OrderBy(s => s.EventType, StringComparer.Ordinal),
        ];
        if (unauthorized.Count == 0)
            return null;

        return new ActionRequiredItemDto(
            Id: $"{UnauthorizedKeyPrefix}{Fingerprint(unauthorized)}",
            Kind: "eventsub_unauthorized",
            Severity: "critical",
            TitleKey: "attention_eventsub_unauthorized_title",
            MessageKey: "attention_eventsub_unauthorized_message",
            Parameters: new()
            {
                ["topics"] = JoinTopics(unauthorized),
                ["count"] = unauthorized.Count.ToString(),
            },
            DetectedAt: unauthorized.Min(s => s.UpdatedAt),
            DeepLinkRoute: "integrations",
            SourceUserId: null,
            SourceUserName: null,
            Count: unauthorized.Count,
            QueueItemIds: []
        );
    }

    private static string? ScopeOf(string? lastError) =>
        lastError is not null
        && lastError.StartsWith(MissingScopePrefix, StringComparison.OrdinalIgnoreCase)
            ? lastError[MissingScopePrefix.Length..].Trim()
            : null;

    private static string JoinTopics(IEnumerable<EventSubSubscription> rows) =>
        string.Join(", ", rows.Select(r => r.EventType).Distinct().Order(StringComparer.Ordinal));

    /// <summary>
    /// Hash of every refused topic with the error it stored. The stored error embeds the grant fingerprint at
    /// failure time, so a refusal after a re-grant produces a different key.
    /// </summary>
    private static string Fingerprint(IEnumerable<EventSubSubscription> rows)
    {
        string joined = string.Join('\n', rows.Select(r => $"{r.EventType}|{r.LastError}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)))[..16];
    }
}
