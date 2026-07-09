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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation;

public class ModerationService : IModerationService
{
    private const string RuleRecordType = "moderation_rule";
    private const string ActionRecordType = "moderation_action";

    private readonly IApplicationDbContext _db;
    private readonly ITwitchModerationApi _moderation;
    private readonly ILogger<ModerationService> _logger;
    private readonly IEventBus _eventBus;

    public ModerationService(
        IApplicationDbContext db,
        ITwitchModerationApi moderation,
        ILogger<ModerationService> logger,
        IEventBus eventBus
    )
    {
        _db = db;
        _moderation = moderation;
        _logger = logger;
        _eventBus = eventBus;
    }

    public async Task<Result<ModerationActionResult>> TimeoutAsync(
        string broadcasterId,
        string targetUserId,
        int durationSeconds,
        string? reason = null,
        string? moderatorId = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<ModerationActionResult>(broadcasterId);

        Result<ModerationActionResult> result = await RecordActionAsync(
            tenantId,
            "timeout",
            targetUserId,
            reason,
            durationSeconds,
            moderatorId,
            cancellationToken
        );

        if (result.IsSuccess)
        {
            Result<TwitchBanResult> twitchResult = await _moderation.TimeoutUserAsync(
                tenantId,
                targetUserId,
                durationSeconds,
                reason,
                cancellationToken
            );
            if (twitchResult.IsFailure)
                _logger.LogWarning(
                    "Twitch API timeout failed for {UserId} in {Channel}: {Error}",
                    targetUserId,
                    tenantId,
                    twitchResult.ErrorMessage
                );
        }

        return result;
    }

    public async Task<Result<ModerationActionResult>> BanAsync(
        string broadcasterId,
        string targetUserId,
        string? reason = null,
        string? moderatorId = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<ModerationActionResult>(broadcasterId);

        Result<ModerationActionResult> result = await RecordActionAsync(
            tenantId,
            "ban",
            targetUserId,
            reason,
            null,
            moderatorId,
            cancellationToken
        );

        if (result.IsSuccess)
        {
            Result<TwitchBanResult> twitchResult = await _moderation.BanUserAsync(
                tenantId,
                targetUserId,
                reason,
                cancellationToken
            );
            if (twitchResult.IsFailure)
                _logger.LogWarning(
                    "Twitch API ban failed for {UserId} in {Channel}: {Error}",
                    targetUserId,
                    tenantId,
                    twitchResult.ErrorMessage
                );
        }

        return result;
    }

    public async Task<Result<ModerationActionResult>> UnbanAsync(
        string broadcasterId,
        string targetUserId,
        string? moderatorId = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<ModerationActionResult>(broadcasterId);

        Result<ModerationActionResult> result = await RecordActionAsync(
            tenantId,
            "unban",
            targetUserId,
            null,
            null,
            moderatorId,
            cancellationToken
        );

        if (result.IsSuccess)
        {
            Result twitchResult = await _moderation.UnbanUserAsync(
                tenantId,
                targetUserId,
                cancellationToken
            );
            if (twitchResult.IsFailure)
                _logger.LogWarning(
                    "Twitch API unban failed for {UserId} in {Channel}: {Error}",
                    targetUserId,
                    tenantId,
                    twitchResult.ErrorMessage
                );
        }

        return result;
    }

    public async Task<Result<ModerationRuleDetail>> CreateRuleAsync(
        string broadcasterId,
        CreateModerationRuleRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<ModerationRuleDetail>(broadcasterId);

        bool channelExists = await _db.Channels.AnyAsync(c => c.Id == tenantId, cancellationToken);
        if (!channelExists)
            return Errors.ChannelNotFound<ModerationRuleDetail>(broadcasterId);

        ModerationRuleData ruleData = new()
        {
            Name = request.Name,
            Type = request.Type,
            Action = request.Action,
            DurationSeconds = request.DurationSeconds,
            Reason = request.Reason,
            Settings = request.Settings ?? new Dictionary<string, object?>(),
            ExemptRoles = request.ExemptRoles ?? [],
            IsEnabled = true,
        };

        Record record = new()
        {
            BroadcasterId = tenantId,
            RecordType = RuleRecordType,
            Data = JsonSerializer.Serialize(ruleData),
            UserId = broadcasterId, // system record — use broadcaster as owner
        };

        _db.Records.Add(record);
        await _db.SaveChangesAsync(cancellationToken);
        await PublishConfigChangedAsync(tenantId, record.Id, "created", cancellationToken);

        return Result.Success(
            new ModerationRuleDetail(
                record.Id,
                ruleData.Name,
                ruleData.Type,
                ruleData.IsEnabled,
                ruleData.Action,
                ruleData.DurationSeconds,
                ruleData.Reason,
                ruleData.Settings,
                ruleData.ExemptRoles,
                record.CreatedAt,
                record.UpdatedAt
            )
        );
    }

    public async Task<Result> DeleteRuleAsync(
        string broadcasterId,
        int ruleId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Result.Failure($"Moderation rule '{ruleId}' was not found.", "NOT_FOUND");

        Record? record = await _db.Records.FirstOrDefaultAsync(
            r => r.Id == ruleId && r.BroadcasterId == tenantId && r.RecordType == RuleRecordType,
            cancellationToken
        );

        if (record is null)
            return Result.Failure($"Moderation rule '{ruleId}' was not found.", "NOT_FOUND");

        _db.Records.Remove(record);
        await _db.SaveChangesAsync(cancellationToken);
        await PublishConfigChangedAsync(tenantId, record.Id, "deleted", cancellationToken);

        return Result.Success();
    }

    public async Task<Result<ModerationRuleDetail>> UpdateRuleAsync(
        string broadcasterId,
        int ruleId,
        UpdateModerationRuleRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.NotFound<ModerationRuleDetail>("Moderation rule", ruleId.ToString());

        Record? record = await _db.Records.FirstOrDefaultAsync(
            r => r.Id == ruleId && r.BroadcasterId == tenantId && r.RecordType == RuleRecordType,
            cancellationToken
        );

        if (record is null)
            return Errors.NotFound<ModerationRuleDetail>("Moderation rule", ruleId.ToString());

        ModerationRuleData ruleData =
            JsonSerializer.Deserialize<ModerationRuleData>(record.Data) ?? new ModerationRuleData();

        if (request.Name is not null)
            ruleData.Name = request.Name;
        if (request.Action is not null)
            ruleData.Action = request.Action;
        if (request.DurationSeconds.HasValue)
            ruleData.DurationSeconds = request.DurationSeconds.Value;
        if (request.Reason is not null)
            ruleData.Reason = request.Reason;
        if (request.Settings is not null)
            ruleData.Settings = request.Settings;
        if (request.ExemptRoles is not null)
            ruleData.ExemptRoles = request.ExemptRoles;
        if (request.IsEnabled.HasValue)
            ruleData.IsEnabled = request.IsEnabled.Value;

        record.Data = JsonSerializer.Serialize(ruleData);
        // UpdatedAt stamped by AuditableEntityInterceptor on save.

        await _db.SaveChangesAsync(cancellationToken);
        await PublishConfigChangedAsync(tenantId, record.Id, "updated", cancellationToken);

        return Result.Success(
            new ModerationRuleDetail(
                record.Id,
                ruleData.Name,
                ruleData.Type,
                ruleData.IsEnabled,
                ruleData.Action,
                ruleData.DurationSeconds,
                ruleData.Reason,
                ruleData.Settings,
                ruleData.ExemptRoles,
                record.CreatedAt,
                record.UpdatedAt
            )
        );
    }

    public async Task<Result<PagedList<ModerationRuleListItem>>> ListRulesAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<PagedList<ModerationRuleListItem>>(broadcasterId);

        IQueryable<Record> query = _db.Records.Where(r =>
            r.BroadcasterId == tenantId && r.RecordType == RuleRecordType
        );

        int total = await query.CountAsync(cancellationToken);

        List<Record> records = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        List<ModerationRuleListItem> items = records
            .Select(r =>
            {
                ModerationRuleData data =
                    JsonSerializer.Deserialize<ModerationRuleData>(r.Data)
                    ?? new ModerationRuleData();
                return new ModerationRuleListItem(
                    r.Id,
                    data.Name,
                    data.Type,
                    data.IsEnabled,
                    data.Action,
                    data.DurationSeconds,
                    r.CreatedAt
                );
            })
            .ToList();

        return Result.Success(
            new PagedList<ModerationRuleListItem>(
                items,
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    public async Task<Result<PagedList<ModerationActionLog>>> GetActionsAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<PagedList<ModerationActionLog>>(broadcasterId);

        IQueryable<Record> query = _db.Records.Where(r =>
            r.BroadcasterId == tenantId && r.RecordType == ActionRecordType
        );

        int total = await query.CountAsync(cancellationToken);

        List<Record> records = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        // Record.UserId is either a Twitch user id or a broadcaster UUID — ResolveUsernamesAsync handles both.
        Dictionary<string, string> usernamesByTwitchId = await ResolveUsernamesAsync(
            records.Select(r => r.UserId),
            cancellationToken
        );

        List<ModerationActionLog> items = records
            .Select(r =>
            {
                ModerationActionData data =
                    JsonSerializer.Deserialize<ModerationActionData>(r.Data)
                    ?? new ModerationActionData();
                return new ModerationActionLog(
                    r.Id.ToString(),
                    data.Action,
                    r.UserId,
                    usernamesByTwitchId.GetValueOrDefault(r.UserId, r.UserId),
                    data.TargetUserId,
                    data.TargetUsername,
                    data.Reason,
                    data.DurationSeconds,
                    r.CreatedAt
                );
            })
            .ToList();

        return Result.Success(
            new PagedList<ModerationActionLog>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task<Result<ModerationActionResult>> RecordActionAsync(
        Guid tenantId,
        string action,
        string targetUserId,
        string? reason,
        int? durationSeconds,
        string? moderatorId,
        CancellationToken cancellationToken
    )
    {
        bool channelExists = await _db.Channels.AnyAsync(c => c.Id == tenantId, cancellationToken);
        if (!channelExists)
            return Errors.ChannelNotFound<ModerationActionResult>(tenantId.ToString());

        // targetUserId is the Twitch user id passed to Helix — resolve the username via TwitchUserId.
        User? targetUser = await _db.Users.FirstOrDefaultAsync(
            u => u.TwitchUserId == targetUserId,
            cancellationToken
        );

        ModerationActionData actionData = new()
        {
            Action = action,
            TargetUserId = targetUserId,
            TargetUsername = targetUser?.Username,
            Reason = reason,
            DurationSeconds = durationSeconds,
        };

        Record record = new()
        {
            BroadcasterId = tenantId,
            RecordType = ActionRecordType,
            Data = JsonSerializer.Serialize(actionData),
            UserId = moderatorId ?? tenantId.ToString(),
        };

        _db.Records.Add(record);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(new ModerationActionResult(true, $"{action} applied successfully."));
    }

    public async Task<Result<AutomodConfigDto>> GetAutomodConfigAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<AutomodConfigDto>(broadcasterId);

        List<Record> rules = await _db
            .Records.Where(r =>
                r.BroadcasterId == tenantId
                && r.RecordType == RuleRecordType
                && (
                    r.Data.Contains("\"link_filter\"")
                    || r.Data.Contains("\"caps_filter\"")
                    || r.Data.Contains("\"banned_phrases\"")
                    || r.Data.Contains("\"emote_spam\"")
                )
            )
            .ToListAsync(cancellationToken);

        AutomodLinkFilterDto linkFilter = new(false, []);
        AutomodCapsFilterDto capsFilter = new(false, 70);
        AutomodBannedPhrasesDto bannedPhrases = new(false, []);
        AutomodEmoteSpamDto emoteSpam = new(false, 10);

        foreach (Record rule in rules)
        {
            // A single corrupt/legacy row (unparseable JSON, or a setting stored in an unexpected shape) must
            // degrade to that filter's default — never bubble an exception up as an unhandled 500 for the whole
            // config read. This endpoint reads only persisted rows (no Helix), so the sole failure mode is bad
            // stored data; the safe accessors below fold every such case back to the default.
            ModerationRuleData? data = TryDeserializeRule(rule, tenantId);
            if (data is null)
                continue;

            Dictionary<string, object?> settings = data.Settings ?? new();

            switch (data.Type)
            {
                case "link_filter":
                    linkFilter = new(data.IsEnabled, ReadStringList(settings, "whitelist"));
                    break;

                case "caps_filter":
                    capsFilter = new(data.IsEnabled, ReadInt(settings, "threshold", 70));
                    break;

                case "banned_phrases":
                    bannedPhrases = new(data.IsEnabled, ReadStringList(settings, "phrases"));
                    break;

                case "emote_spam":
                    emoteSpam = new(data.IsEnabled, ReadInt(settings, "maxEmotes", 10));
                    break;
            }
        }

        return Result.Success(
            new AutomodConfigDto(linkFilter, capsFilter, bannedPhrases, emoteSpam)
        );
    }

    /// <summary>
    /// Deserializes one persisted automod rule, returning <c>null</c> (and logging a warning) instead of throwing
    /// when the stored data is not valid JSON. Keeps <see cref="GetAutomodConfigAsync"/> resilient to a single
    /// corrupt row rather than failing the whole read.
    /// </summary>
    private ModerationRuleData? TryDeserializeRule(Record rule, Guid tenantId)
    {
        try
        {
            return JsonSerializer.Deserialize<ModerationRuleData>(rule.Data)
                ?? new ModerationRuleData();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Skipping automod rule {RecordId} for channel {Channel}: stored data is not valid JSON.",
                rule.Id,
                tenantId
            );
            return null;
        }
    }

    /// <summary>
    /// Reads an <see cref="int"/> setting, falling back to <paramref name="fallback"/> when the key is missing or
    /// the stored value is not a JSON number that fits an <see cref="int"/> (e.g. a string, a decimal, or overflow).
    /// </summary>
    private static int ReadInt(Dictionary<string, object?> settings, string key, int fallback) =>
        settings.TryGetValue(key, out object? value)
        && value is JsonElement el
        && el.ValueKind == JsonValueKind.Number
        && el.TryGetInt32(out int parsed)
            ? parsed
            : fallback;

    /// <summary>
    /// Reads a list-of-strings setting, falling back to an empty list when the key is missing or the stored value
    /// is not a JSON array; non-string array elements are skipped.
    /// </summary>
    private static List<string> ReadStringList(Dictionary<string, object?> settings, string key) =>
        settings.TryGetValue(key, out object? value)
        && value is JsonElement el
        && el.ValueKind == JsonValueKind.Array
            ? el.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString() ?? "")
                .Where(s => s != "")
                .ToList()
            : [];

    public async Task<Result<AutomodConfigDto>> SaveAutomodConfigAsync(
        string broadcasterId,
        AutomodConfigDto config,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<AutomodConfigDto>(broadcasterId);

        bool channelExists = await _db.Channels.AnyAsync(c => c.Id == tenantId, cancellationToken);
        if (!channelExists)
            return Errors.ChannelNotFound<AutomodConfigDto>(broadcasterId);

        (string type, bool enabled, Dictionary<string, object?> settings)[] automodRules =
        [
            (
                "link_filter",
                config.LinkFilter.Enabled,
                new Dictionary<string, object?> { ["whitelist"] = config.LinkFilter.Whitelist }
            ),
            (
                "caps_filter",
                config.CapsFilter.Enabled,
                new Dictionary<string, object?> { ["threshold"] = config.CapsFilter.Threshold }
            ),
            (
                "banned_phrases",
                config.BannedPhrases.Enabled,
                new Dictionary<string, object?> { ["phrases"] = config.BannedPhrases.Phrases }
            ),
            (
                "emote_spam",
                config.EmoteSpam.Enabled,
                new Dictionary<string, object?> { ["maxEmotes"] = config.EmoteSpam.MaxEmotes }
            ),
        ];

        foreach ((string type, bool enabled, Dictionary<string, object?> settings) in automodRules)
        {
            string typeJson = $"\"{type}\"";
            Record? existing = await _db
                .Records.Where(r =>
                    r.BroadcasterId == tenantId
                    && r.RecordType == RuleRecordType
                    && r.Data.Contains(typeJson)
                )
                .FirstOrDefaultAsync(cancellationToken);

            ModerationRuleData ruleData = existing is not null
                ? JsonSerializer.Deserialize<ModerationRuleData>(existing.Data)
                    ?? new ModerationRuleData()
                : new ModerationRuleData
                {
                    Name = type,
                    Type = type,
                    Action = "delete",
                };

            ruleData.IsEnabled = enabled;
            ruleData.Settings = settings;

            if (existing is not null)
            {
                existing.Data = JsonSerializer.Serialize(ruleData);
                // UpdatedAt stamped by AuditableEntityInterceptor on save.
            }
            else
            {
                _db.Records.Add(
                    new()
                    {
                        BroadcasterId = tenantId,
                        RecordType = RuleRecordType,
                        Data = JsonSerializer.Serialize(ruleData),
                        UserId = broadcasterId,
                    }
                );
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await PublishConfigChangedAsync(
            tenantId,
            "automod",
            entityId: null,
            "updated",
            cancellationToken
        );

        return await GetAutomodConfigAsync(broadcasterId, cancellationToken);
    }

    public async Task<Result<List<BannedUserDto>>> GetBannedUsersAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<List<BannedUserDto>>(broadcasterId);

        List<Record> actions = await _db
            .Records.Where(r =>
                r.BroadcasterId == tenantId
                && r.RecordType == ActionRecordType
                && (r.Data.Contains("\"ban\"") || r.Data.Contains("\"unban\""))
            )
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        // The moderator's Twitch user id (Record.UserId) → display name via Users.TwitchUserId.
        Dictionary<string, string> moderatorNames = await ResolveUsernamesAsync(
            actions.Select(r => r.UserId),
            cancellationToken
        );

        // Build the latest action per target user
        Dictionary<
            string,
            (string action, Record record, ModerationActionData data)
        > latestByTarget = new(StringComparer.OrdinalIgnoreCase);

        foreach (Record r in actions)
        {
            ModerationActionData d =
                JsonSerializer.Deserialize<ModerationActionData>(r.Data)
                ?? new ModerationActionData();
            if (d.TargetUserId is null)
                continue;
            if (!latestByTarget.ContainsKey(d.TargetUserId))
                latestByTarget[d.TargetUserId] = (d.Action, r, d);
        }

        List<BannedUserDto> banned = latestByTarget
            .Values.Where(e => e.action == "ban")
            .Select(e => new BannedUserDto(
                e.data.TargetUserId!,
                e.data.TargetUsername ?? e.data.TargetUserId!,
                e.data.Reason,
                moderatorNames.GetValueOrDefault(e.record.UserId, e.record.UserId),
                e.record.CreatedAt
            ))
            .OrderByDescending(b => b.BannedAt)
            .ToList();

        return Result.Success(banned);
    }

    /// <summary>
    /// Resolves a set of actor ids to display names. Dashboard-originated actions store the broadcaster's
    /// internal <c>Guid</c> as the actor; EventSub-originated actions store the Twitch user id. This method
    /// handles both: UUID-shaped strings are looked up via <c>User.Id</c>; everything else via
    /// <c>User.TwitchUserId</c>. Missing ids fall back to the raw id string.
    /// </summary>
    private async Task<Dictionary<string, string>> ResolveUsernamesAsync(
        IEnumerable<string> actorIds,
        CancellationToken cancellationToken
    )
    {
        List<string> distinct = actorIds
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinct.Count == 0)
            return new(StringComparer.OrdinalIgnoreCase);

        // Split into UUID-shaped ids (stored by dashboard actions) and plain Twitch user ids.
        List<Guid> internalIds = distinct
            .Where(id => Guid.TryParse(id, out _))
            .Select(Guid.Parse)
            .ToList();
        List<string> twitchIds = distinct.Where(id => !Guid.TryParse(id, out _)).ToList();

        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);

        if (twitchIds.Count > 0)
        {
            Dictionary<string, string> byTwitch = await _db
                .Users.Where(u => twitchIds.Contains(u.TwitchUserId!))
                .ToDictionaryAsync(u => u.TwitchUserId!, u => u.DisplayName, cancellationToken);
            foreach (KeyValuePair<string, string> kv in byTwitch)
                result[kv.Key] = kv.Value;
        }

        if (internalIds.Count > 0)
        {
            Dictionary<string, string> byInternal = await _db
                .Users.Where(u => internalIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id.ToString(), u => u.DisplayName, cancellationToken);
            foreach (KeyValuePair<string, string> kv in byInternal)
                result[kv.Key] = kv.Value;
        }

        return result;
    }

    /// <summary>E5 dashboard live-sync: fired after every successful write so other open dashboards refetch.</summary>
    private Task PublishConfigChangedAsync(
        Guid broadcasterId,
        int ruleId,
        string action,
        CancellationToken ct
    ) =>
        PublishConfigChangedAsync(broadcasterId, "moderation-rules", ruleId.ToString(), action, ct);

    /// <summary>E5 dashboard live-sync: fired after every successful write so other open dashboards refetch.</summary>
    private Task PublishConfigChangedAsync(
        Guid broadcasterId,
        string domain,
        string? entityId,
        string action,
        CancellationToken ct
    ) =>
        _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterId,
                Domain = domain,
                EntityId = entityId,
                Action = action,
            },
            ct
        );

    // ─── Private data shapes stored in Record.Data ───────────────────────────

    private sealed class ModerationRuleData
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public int? DurationSeconds { get; set; }
        public string? Reason { get; set; }
        public Dictionary<string, object?> Settings { get; set; } = new();
        public List<string> ExemptRoles { get; set; } = [];
        public bool IsEnabled { get; set; } = true;
    }

    private sealed class ModerationActionData
    {
        public string Action { get; set; } = string.Empty;
        public string? TargetUserId { get; set; }
        public string? TargetUsername { get; set; }
        public string? Reason { get; set; }
        public int? DurationSeconds { get; set; }
    }
}
