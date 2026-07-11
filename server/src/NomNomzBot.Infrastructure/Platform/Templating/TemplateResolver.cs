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
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.ViewerData.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Templating;

/// <summary>
/// Full implementation of ITemplateResolver with 90+ built-in variables.
///
/// Variable groups:
///   {user.*}         — triggering user info
///   {target.*}       — first argument (@ stripped) resolved to a user
///   {args.*}         — command arguments
///   {channel.*}      — channel info
///   {stream.*}       — live stream info
///   {time.*}         — current time
///   {random.*}       — random helpers
///   {botname}        — bot display name
///   {count.*}        — per-channel named counters (G.4; unset renders 0)
///   {viewer.data.*}  — per-viewer key/value store for the triggering viewer (G.14; unset renders empty)
///   {target.data.*}  — per-viewer key/value store for the @mention target
///   {viewer.*}       — M.1 stat helpers for the triggering viewer (messages, watchtime, firstseen,
///                      redemptions, songrequests) — {target.*} mirrors for the @mention
///
/// Seed variables from the caller always take precedence over auto-resolved values.
/// DB lookups only happen when the template actually contains those variables (lazy resolution).
/// </summary>
public sealed partial class TemplateResolver : ITemplateResolver
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IChannelRegistry _registry;
    private readonly ILogger<TemplateResolver> _logger;
    private readonly TimeProvider _timeProvider;

    public TemplateResolver(
        IServiceScopeFactory scopeFactory,
        IChannelRegistry registry,
        ILogger<TemplateResolver> logger,
        TimeProvider timeProvider
    )
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <summary>Simple synchronous resolve using only provided variables.</summary>
    public string Resolve(string template, IDictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        return VariablePattern()
            .Replace(
                template,
                match =>
                {
                    string key = match.Groups[1].Value.Trim();
                    foreach (KeyValuePair<string, string> kvp in variables)
                    {
                        if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                            return kvp.Value ?? string.Empty;
                    }
                    return match.Value; // leave unknown variables as-is
                }
            );
    }

    /// <summary>Full async resolve with lazy DB lookups for built-in variables.</summary>
    public async Task<string> ResolveAsync(
        string template,
        IDictionary<string, string> seedVariables,
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        // Build a merged variable bag: start with seeds, fill in auto-resolved on demand
        Dictionary<string, string> vars = new(seedVariables, StringComparer.OrdinalIgnoreCase);

        // Extract all placeholders used in the template so we only resolve what's needed
        HashSet<string> needed = ExtractPlaceholders(template);
        if (needed.Count == 0)
            return template;

        // Resolve built-in variable groups lazily
        await ResolveBuiltInsAsync(vars, needed, broadcasterId, cancellationToken);

        // Final substitution
        return VariablePattern()
            .Replace(
                template,
                match =>
                {
                    string key = match.Groups[1].Value.Trim();
                    return vars.TryGetValue(key, out string? val) ? val : match.Value;
                }
            );
    }

    // ─── Built-in resolution ──────────────────────────────────────────────────

    private async Task ResolveBuiltInsAsync(
        Dictionary<string, string> vars,
        HashSet<string> needed,
        Guid? broadcasterId,
        CancellationToken ct
    )
    {
        ChannelContext? channelCtx = broadcasterId is not null
            ? _registry.Get(broadcasterId.Value)
            : null;
        DateTimeOffset now = _timeProvider.GetUtcNow();

        // ── Time variables (no DB needed) ──────────────────────────────────
        if (NeedsAny(needed, "time", "time.utc", "date"))
        {
            vars.TryAdd("time", now.ToString("HH:mm:ss"));
            vars.TryAdd("time.utc", now.UtcDateTime.ToString("HH:mm:ss") + " UTC");
            vars.TryAdd("date", now.ToString("yyyy-MM-dd"));
        }

        // ── Stream/channel variables (from ChannelRegistry) ────────────────
        if (
            NeedsAny(
                needed,
                "stream.title",
                "stream.game",
                "stream.uptime",
                "stream.viewers",
                "stream.isLive",
                "stream.startedAt",
                "channel",
                "channel.display",
                "channel.id",
                "streamer"
            )
        )
        {
            if (channelCtx is not null)
            {
                vars.TryAdd("channel", channelCtx.ChannelName);
                vars.TryAdd("channel.display", channelCtx.DisplayName ?? channelCtx.ChannelName);
                // {{channel.id}} is the Twitch channel string id (transport boundary), never the tenant Guid.
                vars.TryAdd("channel.id", channelCtx.TwitchChannelId);
                vars.TryAdd("streamer", channelCtx.DisplayName ?? channelCtx.ChannelName);
                vars.TryAdd("stream.title", channelCtx.CurrentTitle ?? string.Empty);
                vars.TryAdd("stream.game", channelCtx.CurrentGame ?? string.Empty);
                vars.TryAdd("stream.isLive", channelCtx.IsLive ? "true" : "false");
                vars.TryAdd(
                    "stream.startedAt",
                    channelCtx.WentLiveAt?.ToString("O") ?? string.Empty
                );

                if (channelCtx.IsLive && channelCtx.WentLiveAt.HasValue)
                {
                    TimeSpan uptime = now - channelCtx.WentLiveAt.Value;
                    vars.TryAdd("stream.uptime", FormatUptime(uptime));
                }
                else
                {
                    vars.TryAdd("stream.uptime", "offline");
                }
            }
            else if (broadcasterId is not null && needed.Contains("channel.id"))
            {
                // No live registry context — resolve the Twitch channel string id from the tenant Guid.
                string? twitchChannelId = await ResolveTwitchChannelIdAsync(
                    broadcasterId.Value,
                    ct
                );
                if (twitchChannelId is not null)
                    vars.TryAdd("channel.id", twitchChannelId);
            }
        }

        // ── Random variables ──────────────────────────────────────────────
        if (
            channelCtx is not null
            && needed.Any(n => n.StartsWith("random.", StringComparison.OrdinalIgnoreCase))
        )
        {
            foreach (
                string key in needed.Where(n =>
                    n.StartsWith("random.", StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                if (vars.ContainsKey(key))
                    continue;

                if (key.Equals("random.user", StringComparison.OrdinalIgnoreCase))
                {
                    List<string> chatters = channelCtx.SessionChatters.Values.ToList();
                    vars[key] =
                        chatters.Count > 0
                            ? chatters[Random.Shared.Next(chatters.Count)]
                            : vars.GetValueOrDefault("user", "someone");
                }
                else if (key.StartsWith("random.number.", StringComparison.OrdinalIgnoreCase))
                {
                    // random.number.100 → random 1-100
                    string[] parts = key.Split('.');
                    if (parts.Length == 3 && int.TryParse(parts[2], out int maxVal))
                        vars[key] = Random.Shared.Next(1, maxVal + 1).ToString();
                }
                else if (key.StartsWith("random.pick.", StringComparison.OrdinalIgnoreCase))
                {
                    // random.pick.a.b.c → random pick from ["a", "b", "c"]
                    string[] parts = key.Split('.');
                    if (parts.Length > 2)
                    {
                        string[] options = parts[2..];
                        vars[key] = options[Random.Shared.Next(options.Length)];
                    }
                }
            }
        }

        // ── Bot name (from config / DB) ────────────────────────────────────
        if (
            needed.Contains("botname", StringComparer.OrdinalIgnoreCase)
            && !vars.ContainsKey("botname")
        )
        {
            vars["botname"] = await GetBotNameAsync(ct);
        }

        // ── User DB lookups (follow age, account age, pronouns) ────────────
        if (
            NeedsAny(
                needed,
                "user.followAge",
                "user.accountAge",
                "user.pronouns",
                "user.messageCount"
            )
        )
        {
            string? userId = vars.GetValueOrDefault("user.id");
            if (!string.IsNullOrEmpty(userId) && broadcasterId is not null)
            {
                await ResolveUserDbFieldsAsync(vars, userId, broadcasterId.Value, ct);
            }
        }

        // ── Target DB lookups ──────────────────────────────────────────────
        if (NeedsAny(needed, "target.id", "target.name", "target.followAge"))
        {
            string? targetName = vars.GetValueOrDefault("target");
            if (!string.IsNullOrEmpty(targetName))
            {
                await ResolveTargetAsync(vars, targetName, ct);
            }
        }

        // ── Named counters {count.<key>} ───────────────────────────────────
        List<string> countKeys = needed
            .Where(n =>
                n.StartsWith("count.", StringComparison.OrdinalIgnoreCase)
                && n.Length > "count.".Length
                && !vars.ContainsKey(n)
            )
            .ToList();
        if (countKeys.Count > 0 && broadcasterId is not null)
        {
            await ResolveCountersAsync(vars, countKeys, broadcasterId.Value, ct);
        }

        // ── Per-viewer data + stats ({viewer.data.*}/{target.data.*}/{viewer.*}/{target.*}) ──
        if (broadcasterId is not null)
        {
            await ResolveViewerDataAsync(vars, needed, broadcasterId.Value, ct);
        }
    }

    // ─── DB helpers ───────────────────────────────────────────────────────────

    private async Task ResolveUserDbFieldsAsync(
        Dictionary<string, string> vars,
        string twitchUserId,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            // user.id is the Twitch string id (from the event); look the user up by TwitchUserId, not the Guid PK.
            User? user = await db
                .Users.Include(u => u.Pronoun)
                .Include(u => u.AltPronoun)
                .FirstOrDefaultAsync(u => u.TwitchUserId == twitchUserId, ct);
            if (user is null)
                return;

            if (!vars.ContainsKey("user.accountAge"))
            {
                TimeSpan age = _timeProvider.GetUtcNow().UtcDateTime - user.CreatedAt;
                vars["user.accountAge"] = FormatAge(age);
            }

            if (!vars.ContainsKey("user.pronouns"))
            {
                string? pronounDisplay = UserPronounDisplay.Format(user.Pronoun, user.AltPronoun);
                if (pronounDisplay is not null)
                    vars["user.pronouns"] = pronounDisplay;
            }

            // Follow age & message count would require a ChannelEvent lookup — set placeholders for now
            vars.TryAdd("user.followAge", "unknown");
            vars.TryAdd("user.messageCount", "0");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve user DB fields for {UserId}", twitchUserId);
        }
    }

    private async Task ResolveTargetAsync(
        Dictionary<string, string> vars,
        string targetName,
        CancellationToken ct
    )
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            User? target = await db.Users.FirstOrDefaultAsync(
                u => u.Username == targetName.ToLowerInvariant(),
                ct
            );

            if (target is null)
                return;

            // {{target.id}} is the Twitch user string id, not the internal Guid PK.
            vars.TryAdd("target.id", target.TwitchUserId!);
            vars.TryAdd("target.name", target.Username ?? targetName);
            vars.TryAdd("target.followAge", "unknown");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve target {TargetName}", targetName);
        }
    }

    /// <summary>Referenced counters resolve in one round-trip; unset counters render as 0.</summary>
    private async Task ResolveCountersAsync(
        Dictionary<string, string> vars,
        List<string> countKeys,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            INamedCounterService counters =
                scope.ServiceProvider.GetRequiredService<INamedCounterService>();

            List<string> bare = countKeys.Select(k => k["count.".Length..]).ToList();
            Result<IReadOnlyDictionary<string, long>> loaded = await counters.LoadKeysAsync(
                broadcasterId,
                bare,
                ct
            );
            foreach (string key in countKeys)
            {
                string bareKey = key["count.".Length..];
                long value =
                    loaded.IsSuccess && loaded.Value.TryGetValue(bareKey, out long found)
                        ? found
                        : 0L;
                vars[key] = value.ToString(CultureInfo.InvariantCulture);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to resolve counters for tenant {BroadcasterId}",
                broadcasterId
            );
        }
    }

    /// <summary>
    /// Per-viewer data ({viewer.data.*}/{target.data.*}) and M.1 stat helpers ({viewer.*}/{target.*}) for
    /// the triggering viewer and the @mention target — one scope, one bulk read per side, referenced keys
    /// only. Unset data keys render empty; stats for a never-seen viewer render as honest zeros.
    /// </summary>
    private async Task ResolveViewerDataAsync(
        Dictionary<string, string> vars,
        HashSet<string> needed,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        List<string> viewerDataKeys = needed
            .Where(n =>
                n.StartsWith("viewer.data.", StringComparison.OrdinalIgnoreCase)
                && n.Length > "viewer.data.".Length
                && !vars.ContainsKey(n)
            )
            .ToList();
        List<string> targetDataKeys = needed
            .Where(n =>
                n.StartsWith("target.data.", StringComparison.OrdinalIgnoreCase)
                && n.Length > "target.data.".Length
                && !vars.ContainsKey(n)
            )
            .ToList();
        bool viewerStats = NeedsAny(
            needed,
            "viewer.messages",
            "viewer.watchtime",
            "viewer.firstseen",
            "viewer.redemptions",
            "viewer.songrequests"
        );
        bool targetStats = NeedsAny(
            needed,
            "target.messages",
            "target.watchtime",
            "target.firstseen",
            "target.redemptions",
            "target.songrequests"
        );

        if (viewerDataKeys.Count == 0 && targetDataKeys.Count == 0 && !viewerStats && !targetStats)
            return;

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            if (viewerDataKeys.Count > 0 || viewerStats)
            {
                Guid? viewerId = await ResolveViewerGuidAsync(
                    db,
                    vars.GetValueOrDefault("user.id"),
                    vars.GetValueOrDefault("user.provider"),
                    ct
                );
                await ResolveOneViewerSideAsync(
                    scope,
                    vars,
                    broadcasterId,
                    viewerId,
                    "viewer",
                    viewerDataKeys,
                    viewerStats,
                    ct
                );
            }

            if (targetDataKeys.Count > 0 || targetStats)
            {
                Guid? targetId = await ResolveTargetGuidAsync(
                    db,
                    vars.GetValueOrDefault("target"),
                    ct
                );
                await ResolveOneViewerSideAsync(
                    scope,
                    vars,
                    broadcasterId,
                    targetId,
                    "target",
                    targetDataKeys,
                    targetStats,
                    ct
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to resolve viewer data for tenant {BroadcasterId}",
                broadcasterId
            );
        }
    }

    private static async Task ResolveOneViewerSideAsync(
        IServiceScope scope,
        Dictionary<string, string> vars,
        Guid broadcasterId,
        Guid? viewerId,
        string prefix,
        List<string> dataKeys,
        bool wantsStats,
        CancellationToken ct
    )
    {
        if (dataKeys.Count > 0)
        {
            IReadOnlyDictionary<string, string> data = new Dictionary<string, string>();
            if (viewerId is not null)
            {
                IViewerDataService viewerData =
                    scope.ServiceProvider.GetRequiredService<IViewerDataService>();
                Result<IReadOnlyDictionary<string, string>> loaded = await viewerData.LoadKeysAsync(
                    broadcasterId,
                    viewerId.Value,
                    dataKeys.Select(k => k[(prefix.Length + ".data.".Length)..]).ToList(),
                    ct
                );
                if (loaded.IsSuccess)
                    data = loaded.Value;
            }
            foreach (string key in dataKeys)
            {
                string bareKey = key[(prefix.Length + ".data.".Length)..];
                vars[key] = data.TryGetValue(bareKey, out string? value) ? value : string.Empty;
            }
        }

        if (wantsStats)
        {
            ViewerProfileDto? profile = null;
            if (viewerId is not null)
            {
                IViewerAnalyticsService analytics =
                    scope.ServiceProvider.GetRequiredService<IViewerAnalyticsService>();
                Result<ViewerProfileDto> loaded = await analytics.GetProfileAsync(
                    broadcasterId,
                    viewerId.Value,
                    ct
                );
                if (loaded.IsSuccess)
                    profile = loaded.Value;
            }
            vars.TryAdd($"{prefix}.messages", (profile?.TotalMessages ?? 0).ToString());
            vars.TryAdd($"{prefix}.watchtime", FormatWatchTime(profile?.TotalWatchSeconds ?? 0));
            vars.TryAdd(
                $"{prefix}.firstseen",
                profile?.FirstSeenAt?.ToString("yyyy-MM-dd") ?? "unknown"
            );
            vars.TryAdd($"{prefix}.redemptions", (profile?.TotalRedemptions ?? 0).ToString());
            vars.TryAdd($"{prefix}.songrequests", (profile?.TotalSongRequests ?? 0).ToString());
        }
    }

    /// <summary>
    /// The triggering viewer's platform user Guid: redemption paths seed the Guid directly; chat paths
    /// seed the provider's external id, resolved identity-first (provider + external id), then via the
    /// legacy Twitch-id projection.
    /// </summary>
    private static async Task<Guid?> ResolveViewerGuidAsync(
        IApplicationDbContext db,
        string? externalUserId,
        string? provider,
        CancellationToken ct
    )
    {
        if (string.IsNullOrEmpty(externalUserId))
            return null;
        if (Guid.TryParse(externalUserId, out Guid direct))
            return direct;

        if (!string.IsNullOrEmpty(provider))
        {
            Guid viaIdentity = await db
                .UserIdentities.AsNoTracking()
                .Where(i => i.Provider == provider && i.ProviderUserId == externalUserId)
                .Select(i => i.UserId)
                .FirstOrDefaultAsync(ct);
            if (viaIdentity != Guid.Empty)
                return viaIdentity;
        }

        Guid viaTwitchId = await db
            .Users.AsNoTracking()
            .Where(u => u.TwitchUserId == externalUserId)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);
        return viaTwitchId == Guid.Empty ? null : viaTwitchId;
    }

    private static async Task<Guid?> ResolveTargetGuidAsync(
        IApplicationDbContext db,
        string? targetName,
        CancellationToken ct
    )
    {
        if (string.IsNullOrEmpty(targetName))
            return null;
        string login = targetName.ToLowerInvariant();
        Guid id = await db
            .Users.AsNoTracking()
            .Where(u => u.Username == login)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);
        return id == Guid.Empty ? null : id;
    }

    private static string FormatWatchTime(long totalSeconds)
    {
        long hours = totalSeconds / 3600;
        long minutes = totalSeconds % 3600 / 60;
        return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
    }

    private async Task<string?> ResolveTwitchChannelIdAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ITwitchIdentityResolver resolver =
                scope.ServiceProvider.GetRequiredService<ITwitchIdentityResolver>();
            return await resolver.GetTwitchChannelIdAsync(broadcasterId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to resolve Twitch channel id for tenant {BroadcasterId}",
                broadcasterId
            );
            return null;
        }
    }

    private async Task<string> GetBotNameAsync(CancellationToken ct)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            Service? service = await db
                .Services.Where(s => s.Name == "twitch_bot" && s.Enabled)
                .FirstOrDefaultAsync(ct);

            return service?.UserName ?? "NomNomzBot";
        }
        catch
        {
            return "NomNomzBot";
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static HashSet<string> ExtractPlaceholders(string template)
    {
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in VariablePattern().Matches(template))
            result.Add(m.Groups[1].Value.Trim());
        return result;
    }

    private static bool NeedsAny(HashSet<string> needed, params string[] keys) =>
        keys.Any(k => needed.Contains(k));

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalHours >= 1)
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        if (uptime.TotalMinutes >= 1)
            return $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";
        return $"{uptime.Seconds}s";
    }

    private static string FormatAge(TimeSpan age)
    {
        int years = (int)(age.TotalDays / 365);
        int months = (int)((age.TotalDays % 365) / 30);
        if (years > 0)
            return months > 0
                ? $"{years} year{(years == 1 ? "" : "s")}, {months} month{(months == 1 ? "" : "s")}"
                : $"{years} year{(years == 1 ? "" : "s")}";
        if (months > 0)
            return $"{months} month{(months == 1 ? "" : "s")}";
        int days = (int)age.TotalDays;
        return days > 0 ? $"{days} day{(days == 1 ? "" : "s")}" : "less than a day";
    }

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex VariablePattern();
}
