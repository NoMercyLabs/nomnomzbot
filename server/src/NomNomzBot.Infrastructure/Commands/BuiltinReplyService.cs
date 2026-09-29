// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands.Builtins;

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>
/// See <see cref="IBuiltinReplyService"/>. The catalogue is <see cref="ToneTemplateCatalog"/> (code-defined);
/// the channel's own texts live per reply group in <c>ChannelBuiltinCommand.OverridesJson</c>
/// (<see cref="BuiltinOverridesJson"/>). A write reloads the channel registry, which is what the response
/// composer reads at runtime — so the next chat reply uses the new text.
/// </summary>
public sealed class BuiltinReplyService : IBuiltinReplyService
{
    /// <summary>The platform column's limit — a channel text can never be longer than the platform's.</summary>
    private const int MaxTemplateLength = 500;

    private readonly IApplicationDbContext _db;
    private readonly IBuiltinCommandCatalog _catalog;
    private readonly IPlatformBuiltinReplyDefaults _platformReplies;
    private readonly ITemplateHelperValidator _validator;
    private readonly IChannelRegistry _registry;
    private readonly IEventBus _eventBus;

    public BuiltinReplyService(
        IApplicationDbContext db,
        IBuiltinCommandCatalog catalog,
        IPlatformBuiltinReplyDefaults platformReplies,
        ITemplateHelperValidator validator,
        IChannelRegistry registry,
        IEventBus eventBus
    )
    {
        _db = db;
        _catalog = catalog;
        _platformReplies = platformReplies;
        _validator = validator;
        _registry = registry;
        _eventBus = eventBus;
    }

    public async Task<Result<IReadOnlyList<BuiltinReplyGroupDto>>> ListAsync(
        string broadcasterId,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<IReadOnlyList<BuiltinReplyGroupDto>>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        string personality = await PersonalityAsync(broadcaster, ct);
        Dictionary<string, IReadOnlyDictionary<string, string>> channelTexts =
            await ChannelTextsAsync(broadcaster, ct);

        List<BuiltinReplyGroupDto> groups = [];
        foreach (
            IGrouping<string, (string BuiltinKey, string Slot)> group in ToneTemplateCatalog
                .AllSlots()
                .GroupBy(s => s.BuiltinKey, StringComparer.Ordinal)
        )
        {
            List<BuiltinReplyDto> replies = [];
            foreach ((string _, string slot) in group)
                replies.Add(await ResolveAsync(group.Key, slot, personality, channelTexts, ct));

            groups.Add(new(group.Key, CommandKeysOf(group.Key), replies));
        }

        return Result.Success<IReadOnlyList<BuiltinReplyGroupDto>>(groups);
    }

    public async Task<Result<BuiltinReplyDto>> SetAsync(
        string broadcasterId,
        string builtinKey,
        string slot,
        string? template,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(template))
            return await ResetAsync(broadcasterId, builtinKey, slot, ct);

        Result<Guid> target = ValidateTarget(broadcasterId, builtinKey, slot);
        if (target.IsFailure)
            return Result.Failure<BuiltinReplyDto>(target.ErrorMessage!, target.ErrorCode!);

        string trimmed = template.Trim();
        if (trimmed.Length > MaxTemplateLength)
            return Result.Failure<BuiltinReplyDto>(
                $"A reply can be at most {MaxTemplateLength} characters (this one is {trimmed.Length}).",
                "VALIDATION_FAILED"
            );

        Result valid = _validator.Validate(
            trimmed,
            TemplateHelperContext.Command,
            [.. ToneTemplateCatalog.Variables(builtinKey, slot)]
        );
        if (valid.IsFailure)
            return Result.Failure<BuiltinReplyDto>(valid.ErrorMessage!, valid.ErrorCode!);

        return await WriteAsync(target.Value, builtinKey, slot, trimmed, ct);
    }

    public async Task<Result<BuiltinReplyDto>> ResetAsync(
        string broadcasterId,
        string builtinKey,
        string slot,
        CancellationToken ct = default
    )
    {
        Result<Guid> target = ValidateTarget(broadcasterId, builtinKey, slot);
        if (target.IsFailure)
            return Result.Failure<BuiltinReplyDto>(target.ErrorMessage!, target.ErrorCode!);

        return await WriteAsync(target.Value, builtinKey, slot, null, ct);
    }

    private Result<Guid> ValidateTarget(string broadcasterId, string builtinKey, string slot)
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<Guid>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        if (!ToneTemplateCatalog.Contains(builtinKey, slot))
            return Result.Failure<Guid>(
                $"Unknown built-in reply '{builtinKey}/{slot}'.",
                "NOT_FOUND"
            );

        if (IsLocked(builtinKey, slot))
            return Result.Failure<Guid>(
                $"'{builtinKey}/{slot}' is a data-rights reply — its wording is fixed and cannot be changed.",
                "VALIDATION_FAILED"
            );

        return Result.Success(broadcaster);
    }

    /// <summary>
    /// Writes (or, for a null template, removes) one slot's text on the reply group's row. Every other row that
    /// speaks with the same group (e.g. a legacy <c>unlurk</c> row) is folded into the group's row first, so the
    /// slot has exactly one source and a reset really restores the default.
    /// </summary>
    private async Task<Result<BuiltinReplyDto>> WriteAsync(
        Guid broadcaster,
        string replyGroup,
        string slot,
        string? template,
        CancellationToken ct
    )
    {
        List<ChannelBuiltinCommand> rows = await _db
            .ChannelBuiltinCommands.Where(c => c.BroadcasterId == broadcaster)
            .ToListAsync(ct);

        ChannelBuiltinCommand? groupRow = rows.FirstOrDefault(r =>
            string.Equals(Normalize(r.BuiltinKey), replyGroup, StringComparison.Ordinal)
        );
        Dictionary<string, string> responses = new(
            BuiltinOverridesJson.EffectiveResponses(replyGroup, groupRow?.OverridesJson),
            StringComparer.OrdinalIgnoreCase
        );

        foreach (
            ChannelBuiltinCommand sibling in rows.Where(r =>
                r != groupRow
                && BuiltinResponseSlots.ReplyGroupFor(r.BuiltinKey) == replyGroup
                && Normalize(r.BuiltinKey) != replyGroup
            )
        )
        {
            foreach (
                KeyValuePair<string, string> reply in BuiltinOverridesJson.EffectiveResponses(
                    sibling.BuiltinKey,
                    sibling.OverridesJson
                )
            )
                responses.TryAdd(reply.Key, reply.Value);

            sibling.OverridesJson = BuiltinOverridesJson.Serialize(
                new Dictionary<string, string>(),
                BuiltinOverridesJson.SpeakWithTts(sibling.OverridesJson)
            );
        }

        if (template is null)
            responses.Remove(slot);
        else
            responses[slot] = template;

        string? overridesJson = BuiltinOverridesJson.Serialize(
            responses,
            BuiltinOverridesJson.SpeakWithTts(groupRow?.OverridesJson)
        );

        if (groupRow is null)
        {
            if (overridesJson is not null)
                _db.ChannelBuiltinCommands.Add(
                    new()
                    {
                        BroadcasterId = broadcaster,
                        BuiltinKey = replyGroup,
                        IsEnabled = true,
                        OverridesJson = overridesJson,
                    }
                );
        }
        else
        {
            groupRow.OverridesJson = overridesJson;
        }

        await _db.SaveChangesAsync(ct);
        await _registry.InvalidateBuiltinsAsync(broadcaster, ct);
        await _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcaster,
                Domain = "builtins",
                EntityId = $"{replyGroup}/{slot}",
                Action = template is null ? "reply_reset" : "reply_set",
            },
            ct
        );

        string personality = await PersonalityAsync(broadcaster, ct);
        Dictionary<string, IReadOnlyDictionary<string, string>> channelTexts = new()
        {
            [replyGroup] = responses,
        };
        return Result.Success(await ResolveAsync(replyGroup, slot, personality, channelTexts, ct));
    }

    private async Task<BuiltinReplyDto> ResolveAsync(
        string replyGroup,
        string slot,
        string personality,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> channelTexts,
        CancellationToken ct
    )
    {
        string? platform = await _platformReplies.GetAsync(replyGroup, slot, ct);
        IReadOnlyList<string> toneLines = platform is null
            ? ToneTemplateCatalog.Get(personality, replyGroup, slot)
            : [];
        string defaultTemplate = platform ?? toneLines.FirstOrDefault() ?? string.Empty;

        string? own =
            channelTexts.TryGetValue(replyGroup, out IReadOnlyDictionary<string, string>? texts)
            && texts.TryGetValue(slot, out string? text)
                ? text
                : null;

        string source =
            own is not null ? BuiltinReplySource.Channel
            : platform is not null ? BuiltinReplySource.Platform
            : BuiltinReplySource.Tone;

        return new BuiltinReplyDto(
            replyGroup,
            slot,
            BuiltinReplyLabels.Label(replyGroup, slot),
            BuiltinReplyLabels.Description(replyGroup, slot),
            own ?? defaultTemplate,
            source,
            defaultTemplate,
            toneLines,
            [
                .. ToneTemplateCatalog
                    .Variables(replyGroup, slot)
                    .Select(name => new BuiltinReplyVariableDto(
                        name,
                        BuiltinReplyLabels.Variable(name),
                        ToneTemplateCatalog.SampleValue(name)
                    )),
            ],
            own is not null,
            IsLocked(replyGroup, slot)
        );
    }

    /// <summary>
    /// The channel's own texts per reply group, read straight from the rows (not the registry cache, which only
    /// holds channels with chat activity). A group's own row wins over a sibling row (e.g. a legacy
    /// <c>unlurk</c> row) for the same slot.
    /// </summary>
    private async Task<Dictionary<string, IReadOnlyDictionary<string, string>>> ChannelTextsAsync(
        Guid broadcaster,
        CancellationToken ct
    )
    {
        List<ChannelBuiltinCommand> rows = await _db
            .ChannelBuiltinCommands.AsNoTracking()
            .Where(c => c.BroadcasterId == broadcaster && c.OverridesJson != null)
            .ToListAsync(ct);

        Dictionary<string, Dictionary<string, string>> byGroup = new(StringComparer.Ordinal);
        foreach (
            ChannelBuiltinCommand row in rows.OrderBy(r =>
                Normalize(r.BuiltinKey) == BuiltinResponseSlots.ReplyGroupFor(r.BuiltinKey)
            )
        )
        {
            string group = BuiltinResponseSlots.ReplyGroupFor(row.BuiltinKey);
            if (!byGroup.TryGetValue(group, out Dictionary<string, string>? texts))
                byGroup[group] = texts = new(StringComparer.OrdinalIgnoreCase);

            foreach (
                KeyValuePair<string, string> reply in BuiltinOverridesJson.EffectiveResponses(
                    row.BuiltinKey,
                    row.OverridesJson
                )
            )
                texts[reply.Key] = reply.Value;
        }

        return byGroup.ToDictionary(
            g => g.Key,
            g => (IReadOnlyDictionary<string, string>)g.Value,
            StringComparer.Ordinal
        );
    }

    private async Task<string> PersonalityAsync(Guid broadcaster, CancellationToken ct)
    {
        string? stored = await _db
            .Channels.AsNoTracking()
            .Where(c => c.Id == broadcaster)
            .Select(c => c.Personality)
            .FirstOrDefaultAsync(ct);
        return PersonalityTone.Normalize(stored);
    }

    /// <summary>The chat triggers that speak with this reply group (empty for the bot's own groups).</summary>
    private IReadOnlyList<string> CommandKeysOf(string replyGroup) =>
        [
            .. _catalog
                .GetAll()
                .Select(c => c.BuiltinKey)
                .Where(key => BuiltinResponseSlots.ReplyGroupFor(key) == replyGroup)
                .Order(StringComparer.Ordinal),
        ];

    /// <summary>
    /// A reserved data-rights built-in's replies are fixed (gdpr-crypto.md §9) — except the streamer-stylable
    /// "clean slate" sentence, which §9 part 1 makes customizable (its mandatory clause is appended in code).
    /// </summary>
    private bool IsLocked(string replyGroup, string slot)
    {
        if (
            replyGroup == BuiltinResponseSlots.Forgetme.Key
            && slot == BuiltinResponseSlots.Forgetme.Done
        )
            return false;

        List<IBuiltinCommand> owners =
        [
            .. _catalog
                .GetAll()
                .Where(c => BuiltinResponseSlots.ReplyGroupFor(c.BuiltinKey) == replyGroup),
        ];
        return owners.Count > 0 && owners.All(c => c.IsReserved);
    }

    private static string Normalize(string builtinKey) =>
        builtinKey.TrimStart('!').ToLowerInvariant();
}
