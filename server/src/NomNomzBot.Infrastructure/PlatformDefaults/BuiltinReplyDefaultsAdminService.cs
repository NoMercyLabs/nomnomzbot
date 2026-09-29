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
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.PlatformDefaults.Dtos;
using NomNomzBot.Application.PlatformDefaults.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Infrastructure.Commands.Builtins;

namespace NomNomzBot.Infrastructure.PlatformDefaults;

/// <summary>
/// See <see cref="IBuiltinReplyDefaultsAdminService"/>. Writes <see cref="PlatformBuiltinReplyDefault"/> rows
/// (a missing row = the shipped wording) and drops the runtime reader's cache after every save, so the
/// composer's next render uses the new text on every API instance.
/// </summary>
public sealed class BuiltinReplyDefaultsAdminService(
    IApplicationDbContext db,
    IPlatformBuiltinReplyDefaults runtimeReplies,
    TimeProvider clock
) : IBuiltinReplyDefaultsAdminService
{
    private const string AuditFamily = "builtin_reply";
    private const int MaxTemplateLength = 500;

    public async Task<Result<IReadOnlyList<BuiltinReplyDefaultDto>>> ListAsync(
        CancellationToken ct = default
    )
    {
        Dictionary<(string, string), string> platform = await db
            .PlatformBuiltinReplyDefaults.AsNoTracking()
            .ToDictionaryAsync(d => (d.BuiltinKey, d.Slot), d => d.Template, ct);
        Dictionary<string, List<Guid>> ownReplies = await ChannelsWithOwnReplyBySlotAsync(ct);

        List<BuiltinReplyDefaultDto> rows =
        [
            .. ToneTemplateCatalog
                .AllSlots()
                .Select(s =>
                    ToDto(
                        s.BuiltinKey,
                        s.Slot,
                        platform.GetValueOrDefault((s.BuiltinKey, s.Slot)),
                        ownReplies
                    )
                ),
        ];
        return Result.Success<IReadOnlyList<BuiltinReplyDefaultDto>>(rows);
    }

    public async Task<Result<PlatformDefaultBlastRadiusDto>> PreviewAsync(
        string builtinKey,
        string slot,
        BuiltinReplyDefaultChange change,
        CancellationToken ct = default
    )
    {
        Result validation = Validate(builtinKey, slot, change.Template);
        if (validation.IsFailure)
            return validation.WithValue<PlatformDefaultBlastRadiusDto>(null!);

        PlatformBuiltinReplyDefault? current = await FindAsync(builtinKey, slot, ct);
        return Result.Success(await CountAsync(builtinKey, slot, current, change.Template, ct));
    }

    public async Task<Result<BuiltinReplyDefaultDto>> SetAsync(
        string builtinKey,
        string slot,
        SetBuiltinReplyDefaultRequest request,
        Guid actorUserId,
        CancellationToken ct = default
    )
    {
        string? template = Normalize(request.Template);
        Result validation = Validate(builtinKey, slot, template);
        if (validation.IsFailure)
            return validation.WithValue<BuiltinReplyDefaultDto>(null!);

        PlatformBuiltinReplyDefault? current = await FindAsync(builtinKey, slot, ct);
        PlatformDefaultBlastRadiusDto radius = await CountAsync(
            builtinKey,
            slot,
            current,
            template,
            ct
        );
        if (radius.ChannelsAffected != request.ConfirmedChannelsAffected)
            return Result.Failure<BuiltinReplyDefaultDto>(
                $"The change now affects {radius.ChannelsAffected} channels, not the {request.ConfirmedChannelsAffected} you confirmed. Review it again.",
                "PREVIEW_STALE"
            );

        DateTime now = clock.GetUtcNow().UtcDateTime;
        string oldValue = current?.Template ?? "(shipped)";
        if (template is null)
        {
            if (current is not null)
                db.PlatformBuiltinReplyDefaults.Remove(current);
        }
        else if (current is null)
        {
            db.PlatformBuiltinReplyDefaults.Add(
                new()
                {
                    BuiltinKey = builtinKey,
                    Slot = slot,
                    Template = template,
                    UpdatedByUserId = actorUserId,
                }
            );
        }
        else
        {
            current.Template = template;
            current.UpdatedByUserId = actorUserId;
        }
        PlatformDefaultAudit.Record(
            db,
            AuditFamily,
            $"{builtinKey}:{slot}",
            actorUserId,
            oldValue,
            template ?? "(shipped)",
            radius.ChannelsAffected,
            now
        );
        await db.SaveChangesAsync(ct);
        await runtimeReplies.InvalidateAsync(ct);

        string? saved = await db
            .PlatformBuiltinReplyDefaults.AsNoTracking()
            .Where(d => d.BuiltinKey == builtinKey && d.Slot == slot)
            .Select(d => d.Template)
            .FirstOrDefaultAsync(ct);
        return Result.Success(
            ToDto(builtinKey, slot, saved, await ChannelsWithOwnReplyBySlotAsync(ct))
        );
    }

    /// <summary>Only catalogued slots are editable; a text must fit a chat message and must say something.</summary>
    private static Result Validate(string builtinKey, string slot, string? template)
    {
        if (!ToneTemplateCatalog.Contains(builtinKey, slot))
            return Result.Failure($"Unknown built-in reply '{builtinKey}:{slot}'.", "NOT_FOUND");
        if (Normalize(template) is { Length: > MaxTemplateLength })
            return Result.Failure(
                $"The reply is longer than {MaxTemplateLength} characters.",
                "VALIDATION_FAILED"
            );
        return Result.Success();
    }

    private static string? Normalize(string? template) =>
        string.IsNullOrWhiteSpace(template) ? null : template.Trim();

    private async Task<PlatformDefaultBlastRadiusDto> CountAsync(
        string builtinKey,
        string slot,
        PlatformBuiltinReplyDefault? current,
        string? proposed,
        CancellationToken ct
    )
    {
        bool changes = !string.Equals(
            current?.Template,
            Normalize(proposed),
            StringComparison.Ordinal
        );
        // A channel with its own text for this exact slot keeps it; every other active channel feels the change.
        IReadOnlyCollection<Guid> keeping =
            (await ChannelsWithOwnReplyBySlotAsync(ct)).GetValueOrDefault(SlotKey(builtinKey, slot))
            ?? [];
        return await PlatformDefaultBlastRadius.CountAsync(
            db,
            keeping,
            valueChanges: changes,
            requiresDangerConfirmation: false,
            ct
        );
    }

    /// <summary>
    /// Per reply slot (<see cref="SlotKey"/>), the channels with their own text for it — the same parse the channel
    /// registry applies at runtime (reply group resolved the same way, legacy single override included).
    /// </summary>
    private async Task<Dictionary<string, List<Guid>>> ChannelsWithOwnReplyBySlotAsync(
        CancellationToken ct
    )
    {
        // Anonymous projection forces `var`.
        var rows = await db
            .ChannelBuiltinCommands.IgnoreQueryFilters()
            .Where(c => c.DeletedAt == null && c.OverridesJson != null)
            .Select(c => new
            {
                c.BroadcasterId,
                c.BuiltinKey,
                c.OverridesJson,
            })
            .ToListAsync(ct);
        Dictionary<string, List<Guid>> bySlot = new(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            string group = BuiltinResponseSlots.ReplyGroupFor(row.BuiltinKey);
            foreach (
                string slot in BuiltinOverridesJson
                    .EffectiveResponses(row.BuiltinKey, row.OverridesJson)
                    .Keys
            )
            {
                string key = SlotKey(group, slot);
                if (!bySlot.TryGetValue(key, out List<Guid>? channels))
                    bySlot[key] = channels = [];
                if (!channels.Contains(row.BroadcasterId))
                    channels.Add(row.BroadcasterId);
            }
        }
        return bySlot;
    }

    private static string SlotKey(string replyGroup, string slot) =>
        replyGroup + "|" + slot.ToLowerInvariant();

    private async Task<PlatformBuiltinReplyDefault?> FindAsync(
        string builtinKey,
        string slot,
        CancellationToken ct
    ) =>
        await db.PlatformBuiltinReplyDefaults.FirstOrDefaultAsync(
            d => d.BuiltinKey == builtinKey && d.Slot == slot,
            ct
        );

    private static BuiltinReplyDefaultDto ToDto(
        string builtinKey,
        string slot,
        string? platformTemplate,
        Dictionary<string, List<Guid>> ownReplies
    )
    {
        return new(
            builtinKey,
            slot,
            ToneTemplateCatalog.ShippedTemplate(builtinKey, slot),
            platformTemplate,
            ownReplies.GetValueOrDefault(SlotKey(builtinKey, slot))?.Count ?? 0
        );
    }
}
