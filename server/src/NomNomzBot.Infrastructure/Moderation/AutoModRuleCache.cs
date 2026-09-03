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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>One auto-moderation rule as stored in <c>Record.Data</c>.</summary>
public sealed class AutoModRule
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

/// <summary>
/// The per-channel auto-mod rule set, cached across messages and evicted the moment an operator
/// changes it.
/// </summary>
public interface IAutoModRuleCache
{
    Task<IReadOnlyList<AutoModRule>> GetAsync(Guid broadcasterId, CancellationToken ct);

    /// <summary>Drops a channel's entry so the next message re-reads it.</summary>
    void Invalidate(Guid broadcasterId);
}

/// <summary>
/// Rule cache, singleton, invalidated on write.
///
/// <para>This used to live as an instance field on <c>AutoModerationHandler</c>, which is registered
/// <b>scoped</b> — a new handler per event. The cache was therefore rebuilt for every single chat
/// message and never survived to be hit, so it did the exact opposite of what it was written for: a
/// database round-trip per message on the hot path, and a five-minute expiry that could never expire
/// because nothing lived that long.</para>
///
/// <para>Held here as a singleton it actually caches. The five-minute expiry stays only as a backstop
/// for writes that bypass the service layer; the real freshness guarantee is
/// <see cref="AutoModRuleCacheInvalidator"/>, which evicts on the change event so an operator's edit
/// takes effect on the very next message rather than up to five minutes later with no indication.</para>
/// </summary>
public sealed class AutoModRuleCache : IAutoModRuleCache
{
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);
    private const string RuleRecordType = "moderation_rule";

    private readonly ConcurrentDictionary<Guid, CachedRules> _cache = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AutoModRuleCache> _logger;

    public AutoModRuleCache(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<AutoModRuleCache> logger
    )
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AutoModRule>> GetAsync(Guid broadcasterId, CancellationToken ct)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        if (
            _cache.TryGetValue(broadcasterId, out CachedRules? cached)
            && now - cached.CachedAt < Expiry
        )
        {
            return cached.Rules;
        }

        IReadOnlyList<AutoModRule> rules = await LoadAsync(broadcasterId, ct);
        _cache[broadcasterId] = new(rules, now);
        return rules;
    }

    public void Invalidate(Guid broadcasterId) => _cache.TryRemove(broadcasterId, out _);

    private async Task<IReadOnlyList<AutoModRule>> LoadAsync(
        Guid broadcasterId,
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
                    r.BroadcasterId == broadcasterId && r.RecordType == RuleRecordType
                )
                .ToListAsync(ct);

            return
            [
                .. records
                    .Select(r =>
                    {
                        try
                        {
                            return Parse(r.Data);
                        }
                        catch
                        {
                            // One malformed rule must not disarm the other rules in the channel.
                            return null;
                        }
                    })
                    .Where(r => r is not null)
                    .Select(r => r!),
            ];
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

    private static AutoModRule Parse(string data)
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
                    : null,
            Reason = root.TryGetProperty("Reason", out JsonElement r) ? r.GetString() : null,
            Settings =
                root.TryGetProperty("Settings", out JsonElement s)
                && s.ValueKind == JsonValueKind.Object
                    ? s.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value.Clone())
                    : new(),
            ExemptRoles =
                root.TryGetProperty("ExemptRoles", out JsonElement er)
                && er.ValueKind == JsonValueKind.Array
                    ?
                    [
                        .. er.EnumerateArray()
                            .Select(x => x.GetString() ?? string.Empty)
                            .Where(x => x.Length > 0),
                    ]
                    : [],
        };
    }

    private sealed record CachedRules(IReadOnlyList<AutoModRule> Rules, DateTimeOffset CachedAt);
}
