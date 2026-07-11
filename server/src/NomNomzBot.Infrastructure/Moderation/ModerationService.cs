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

        Result guard = await EnsureTargetIsModeratableAsync(
            tenantId,
            targetUserId,
            "timeout",
            cancellationToken
        );
        if (guard.IsFailure)
            return Result.Failure<ModerationActionResult>(guard.ErrorMessage!, guard.ErrorCode!);

        // Enforce on Twitch FIRST, and only record the action once Twitch actually applied it. The dashboard's
        // action log and banned-viewers list are built from these local records, so recording before (or
        // regardless of) the Helix call is what let a timeout Twitch rejected show up as if it had happened.
        Result<TwitchBanResult> twitchResult = await _moderation.TimeoutUserAsync(
            tenantId,
            targetUserId,
            durationSeconds,
            reason,
            cancellationToken
        );
        if (twitchResult.IsFailure)
            return Result.Failure<ModerationActionResult>(
                twitchResult.ErrorMessage ?? "Twitch rejected the timeout.",
                twitchResult.ErrorCode ?? "TWITCH_ERROR"
            );

        return await RecordActionAsync(
            tenantId,
            "timeout",
            targetUserId,
            reason,
            durationSeconds,
            moderatorId,
            cancellationToken
        );
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

        Result guard = await EnsureTargetIsModeratableAsync(
            tenantId,
            targetUserId,
            "ban",
            cancellationToken
        );
        if (guard.IsFailure)
            return Result.Failure<ModerationActionResult>(guard.ErrorMessage!, guard.ErrorCode!);

        // Enforce on Twitch FIRST, and only record the ban once Twitch actually applied it. The dashboard's
        // banned-viewers list is built from these local records, so recording before (or regardless of) the
        // Helix result is exactly what let a ban Twitch rejected — e.g. banning the broadcaster — show as real.
        Result<TwitchBanResult> twitchResult = await _moderation.BanUserAsync(
            tenantId,
            targetUserId,
            reason,
            cancellationToken
        );
        if (twitchResult.IsFailure)
            return Result.Failure<ModerationActionResult>(
                twitchResult.ErrorMessage ?? "Twitch rejected the ban.",
                twitchResult.ErrorCode ?? "TWITCH_ERROR"
            );

        return await RecordActionAsync(
            tenantId,
            "ban",
            targetUserId,
            reason,
            null,
            moderatorId,
            cancellationToken
        );
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

    // Twitch structurally protects the channel's own broadcaster: a ban/timeout against them 400s, so a local
    // record would be a fake the dashboard's banned list then displays. The broadcaster's Twitch user id IS the
    // tenant's TwitchChannelId, and targetUserId is the raw Twitch id passed to Helix, so this rejects any
    // attempt (including the owner banning themselves) before it can be recorded.
    private async Task<Result> EnsureTargetIsModeratableAsync(
        Guid tenantId,
        string targetUserId,
        string action,
        CancellationToken cancellationToken
    )
    {
        string? broadcasterTwitchId = await _db
            .Channels.Where(c => c.Id == tenantId)
            .Select(c => c.TwitchChannelId)
            .FirstOrDefaultAsync(cancellationToken);

        if (
            !string.IsNullOrEmpty(broadcasterTwitchId)
            && string.Equals(broadcasterTwitchId, targetUserId, StringComparison.OrdinalIgnoreCase)
        )
            return Result.Failure(
                $"The broadcaster can't be {(action == "ban" ? "banned" : "timed out")} — Twitch protects the "
                    + "channel owner from being moderated on their own channel.",
                "CANNOT_MODERATE_BROADCASTER"
            );

        return Result.Success();
    }

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

        // Read the LIVE banned list from Twitch, not just the bans the bot itself recorded — a viewer banned
        // through Twitch's own UI or by another moderator must appear here too, and Twitch's payload carries the
        // real reason, moderator and timestamp. A failure (missing scope / no broadcaster token for a channel we
        // don't manage) surfaces as an honest error, never a silently-empty "no bans" list.
        List<BannedUserDto> banned = [];
        string? cursor = null;
        int pageGuard = 0;
        do
        {
            Result<TwitchPage<TwitchBannedUser>> page = await _moderation.GetBannedUsersAsync(
                tenantId,
                new TwitchPageRequest(After: cursor),
                cancellationToken
            );
            if (page.IsFailure)
                return Result.Failure<List<BannedUserDto>>(
                    page.ErrorMessage ?? "Twitch rejected the banned-users read.",
                    page.ErrorCode ?? "TWITCH_ERROR"
                );

            foreach (TwitchBannedUser user in page.Value.Items)
            {
                // Get Banned Users returns permanent bans AND active timeouts (timeouts carry ExpiresAt). The
                // dashboard's banned-users list is permanent bans only; transient timeouts live in the action log.
                if (user.ExpiresAt is not null)
                    continue;

                banned.Add(
                    new BannedUserDto(
                        user.UserId,
                        string.IsNullOrEmpty(user.UserName) ? user.UserLogin : user.UserName,
                        string.IsNullOrEmpty(user.Reason) ? null : user.Reason,
                        string.IsNullOrEmpty(user.ModeratorName)
                            ? user.ModeratorLogin
                            : user.ModeratorName,
                        user.CreatedAt.UtcDateTime
                    )
                );
            }

            cursor = page.Value.NextCursor;
        } while (!string.IsNullOrEmpty(cursor) && ++pageGuard < 100);

        return Result.Success(banned.OrderByDescending(b => b.BannedAt).ToList());
    }

    public async Task<Result<List<string>>> GetBlockedTermsAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<List<string>>(broadcasterId);

        Result<List<TwitchBlockedTerm>> terms = await ReadAllBlockedTermsAsync(
            tenantId,
            cancellationToken
        );
        if (terms.IsFailure)
            return Result.Failure<List<string>>(terms.ErrorMessage!, terms.ErrorCode!);

        return Result.Success(terms.Value.Select(term => term.Text).ToList());
    }

    public async Task<Result<List<string>>> AddBlockedTermAsync(
        string broadcasterId,
        string text,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<List<string>>(broadcasterId);
        if (string.IsNullOrWhiteSpace(text))
            return Result.Failure<List<string>>(
                "A blocked term can't be empty.",
                "VALIDATION_FAILED"
            );

        // Add the term ON TWITCH — the block list is Twitch's, so a dashboard-added term that never reached
        // Helix would be a phantom control. Twitch owns validation (length, wildcard rules) and reports it back.
        Result<TwitchBlockedTerm> added = await _moderation.AddBlockedTermAsync(
            tenantId,
            text.Trim(),
            cancellationToken
        );
        if (added.IsFailure)
            return Result.Failure<List<string>>(added.ErrorMessage!, added.ErrorCode!);

        await PublishConfigChangedAsync(
            tenantId,
            "blocked-terms",
            added.Value.Id,
            "created",
            cancellationToken
        );
        return await GetBlockedTermsAsync(broadcasterId, cancellationToken);
    }

    public async Task<Result<List<string>>> RemoveBlockedTermAsync(
        string broadcasterId,
        string text,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<List<string>>(broadcasterId);

        // Helix removes a blocked term by its id, but the dashboard only knows the text the moderator sees.
        // Resolve the current list, then delete every entry whose text matches (Twitch permits duplicates).
        Result<List<TwitchBlockedTerm>> current = await ReadAllBlockedTermsAsync(
            tenantId,
            cancellationToken
        );
        if (current.IsFailure)
            return Result.Failure<List<string>>(current.ErrorMessage!, current.ErrorCode!);

        List<TwitchBlockedTerm> matches = current
            .Value.Where(term => string.Equals(term.Text, text, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Already absent → idempotent success returning the unchanged list (no needless Helix round-trip).
        if (matches.Count == 0)
            return Result.Success(current.Value.Select(term => term.Text).ToList());

        foreach (TwitchBlockedTerm term in matches)
        {
            Result removal = await _moderation.RemoveBlockedTermAsync(
                tenantId,
                term.Id,
                cancellationToken
            );
            if (removal.IsFailure)
                return Result.Failure<List<string>>(removal.ErrorMessage!, removal.ErrorCode!);
        }

        await PublishConfigChangedAsync(
            tenantId,
            "blocked-terms",
            text,
            "deleted",
            cancellationToken
        );
        return await GetBlockedTermsAsync(broadcasterId, cancellationToken);
    }

    public async Task<Result<bool>> GetShieldModeAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<bool>(broadcasterId);

        Result<TwitchShieldModeStatus> status = await _moderation.GetShieldModeStatusAsync(
            tenantId,
            cancellationToken
        );
        if (status.IsFailure)
            return Result.Failure<bool>(status.ErrorMessage!, status.ErrorCode!);

        return Result.Success(status.Value.IsActive);
    }

    public async Task<Result<bool>> SetShieldModeAsync(
        string broadcasterId,
        bool isActive,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<bool>(broadcasterId);

        // Toggle Shield Mode ON TWITCH — storing the flag locally without calling Helix (the previous behavior)
        // was a cosmetic switch that never armed the real protection. Twitch returns the applied state.
        Result<TwitchShieldModeStatus> updated = await _moderation.UpdateShieldModeStatusAsync(
            tenantId,
            isActive,
            cancellationToken
        );
        if (updated.IsFailure)
            return Result.Failure<bool>(updated.ErrorMessage!, updated.ErrorCode!);

        await PublishConfigChangedAsync(
            tenantId,
            "moderation-rules",
            "shield-mode",
            "updated",
            cancellationToken
        );
        return Result.Success(updated.Value.IsActive);
    }

    // The unban-request statuses Twitch's Get Unban Requests accepts; anything else 400s, so reject it locally
    // with a clear message rather than forwarding a doomed request.
    private static readonly HashSet<string> UnbanRequestStatuses = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "pending",
        "approved",
        "denied",
        "acknowledged",
        "canceled",
    };

    public async Task<Result<List<UnbanRequestDto>>> GetUnbanRequestsAsync(
        string broadcasterId,
        string status,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<List<UnbanRequestDto>>(broadcasterId);
        if (!UnbanRequestStatuses.Contains(status))
            return Result.Failure<List<UnbanRequestDto>>(
                $"Unknown unban-request status '{status}'. Valid: {string.Join(", ", UnbanRequestStatuses)}.",
                "VALIDATION_FAILED"
            );

        List<UnbanRequestDto> requests = [];
        string? cursor = null;
        int pageGuard = 0;
        do
        {
            Result<TwitchPage<TwitchUnbanRequest>> page = await _moderation.GetUnbanRequestsAsync(
                tenantId,
                status,
                new TwitchPageRequest(After: cursor),
                cancellationToken
            );
            if (page.IsFailure)
                return Result.Failure<List<UnbanRequestDto>>(
                    page.ErrorMessage ?? "Twitch rejected the unban-requests read.",
                    page.ErrorCode ?? "TWITCH_ERROR"
                );

            requests.AddRange(page.Value.Items.Select(ToDto));
            cursor = page.Value.NextCursor;
        } while (!string.IsNullOrEmpty(cursor) && ++pageGuard < 100);

        return Result.Success(requests);
    }

    public async Task<Result<UnbanRequestDto>> ResolveUnbanRequestAsync(
        string broadcasterId,
        string unbanRequestId,
        bool approve,
        string? note,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<UnbanRequestDto>(broadcasterId);

        // Twitch's Resolve Unban Request takes the terminal status verbatim — approving lifts the ban, denying
        // leaves it. The write actually happens on Twitch; we return the resolved request Twitch echoes back.
        string resolvedStatus = approve ? "approved" : "denied";
        Result<TwitchUnbanRequest> resolved = await _moderation.ResolveUnbanRequestAsync(
            tenantId,
            unbanRequestId,
            resolvedStatus,
            note,
            cancellationToken
        );
        if (resolved.IsFailure)
            return Result.Failure<UnbanRequestDto>(resolved.ErrorMessage!, resolved.ErrorCode!);

        await PublishConfigChangedAsync(
            tenantId,
            "unban-requests",
            unbanRequestId,
            approve ? "approved" : "denied",
            cancellationToken
        );
        return Result.Success(ToDto(resolved.Value));
    }

    private static UnbanRequestDto ToDto(TwitchUnbanRequest request) =>
        new(
            request.Id,
            request.UserId,
            request.UserLogin,
            request.UserName,
            request.Text,
            request.Status,
            request.CreatedAt.UtcDateTime,
            request.ResolvedAt?.UtcDateTime,
            // Twitch omits the resolving moderator + note until the request is resolved.
            string.IsNullOrEmpty(request.ModeratorName)
                ? null
                : request.ModeratorName,
            string.IsNullOrEmpty(request.ResolutionText) ? null : request.ResolutionText
        );

    public async Task<Result<ModerationActionResult>> WarnUserAsync(
        string broadcasterId,
        string targetUserId,
        string reason,
        string? moderatorId = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<ModerationActionResult>(broadcasterId);
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure<ModerationActionResult>(
                "A warning needs a reason.",
                "VALIDATION_FAILED"
            );

        // Warn on Twitch FIRST; only record it to the mod log once Twitch actually issued the warning — the same
        // enforce-then-record discipline as ban/timeout, so a warning Twitch rejected never shows up as history.
        Result<TwitchWarningResult> warned = await _moderation.WarnChatUserAsync(
            tenantId,
            targetUserId,
            reason,
            cancellationToken
        );
        if (warned.IsFailure)
            return Result.Failure<ModerationActionResult>(
                warned.ErrorMessage ?? "Twitch rejected the warning.",
                warned.ErrorCode ?? "TWITCH_ERROR"
            );

        return await RecordActionAsync(
            tenantId,
            "warn",
            targetUserId,
            reason,
            null,
            moderatorId,
            cancellationToken
        );
    }

    // The suspicious-user statuses Twitch's Update Suspicious User accepts; a bad value 400s, so reject locally.
    private static readonly HashSet<string> SuspiciousStatuses = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "active_monitoring",
        "restricted",
    };

    public async Task<Result<SuspiciousStatusDto>> SetSuspiciousStatusAsync(
        string broadcasterId,
        string targetUserId,
        string status,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<SuspiciousStatusDto>(broadcasterId);
        if (!SuspiciousStatuses.Contains(status))
            return Result.Failure<SuspiciousStatusDto>(
                $"Unknown suspicious status '{status}'. Valid: active_monitoring, restricted.",
                "VALIDATION_FAILED"
            );

        Result<TwitchSuspiciousUserStatus> applied = await _moderation.AddSuspiciousStatusAsync(
            tenantId,
            targetUserId,
            status.ToLowerInvariant(),
            cancellationToken
        );
        if (applied.IsFailure)
            return Result.Failure<SuspiciousStatusDto>(applied.ErrorMessage!, applied.ErrorCode!);

        return Result.Success(ToDto(applied.Value));
    }

    public async Task<Result<SuspiciousStatusDto>> ClearSuspiciousStatusAsync(
        string broadcasterId,
        string targetUserId,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid tenantId))
            return Errors.ChannelNotFound<SuspiciousStatusDto>(broadcasterId);

        Result<TwitchSuspiciousUserStatus> cleared = await _moderation.RemoveSuspiciousStatusAsync(
            tenantId,
            targetUserId,
            cancellationToken
        );
        if (cleared.IsFailure)
            return Result.Failure<SuspiciousStatusDto>(cleared.ErrorMessage!, cleared.ErrorCode!);

        return Result.Success(ToDto(cleared.Value));
    }

    private static SuspiciousStatusDto ToDto(TwitchSuspiciousUserStatus status) =>
        new(status.UserId, status.Status, status.Types, status.UpdatedAt.UtcDateTime);

    /// <summary>
    /// Reads every page of the channel's Twitch blocked-term list, short-circuiting to an honest failure on the
    /// first Helix error rather than returning a partial list. Bounded by a page guard against a pathological
    /// cursor loop.
    /// </summary>
    private async Task<Result<List<TwitchBlockedTerm>>> ReadAllBlockedTermsAsync(
        Guid tenantId,
        CancellationToken cancellationToken
    )
    {
        List<TwitchBlockedTerm> all = [];
        string? cursor = null;
        int pageGuard = 0;
        do
        {
            Result<TwitchPage<TwitchBlockedTerm>> page = await _moderation.GetBlockedTermsAsync(
                tenantId,
                new TwitchPageRequest(After: cursor),
                cancellationToken
            );
            if (page.IsFailure)
                return Result.Failure<List<TwitchBlockedTerm>>(
                    page.ErrorMessage ?? "Twitch rejected the blocked-terms read.",
                    page.ErrorCode ?? "TWITCH_ERROR"
                );

            all.AddRange(page.Value.Items);
            cursor = page.Value.NextCursor;
        } while (!string.IsNullOrEmpty(cursor) && ++pageGuard < 100);

        return Result.Success(all);
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
