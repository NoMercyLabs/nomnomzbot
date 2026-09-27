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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.PlatformDefaults.Dtos;
using NomNomzBot.Application.PlatformDefaults.Services;
using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Infrastructure.PlatformDefaults;

/// <summary>
/// See <see cref="IEventResponseDefaultsAdminService"/>. Writes <see cref="PlatformEventResponseDefault"/>;
/// the event-response executor reads it on every event for each channel whose row still follows the platform
/// default, so a save takes effect on the next event with no cache to flush.
/// </summary>
public sealed class EventResponseDefaultsAdminService(
    IApplicationDbContext db,
    ITemplateHelperValidator templateHelperValidator,
    TimeProvider clock
) : IEventResponseDefaultsAdminService
{
    private const string AuditFamily = "event_response";
    private const int MaxMessageLength = 2000;

    public async Task<Result<IReadOnlyList<EventResponseDefaultDto>>> ListAsync(
        CancellationToken ct = default
    )
    {
        List<PlatformEventResponseDefault> defaults = await db
            .PlatformEventResponseDefaults.OrderBy(d => d.EventType)
            .ToListAsync(ct);
        Dictionary<string, int> following = await CountRowsAsync(followsDefault: true, ct);
        Dictionary<string, int> own = await CountRowsAsync(followsDefault: false, ct);

        List<EventResponseDefaultDto> rows =
        [
            .. defaults.Select(d =>
                ToDto(
                    d,
                    following.GetValueOrDefault(d.EventType),
                    own.GetValueOrDefault(d.EventType)
                )
            ),
        ];
        return Result.Success<IReadOnlyList<EventResponseDefaultDto>>(rows);
    }

    public async Task<Result<PlatformDefaultBlastRadiusDto>> PreviewAsync(
        string eventType,
        EventResponseDefaultChange change,
        CancellationToken ct = default
    )
    {
        PlatformEventResponseDefault? current = await FindAsync(eventType, ct);
        if (current is null)
            return Result.Failure<PlatformDefaultBlastRadiusDto>(
                $"Unknown event type '{eventType}'.",
                "NOT_FOUND"
            );

        Result validation = Validate(change.IsEnabled, change.Message);
        if (validation.IsFailure)
            return validation.WithValue<PlatformDefaultBlastRadiusDto>(null!);

        return Result.Success(await CountAsync(current, change.IsEnabled, change.Message, ct));
    }

    public async Task<Result<EventResponseDefaultDto>> SetAsync(
        string eventType,
        SetEventResponseDefaultRequest request,
        Guid actorUserId,
        CancellationToken ct = default
    )
    {
        PlatformEventResponseDefault? current = await FindAsync(eventType, ct);
        if (current is null)
            return Result.Failure<EventResponseDefaultDto>(
                $"Unknown event type '{eventType}'.",
                "NOT_FOUND"
            );

        string? message = Normalize(request.Message);
        Result validation = Validate(request.IsEnabled, message);
        if (validation.IsFailure)
            return validation.WithValue<EventResponseDefaultDto>(null!);

        PlatformDefaultBlastRadiusDto radius = await CountAsync(
            current,
            request.IsEnabled,
            message,
            ct
        );
        if (radius.ChannelsAffected != request.ConfirmedChannelsAffected)
            return Result.Failure<EventResponseDefaultDto>(
                $"The change now affects {radius.ChannelsAffected} channels, not the {request.ConfirmedChannelsAffected} you confirmed. Review it again.",
                "PREVIEW_STALE"
            );

        DateTime now = clock.GetUtcNow().UtcDateTime;
        string oldValue = Describe(current.IsEnabled, current.Message);
        current.IsEnabled = request.IsEnabled;
        current.Message = message;
        current.UpdatedByUserId = actorUserId;
        PlatformDefaultAudit.Record(
            db,
            AuditFamily,
            eventType,
            actorUserId,
            oldValue,
            Describe(current.IsEnabled, current.Message),
            radius.ChannelsAffected,
            now
        );
        await db.SaveChangesAsync(ct);

        PlatformEventResponseDefault saved = await db
            .PlatformEventResponseDefaults.AsNoTracking()
            .FirstAsync(d => d.Id == current.Id, ct);
        Dictionary<string, int> following = await CountRowsAsync(followsDefault: true, ct);
        Dictionary<string, int> own = await CountRowsAsync(followsDefault: false, ct);
        return Result.Success(
            ToDto(saved, following.GetValueOrDefault(eventType), own.GetValueOrDefault(eventType))
        );
    }

    /// <summary>An enabled default must say something, and its template may only use event-response helpers.</summary>
    private Result Validate(bool isEnabled, string? message)
    {
        string? normalized = Normalize(message);
        if (isEnabled && normalized is null)
            return Result.Failure(
                "An enabled default needs a message to send.",
                "VALIDATION_FAILED"
            );
        if (normalized is { Length: > MaxMessageLength })
            return Result.Failure(
                $"The message is longer than {MaxMessageLength} characters.",
                "VALIDATION_FAILED"
            );
        return templateHelperValidator.Validate(normalized, TemplateHelperContext.EventResponse);
    }

    private static string? Normalize(string? message) =>
        string.IsNullOrWhiteSpace(message) ? null : message.Trim();

    private async Task<PlatformDefaultBlastRadiusDto> CountAsync(
        PlatformEventResponseDefault current,
        bool isEnabled,
        string? message,
        CancellationToken ct
    )
    {
        bool changes =
            current.IsEnabled != isEnabled
            || !string.Equals(current.Message, Normalize(message), StringComparison.Ordinal);
        // Only a channel whose row follows the default feels it; every other channel (its own response, or no
        // row at all) keeps what it has.
        IQueryable<Guid> followingChannels = LiveRows()
            .Where(r => r.EventType == current.EventType && r.FollowsPlatformDefault)
            .Select(r => r.BroadcasterId);
        IQueryable<Guid> keepingOwn = db
            .Channels.Where(c => !followingChannels.Contains(c.Id))
            .Select(c => c.Id);
        return await PlatformDefaultBlastRadius.CountAsync(
            db,
            keepingOwn,
            valueChanges: changes,
            requiresDangerConfirmation: false,
            ct
        );
    }

    private async Task<Dictionary<string, int>> CountRowsAsync(
        bool followsDefault,
        CancellationToken ct
    ) =>
        await LiveRows()
            .Where(r => r.FollowsPlatformDefault == followsDefault)
            .GroupBy(r => r.EventType)
            .Select(g => new { EventType = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EventType, x => x.Count, StringComparer.Ordinal, ct);

    // The admin request carries no channel target, so the tenant filter would narrow this to the operator's own
    // channel; the platform view reads every channel's live row explicitly.
    private IQueryable<EventResponse> LiveRows() =>
        db.EventResponses.IgnoreQueryFilters().Where(r => r.DeletedAt == null);

    private async Task<PlatformEventResponseDefault?> FindAsync(
        string eventType,
        CancellationToken ct
    ) =>
        await db.PlatformEventResponseDefaults.FirstOrDefaultAsync(
            d => d.EventType == eventType,
            ct
        );

    private static string Describe(bool isEnabled, string? message) =>
        $"enabled={isEnabled};message={message ?? "-"}";

    private static EventResponseDefaultDto ToDto(
        PlatformEventResponseDefault d,
        int following,
        int own
    )
    {
        EventResponsePresetDto? preset = EventResponsePresetCatalog.Presets.FirstOrDefault(p =>
            p.EventType == d.EventType
        );
        return new(
            d.EventType,
            d.IsEnabled,
            d.Message,
            preset?.Variables ?? [],
            following,
            own,
            d.UpdatedAt
        );
    }
}
