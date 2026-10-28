// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Caching.Memory;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// Viewer identity fields a hub broadcaster can attach to an outgoing payload — additive on top of the raw
/// Twitch ids/names the translators already carry (translators themselves stay persistence-free by design; this
/// hydration happens only at the broadcast layer). Every field is independently nullable: a viewer with no
/// avatar on file, no resolved pronouns, or no recorded standing in this channel still produces a payload —
/// enrichment degrades field-by-field, it never drops the event.
/// </summary>
public sealed record HubUserEnrichment(
    string? DisplayName,
    string? AvatarUrl,
    string? Pronouns,
    string? CommunityStanding
);

/// <summary>
/// Resolves the <see cref="HubUserEnrichment"/> for a Twitch user id, cached briefly so a burst of hub events for
/// the same viewer (a raid, a hype train, a chat flurry) does not turn into N+1 DB reads. A cache miss or lookup
/// failure resolves to <c>null</c> — callers already treat "no enrichment" as an un-enriched payload, never as an
/// error.
/// </summary>
public interface IHubUserEnricher
{
    Task<HubUserEnrichment?> EnrichAsync(
        Guid broadcasterId,
        string twitchUserId,
        CancellationToken ct = default
    );
}

/// <summary>
/// The uncached data-access boundary <see cref="HubUserEnricher"/> wraps — split out so the cache-gating
/// behavior can be proven independently of how the enrichment is actually loaded (mirrors the
/// provider/cache-gate split already used by <c>PronounResolutionService</c>).
/// </summary>
public interface IHubUserEnrichmentStore
{
    Task<HubUserEnrichment?> LoadAsync(
        Guid broadcasterId,
        string twitchUserId,
        CancellationToken ct = default
    );
}

/// <summary>
/// Cache-gates <see cref="IHubUserEnrichmentStore"/> behind a short TTL (30s — long enough to collapse a burst
/// of hub events for the same viewer into one DB read, short enough that an avatar/pronoun/standing change shows
/// up on the next quiet moment rather than being stuck for a long time). A store failure resolves to <c>null</c>
/// and is NOT cached, so the next event for that viewer gets a fresh attempt rather than being stuck un-enriched
/// for the whole TTL.
/// </summary>
public sealed class HubUserEnricher(
    IHubUserEnrichmentStore store,
    IMemoryCache cache,
    ILogger<HubUserEnricher> logger
) : IHubUserEnricher
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private const string CacheKeyPrefix = "hub:enrich:";

    public async Task<HubUserEnrichment?> EnrichAsync(
        Guid broadcasterId,
        string twitchUserId,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrEmpty(twitchUserId))
            return null;

        string cacheKey = $"{CacheKeyPrefix}{broadcasterId}:{twitchUserId}";
        if (cache.TryGetValue(cacheKey, out HubUserEnrichment? cached))
            return cached;

        HubUserEnrichment? enrichment;
        try
        {
            enrichment = await store.LoadAsync(broadcasterId, twitchUserId, ct);
        }
        catch (Exception ex)
        {
            // A DB hiccup must never break a hub broadcast — degrade to "no enrichment" for THIS call only;
            // don't cache the failure so the next event for this viewer gets a fresh attempt.
            logger.LogDebug(
                ex,
                "Hub user enrichment failed for {BroadcasterId}/{TwitchUserId} — payload sent un-enriched",
                broadcasterId,
                twitchUserId
            );
            return null;
        }

        cache.Set(cacheKey, enrichment, CacheTtl);
        return enrichment;
    }
}
