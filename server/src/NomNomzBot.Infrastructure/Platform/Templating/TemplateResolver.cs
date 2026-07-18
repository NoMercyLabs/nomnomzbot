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
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.PickLists.Services;
using NomNomzBot.Application.ViewerData.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.CustomEvents;

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

        // Expand {list.pick.<name>} FIRST — replace each with a random entry from the broadcaster's named
        // pick-list, so any placeholders the picked entry itself carries ({user}, {target}, or another nested
        // {list.pick.…}) flow through the normal resolution below. A missing/empty list expands to empty.
        if (
            broadcasterId is not null
            && template.Contains("{list.pick.", StringComparison.OrdinalIgnoreCase)
        )
            template = await ExpandListPicksAsync(template, broadcasterId.Value, cancellationToken);

        // Expand {custom.<name>.<field>} — substitute each with the latest ingested value of that field from
        // the broadcaster's named custom-data source (the D4 latest-value cache). A missing source/field or
        // any read failure expands to empty; runs alongside the pick-list pre-pass, before the main pass.
        if (
            broadcasterId is not null
            && template.Contains("{custom.", StringComparison.OrdinalIgnoreCase)
        )
            template = await ExpandCustomDataAsync(
                template,
                broadcasterId.Value,
                cancellationToken
            );

        // Build a merged variable bag: start with seeds, fill in auto-resolved on demand
        Dictionary<string, string> vars = new(seedVariables, StringComparer.OrdinalIgnoreCase);

        // Extract all placeholders used in the template so we only resolve what's needed
        HashSet<string> needed = ExtractPlaceholders(template);
        if (needed.Count == 0)
            return template;

        // Resolve built-in variable groups lazily
        await ResolveBuiltInsAsync(vars, needed, broadcasterId, cancellationToken);

        // Final substitution — a pronoun-grammar placeholder whose own first letter is uppercase
        // (e.g. {Subject}, {User.Subject}, {Target.GenderedTerm}) capitalizes the resolved value.
        return VariablePattern()
            .Replace(
                template,
                match =>
                {
                    string key = match.Groups[1].Value.Trim();
                    if (!vars.TryGetValue(key, out string? val))
                        return ResolveVerbAgreement(key, vars) ?? match.Value;
                    return IsPronounGrammarKey(key) && key.Length > 0 && char.IsUpper(key[0])
                        ? CapitalizeFirst(val)
                        : val;
                }
            );
    }

    /// <summary>
    /// Resolves the value-carrying verb-agreement form <c>{verb:sings|sing}</c> (also
    /// <c>{user.verb:…}</c>/<c>{target.verb:…}</c>): picks the singular form when the relevant side's
    /// grammatical number is singular (presentTense "is"), else the plural form — so
    /// "{Subject} {verb:plays|play} games" agrees for he/she AND they. Returns null for non-verb keys.
    /// </summary>
    private static string? ResolveVerbAgreement(string key, Dictionary<string, string> vars)
    {
        string side;
        string payload;
        if (key.StartsWith("verb:", StringComparison.OrdinalIgnoreCase))
        {
            side = string.Empty;
            payload = key["verb:".Length..];
        }
        else if (key.StartsWith("user.verb:", StringComparison.OrdinalIgnoreCase))
        {
            side = "user.";
            payload = key["user.verb:".Length..];
        }
        else if (key.StartsWith("target.verb:", StringComparison.OrdinalIgnoreCase))
        {
            side = "target.";
            payload = key["target.verb:".Length..];
        }
        else
        {
            return null;
        }

        string[] forms = payload.Split('|', 2);
        if (forms.Length != 2)
            return null; // malformed — leave the raw token so the author sees the mistake

        bool singular = string.Equals(
            vars.GetValueOrDefault($"{side}presenttense"),
            "is",
            StringComparison.OrdinalIgnoreCase
        );
        return singular ? forms[0] : forms[1];
    }

    /// <summary>
    /// Replaces every <c>{list.pick.&lt;name&gt;}</c> placeholder with a uniformly random entry from that broadcaster's
    /// named pick-list (via <see cref="IPickListService.PickRandomAsync"/>). Runs before the main pass so the
    /// picked entry's own placeholders resolve with everything else; a missing/empty list yields an empty string.
    /// Depth-bounded so an entry that references another list (or itself) can never loop forever.
    /// </summary>
    private async Task<string> ExpandListPicksAsync(
        string template,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        const int maxDepth = 5;
        string current = template;

        for (int depth = 0; depth < maxDepth; depth++)
        {
            MatchCollection matches = ListPickPattern().Matches(current);
            if (matches.Count == 0)
                return current;

            using IServiceScope scope = _scopeFactory.CreateScope();
            IPickListService pickLists =
                scope.ServiceProvider.GetRequiredService<IPickListService>();

            System.Text.StringBuilder builder = new();
            int cursor = 0;
            foreach (Match match in matches)
            {
                builder.Append(current, cursor, match.Index - cursor);
                string name = match.Groups[1].Value.Trim();
                Result<string> pick = await pickLists.PickRandomAsync(broadcasterId, name, ct);
                // A missing/empty list (or any failure) resolves to an empty string — never throws.
                builder.Append(pick.IsSuccess ? pick.Value : string.Empty);
                cursor = match.Index + match.Length;
            }
            builder.Append(current, cursor, current.Length - cursor);
            current = builder.ToString();
        }

        return current;
    }

    /// <summary>
    /// Replaces every <c>{custom.&lt;name&gt;.&lt;field&gt;}</c> placeholder with the latest ingested value of that field
    /// from the broadcaster's named custom-data source — read from the D4 latest-value cache
    /// (<c>customdata:{broadcasterId}:{name}</c>) written by <see cref="CustomDataIngestService"/>. A missing
    /// source, a missing field, or any read failure resolves to an empty string; never throws, never leaves the
    /// raw token. Runs before the main pass so surrounding text and other placeholders resolve normally.
    /// </summary>
    private async Task<string> ExpandCustomDataAsync(
        string template,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        MatchCollection matches = CustomDataPattern().Matches(template);
        if (matches.Count == 0)
            return template;

        using IServiceScope scope = _scopeFactory.CreateScope();
        ICacheService cache = scope.ServiceProvider.GetRequiredService<ICacheService>();

        // One cache read per distinct source referenced, reused across its fields within this template.
        Dictionary<string, CustomDataLatestValue?> bySource = new(StringComparer.OrdinalIgnoreCase);

        System.Text.StringBuilder builder = new();
        int cursor = 0;
        foreach (Match match in matches)
        {
            builder.Append(template, cursor, match.Index - cursor);
            string name = match.Groups[1].Value;
            string field = match.Groups[2].Value;

            if (!bySource.TryGetValue(name, out CustomDataLatestValue? latest))
            {
                latest = await ReadLatestCustomDataAsync(cache, broadcasterId, name, ct);
                bySource[name] = latest;
            }

            // Missing source OR missing field → empty string (mirrors the pick-list missing-list path).
            builder.Append(
                latest is not null && latest.Fields.TryGetValue(field, out string? value)
                    ? value
                    : string.Empty
            );
            cursor = match.Index + match.Length;
        }
        builder.Append(template, cursor, template.Length - cursor);
        return builder.ToString();
    }

    /// <summary>
    /// Reads the D4 latest-value cache for one source, matching the exact key
    /// <see cref="CustomDataIngestService"/> writes. Any failure yields null, which expands to an empty string.
    /// </summary>
    private async Task<CustomDataLatestValue?> ReadLatestCustomDataAsync(
        ICacheService cache,
        Guid broadcasterId,
        string sourceName,
        CancellationToken ct
    )
    {
        try
        {
            string cacheKey = $"customdata:{broadcasterId}:{sourceName}";
            return await cache.GetAsync<CustomDataLatestValue>(cacheKey, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to read latest custom-data value for source {Source} on tenant {BroadcasterId}",
                sourceName,
                broadcasterId
            );
            return null;
        }
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

        // {tense}/{user.tense}/{target.tense} and {verb:sing|plur} derive from the underlying grammar
        // vars (present/past tense, grammatical number) — widen `needed` up front so those resolve.
        ExpandDerivedGrammarNeeds(needed);

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

        // ── Pronoun grammar variables ({subject}/{object}/{possessive}/{presentTense}/{genderedTerm},
        // {user.*}, {target.*}) — bare vars mirror the @mention target when one is present in context,
        // else the triggering user; explicit user./target. forms always resolve their own side.
        bool needsUserGrammar = PronounGrammarSuffixes.Any(s =>
            needed.Contains(s) || needed.Contains($"user.{s}")
        );
        bool hasTargetContext = !string.IsNullOrEmpty(vars.GetValueOrDefault("target"));
        bool needsTargetGrammar =
            PronounGrammarSuffixes.Any(s => needed.Contains($"target.{s}"))
            || (hasTargetContext && PronounGrammarSuffixes.Any(needed.Contains));

        // ── User DB lookups (follow age, account age, pronouns, pronoun grammar) ─
        if (
            NeedsAny(
                needed,
                "user.followAge",
                "user.accountAge",
                "user.pronouns",
                "user.messageCount"
            ) || needsUserGrammar
        )
        {
            string? userId = vars.GetValueOrDefault("user.id");
            if (!string.IsNullOrEmpty(userId) && broadcasterId is not null)
            {
                await ResolveUserDbFieldsAsync(vars, userId, broadcasterId.Value, ct);
            }
        }

        // ── Target DB lookups (id, name, follow age, pronoun grammar) ───────
        if (NeedsAny(needed, "target.id", "target.name", "target.followAge") || needsTargetGrammar)
        {
            string? targetName = vars.GetValueOrDefault("target");
            if (!string.IsNullOrEmpty(targetName))
            {
                await ResolveTargetAsync(vars, targetName, ct);
            }
        }

        // Bare grammar vars mirror the target (if present) else the caller; anything still unset (no
        // user/target resolved, or a resolved one with no pronoun on record) gets the universal
        // they/them/their/person/are fallback so a pronoun placeholder never renders raw.
        ApplyPronounGrammarBareAndFallback(vars, needed, hasTargetContext);

        // ── Live-state grammar: {status} = live/offline; {tense} = presentTense while live, pastTense
        // once offline ("StoneyEagle is live" / "StoneyEagle was live"). Sides mirror the grammar vars.
        bool isLive = channelCtx?.IsLive == true;
        if (needed.Contains("status"))
            vars.TryAdd("status", isLive ? "live" : "offline");
        foreach (string side in GrammarSides)
        {
            if (!needed.Contains($"{side}tense"))
                continue;
            string source = isLive ? $"{side}presenttense" : $"{side}pasttense";
            string fallback = isLive ? "are" : "were";
            vars.TryAdd($"{side}tense", vars.GetValueOrDefault(source, fallback));
        }

        // ── Profile links: {link}/{user.link}/{target.link} → twitch.tv/<login>. The bare form mirrors
        // the @mention target when one is present (consistent with the bare grammar vars).
        if (needed.Contains("user.link") || (needed.Contains("link") && !hasTargetContext))
        {
            string? login = vars.GetValueOrDefault("user.name") ?? vars.GetValueOrDefault("user");
            if (!string.IsNullOrEmpty(login))
                vars.TryAdd("user.link", $"twitch.tv/{login.ToLowerInvariant()}");
        }
        if (needed.Contains("target.link") || (needed.Contains("link") && hasTargetContext))
        {
            string? login =
                vars.GetValueOrDefault("target.name") ?? vars.GetValueOrDefault("target");
            if (!string.IsNullOrEmpty(login))
                vars.TryAdd("target.link", $"twitch.tv/{login.ToLowerInvariant()}");
        }
        if (needed.Contains("link"))
        {
            string mirrorKey = hasTargetContext ? "target.link" : "user.link";
            if (vars.TryGetValue(mirrorKey, out string? mirroredLink))
                vars.TryAdd("link", mirroredLink);
        }

        // ── Last chat message: {user.lastmessage}/{target.lastmessage} (the viewer's most recent
        // non-command chat line in this channel; unset renders empty) ──────
        if (broadcasterId is not null && NeedsAny(needed, "user.lastmessage", "target.lastmessage"))
        {
            await ResolveLastMessagesAsync(vars, needed, broadcasterId.Value, ct);
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

            // Grammar helpers key off the primary pronoun only (AltPronoun only drives the display badge).
            ApplyPronounGrammarVars(vars, "user.", user.Pronoun);

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

            // Include Pronoun so {target.*} grammar vars (and a future target pronoun display) resolve —
            // mirrors the same fix already in place for {user.*} in ResolveUserDbFieldsAsync.
            User? target = await db
                .Users.Include(u => u.Pronoun)
                .FirstOrDefaultAsync(u => u.Username == targetName.ToLowerInvariant(), ct);

            if (target is null)
                return;

            // {{target.id}} is the Twitch user string id, not the internal Guid PK.
            vars.TryAdd("target.id", target.TwitchUserId!);
            vars.TryAdd("target.name", target.Username ?? targetName);
            vars.TryAdd("target.followAge", "unknown");

            ApplyPronounGrammarVars(vars, "target.", target.Pronoun);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve target {TargetName}", targetName);
        }
    }

    /// <summary>
    /// Populates the five grammar vars (<c>{prefix}subject/object/possessive/presentTense/genderedTerm</c>)
    /// from a resolved <see cref="Pronoun"/> row. presentTense derives from <see cref="Pronoun.Singular"/>
    /// (no stored column). A null pronoun (no row on record) leaves the group unset — the universal
    /// they/them/their/person/are fallback is applied afterwards in
    /// <see cref="ApplyPronounGrammarBareAndFallback"/>, never here, so callers never see a partial mix.
    /// </summary>
    private static void ApplyPronounGrammarVars(
        Dictionary<string, string> vars,
        string prefix,
        Pronoun? pronoun
    )
    {
        if (pronoun is null)
            return;

        vars.TryAdd($"{prefix}subject", pronoun.Subject);
        vars.TryAdd($"{prefix}object", pronoun.Object);
        vars.TryAdd($"{prefix}possessive", pronoun.Possessive);
        vars.TryAdd($"{prefix}presenttense", pronoun.Singular ? "is" : "are");
        vars.TryAdd($"{prefix}pasttense", pronoun.Singular ? "was" : "were");
        vars.TryAdd($"{prefix}genderedterm", pronoun.GenderedTerm);
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

    // ─── Pronoun grammar variables ──────────────────────────────────────────

    /// <summary>The five grammar variable names, bare form (canonical lowercase; lookups are case-insensitive).</summary>
    private static readonly string[] PronounGrammarSuffixes =
    [
        "subject",
        "object",
        "possessive",
        "presenttense",
        "pasttense",
        "genderedterm",
    ];

    /// <summary>The three grammar sides: bare (mirrors target-if-present else user), explicit user., explicit target.</summary>
    private static readonly string[] GrammarSides = ["", "user.", "target."];

    /// <summary>The universal fallback for a viewer with no pronoun on record: they/them/their/person/are/were.</summary>
    private static readonly IReadOnlyDictionary<string, string> PronounGrammarFallback =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["subject"] = "they",
            ["object"] = "them",
            ["possessive"] = "their",
            ["presenttense"] = "are",
            ["pasttense"] = "were",
            ["genderedterm"] = "person",
        };

    /// <summary>
    /// Bare grammar vars ({subject}/{object}/…) mirror the @mention target's pronoun when a target is
    /// present in context, else the triggering user's. Afterwards, any grammar key — bare or explicit
    /// user./target. — still unset (no user/target resolved, or a resolved one with no pronoun on
    /// record) gets the they/them/their/person/are fallback, so a pronoun placeholder never renders raw.
    /// </summary>
    private static void ApplyPronounGrammarBareAndFallback(
        Dictionary<string, string> vars,
        HashSet<string> needed,
        bool hasTargetContext
    )
    {
        string bareSource = hasTargetContext ? "target" : "user";
        foreach (string suffix in PronounGrammarSuffixes)
        {
            if (
                needed.Contains(suffix)
                && vars.TryGetValue($"{bareSource}.{suffix}", out string? mirrored)
            )
                vars.TryAdd(suffix, mirrored);
        }

        foreach (string suffix in PronounGrammarSuffixes)
        {
            string fallback = PronounGrammarFallback[suffix];
            if (needed.Contains(suffix))
                vars.TryAdd(suffix, fallback);
            if (needed.Contains($"user.{suffix}"))
                vars.TryAdd($"user.{suffix}", fallback);
            if (needed.Contains($"target.{suffix}"))
                vars.TryAdd($"target.{suffix}", fallback);
        }
    }

    /// <summary>Whether a placeholder key (any casing) names a pronoun-grammar variable — bare or user./target.
    /// {tense} counts too: it renders a grammar word (is/are/was/were) and honors the same capitalization rule.</summary>
    private static bool IsPronounGrammarKey(string key)
    {
        string lower = key.ToLowerInvariant();
        return Array.IndexOf(PronounGrammarSuffixes, lower) >= 0
            || lower is "tense" or "user.tense" or "target.tense"
            || PronounGrammarSuffixes.Any(s => lower == $"user.{s}" || lower == $"target.{s}");
    }

    /// <summary>
    /// Widens the needed-set for derived grammar placeholders: {tense} needs presentTense+pastTense of its
    /// side; {verb:…} needs presentTense of its side (grammatical-number source). Bare forms widen the bare
    /// vars, which mirror target-if-present else user like every other bare grammar placeholder.
    /// </summary>
    private static void ExpandDerivedGrammarNeeds(HashSet<string> needed)
    {
        foreach (string side in GrammarSides)
        {
            bool wantsTense = needed.Contains($"{side}tense");
            bool wantsVerb = needed.Any(n =>
                n.StartsWith($"{side}verb:", StringComparison.OrdinalIgnoreCase)
            );

            if (wantsTense || wantsVerb)
                needed.Add($"{side}presenttense");
            if (wantsTense)
                needed.Add($"{side}pasttense");
        }
    }

    /// <summary>
    /// Resolves {user.lastmessage}/{target.lastmessage} — the viewer's most recent surviving non-command
    /// chat line in this channel (user side keyed by the platform user id, target side by login). A viewer
    /// with no chat history renders empty, never the raw token.
    /// </summary>
    private async Task ResolveLastMessagesAsync(
        Dictionary<string, string> vars,
        HashSet<string> needed,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            if (needed.Contains("user.lastmessage") && !vars.ContainsKey("user.lastmessage"))
            {
                string? userId = vars.GetValueOrDefault("user.id");
                string message = string.Empty;
                if (!string.IsNullOrEmpty(userId))
                {
                    message =
                        await db
                            .ChatMessages.AsNoTracking()
                            .Where(m =>
                                m.BroadcasterId == broadcasterId
                                && m.UserId == userId
                                && !m.IsCommand
                            )
                            .OrderByDescending(m => m.CreatedAt)
                            .Select(m => m.Message)
                            .FirstOrDefaultAsync(ct)
                        ?? string.Empty;
                }
                vars["user.lastmessage"] = message;
            }

            if (needed.Contains("target.lastmessage") && !vars.ContainsKey("target.lastmessage"))
            {
                string? targetLogin = vars.GetValueOrDefault("target");
                string message = string.Empty;
                if (!string.IsNullOrEmpty(targetLogin))
                {
                    string login = targetLogin.ToLowerInvariant();
                    message =
                        await db
                            .ChatMessages.AsNoTracking()
                            .Where(m =>
                                m.BroadcasterId == broadcasterId
                                && m.Username == login
                                && !m.IsCommand
                            )
                            .OrderByDescending(m => m.CreatedAt)
                            .Select(m => m.Message)
                            .FirstOrDefaultAsync(ct)
                        ?? string.Empty;
                }
                vars["target.lastmessage"] = message;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to resolve last messages for tenant {BroadcasterId}",
                broadcasterId
            );
            vars.TryAdd("user.lastmessage", string.Empty);
            vars.TryAdd("target.lastmessage", string.Empty);
        }
    }

    private static string CapitalizeFirst(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

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

    [GeneratedRegex(@"\{list\.pick\.([^{}]+)\}", RegexOptions.IgnoreCase)]
    private static partial Regex ListPickPattern();

    [GeneratedRegex(@"\{custom\.([A-Za-z0-9_-]+)\.([A-Za-z0-9_-]+)\}", RegexOptions.IgnoreCase)]
    private static partial Regex CustomDataPattern();

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex VariablePattern();
}
