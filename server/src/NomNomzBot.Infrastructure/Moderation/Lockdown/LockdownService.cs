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
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation.Lockdown;

/// <summary>
/// Runs lockdown windows (spam-defense.md §L5.1). The window row is saved BEFORE any control is
/// tightened, carrying every prior value, so a crash between "tightened" and "recorded" can never leave a
/// room tightened with nobody who knows what to put back. <c>RestoredAt</c> is stamped only after the
/// platform confirmed every control was put back.
/// </summary>
public sealed class LockdownService : ILockdownService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Dictionary<string, PlatformLockdownCapabilities> KnownPlatforms = new[]
    {
        PlatformLockdownCapabilities.Twitch,
        PlatformLockdownCapabilities.Kick,
        PlatformLockdownCapabilities.YouTube,
        PlatformLockdownCapabilities.X,
    }.ToDictionary(c => c.Platform);

    private readonly IApplicationDbContext _db;
    private readonly ISpamDefenseService _spam;
    private readonly IReadOnlyList<IPlatformLockdownAdapter> _adapters;
    private readonly TimeProvider _time;
    private readonly IEventBus _events;
    private readonly ILogger<LockdownService> _logger;

    public LockdownService(
        IApplicationDbContext db,
        ISpamDefenseService spam,
        IEnumerable<IPlatformLockdownAdapter> adapters,
        TimeProvider time,
        IEventBus events,
        ILogger<LockdownService> logger
    )
    {
        _db = db;
        _spam = spam;
        _adapters = adapters.ToList();
        _time = time;
        _events = events;
        _logger = logger;
    }

    public async Task<IReadOnlyList<LockdownWindowStatus>> GetActiveAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        List<LockdownWindowRecord> open = await _db
            .LockdownWindows.IgnoreQueryFilters()
            .Where(w =>
                w.BroadcasterId == broadcasterId && w.RestoredAt == null && w.DeletedAt == null
            )
            .OrderBy(w => w.StartedAt)
            .ToListAsync(ct);
        return open.Select(ToStatus).ToList();
    }

    public async Task<Result<LockdownWindowStatus>> EngageAsync(
        Guid broadcasterId,
        string platform,
        string trigger,
        IReadOnlyCollection<LockdownControl> requested,
        CancellationToken ct = default
    )
    {
        string key = platform.Trim().ToLowerInvariant();
        if (!KnownPlatforms.TryGetValue(key, out PlatformLockdownCapabilities? known))
            return Result.Failure<LockdownWindowStatus>(
                $"Unknown platform '{platform}'.",
                "VALIDATION_FAILED"
            );

        SpamDefenseSettings policy = await _spam.GetSettingsAsync(broadcasterId, ct);
        DateTime now = _time.GetUtcNow().UtcDateTime;
        IPlatformLockdownAdapter? adapter = _adapters.FirstOrDefault(a => a.Platform == key);

        List<LockdownWindowRecord> open = await _db
            .LockdownWindows.IgnoreQueryFilters()
            .Where(w =>
                w.BroadcasterId == broadcasterId
                && w.Platform == key
                && w.RestoredAt == null
                && w.DeletedAt == null
            )
            .ToListAsync(ct);

        // A window that ran out (or was ended) but never got fully restored still holds the TRUE prior
        // values. Reading the room now would record the tightened values as "before", so put it back first.
        foreach (LockdownWindowRecord stale in open.Where(w => !IsActive(w, now)))
            await RestoreWindowAsync(stale, adapter, now, ct);
        if (open.Any(w => w.RestoredAt is null && !IsActive(w, now)))
            return Result.Failure<LockdownWindowStatus>(
                "An earlier lockdown window could not be fully restored yet.",
                "LOCKDOWN_RESTORE_PENDING"
            );

        LockdownWindowRecord? active = open.FirstOrDefault(w => IsActive(w, now));
        if (active is not null)
            return Result.Success(await ExtendAsync(active, policy, now, ct));

        return Result.Success(
            await OpenWindowAsync(
                broadcasterId,
                key,
                trigger,
                requested,
                known,
                adapter,
                policy,
                now,
                ct
            )
        );
    }

    public async Task<Result<LockdownWindowStatus>> EndAsync(
        Guid broadcasterId,
        string platform,
        CancellationToken ct = default
    )
    {
        string key = platform.Trim().ToLowerInvariant();
        DateTime now = _time.GetUtcNow().UtcDateTime;

        LockdownWindowRecord? window = await _db
            .LockdownWindows.IgnoreQueryFilters()
            .Where(w =>
                w.BroadcasterId == broadcasterId
                && w.Platform == key
                && w.RestoredAt == null
                && w.EndedAt == null
                && w.ExpiresAt > now
                && w.DeletedAt == null
            )
            .FirstOrDefaultAsync(ct);
        if (window is null)
            return Result.Failure<LockdownWindowStatus>(
                "No lockdown window is active here.",
                "NOT_FOUND"
            );

        window.EndedAt = now;
        window.ExpiresAt = now;
        await RestoreWindowAsync(window, _adapters.FirstOrDefault(a => a.Platform == key), now, ct);
        return Result.Success(ToStatus(window));
    }

    public async Task<int> RestoreDueAsync(CancellationToken ct = default)
    {
        DateTime now = _time.GetUtcNow().UtcDateTime;
        List<LockdownWindowRecord> due = await _db
            .LockdownWindows.IgnoreQueryFilters()
            .Where(w =>
                w.RestoredAt == null
                && w.DeletedAt == null
                && (w.EndedAt != null || w.ExpiresAt <= now)
            )
            .ToListAsync(ct);

        int restored = 0;
        foreach (LockdownWindowRecord window in due)
        {
            await RestoreWindowAsync(
                window,
                _adapters.FirstOrDefault(a => a.Platform == window.Platform),
                now,
                ct
            );
            if (window.RestoredAt is not null)
                restored++;
        }

        return restored;
    }

    private async Task<LockdownWindowStatus> ExtendAsync(
        LockdownWindowRecord window,
        SpamDefenseSettings policy,
        DateTime now,
        CancellationToken ct
    )
    {
        if (policy.LockdownAutoExtend)
        {
            DateTime ceiling = window.StartedAt.AddMinutes(policy.LockdownMaxMinutes);
            DateTime proposed = now.AddMinutes(policy.LockdownMinutes);
            DateTime next = proposed < ceiling ? proposed : ceiling;
            if (next > window.ExpiresAt)
            {
                window.ExpiresAt = next;
                await _db.SaveChangesAsync(ct);
                await _events.PublishAsync(
                    new LockdownExtendedEvent
                    {
                        BroadcasterId = window.BroadcasterId,
                        OccurredAt = _time.GetUtcNow(),
                        WindowId = window.Id,
                        Platform = window.Platform,
                        ExpiresAt = AsUtc(window.ExpiresAt),
                    },
                    ct
                );
            }
        }

        return ToStatus(window);
    }

    private async Task<LockdownWindowStatus> OpenWindowAsync(
        Guid broadcasterId,
        string key,
        string trigger,
        IReadOnlyCollection<LockdownControl> requested,
        PlatformLockdownCapabilities known,
        IPlatformLockdownAdapter? adapter,
        SpamDefenseSettings policy,
        DateTime now,
        CancellationToken ct
    )
    {
        HashSet<LockdownControl> drivable = adapter is null
            ? []
            : known.Supported.Where(adapter.Drives.Contains).ToHashSet();
        PlatformLockdownCapabilities capabilities = new(key, drivable);
        List<LockdownControl> wanted = requested.Distinct().ToList();

        List<LockdownControl> applyFailed = [];
        Dictionary<LockdownControl, string> current = [];
        HashSet<LockdownControl> toEngage = [];
        IReadOnlyDictionary<LockdownControl, Result<ControlReading>> readings = await ReadAsync(
            broadcasterId,
            adapter,
            wanted.Where(capabilities.Supports).ToList(),
            ct
        );
        foreach (LockdownControl control in wanted.Where(capabilities.Supports))
        {
            if (
                !readings.TryGetValue(control, out Result<ControlReading>? reading)
                || reading.IsFailure
            )
                applyFailed.Add(control);
            else if (!reading.Value.AlreadyLocked)
            {
                current[control] = reading.Value.PreviousValue;
                toEngage.Add(control);
            }
        }

        LockdownPlan plan = LockdownWindow.Plan(
            capabilities,
            wanted.Where(c => !capabilities.Supports(c) || toEngage.Contains(c)).ToList(),
            current
        );

        int minutes = Math.Min(policy.LockdownMinutes, policy.LockdownMaxMinutes);
        LockdownWindowRecord window = new()
        {
            BroadcasterId = broadcasterId,
            Platform = key,
            Trigger = trigger,
            StartedAt = now,
            ExpiresAt = now.AddMinutes(minutes),
            EngagedControlsJson = Serialize(plan.Engaged),
            UnavailableControlsJson = Serialize(plan.Unavailable),
            ApplyFailedControlsJson = Serialize(applyFailed),
        };
        _db.LockdownWindows.Add(window);
        await _db.SaveChangesAsync(ct);

        List<EngagedControl> applied = [];
        foreach (EngagedControl engaged in plan.Engaged)
        {
            Result locked = await TryAsync(() =>
                adapter!.LockAsync(broadcasterId, engaged.Control, ct)
            );
            if (locked.IsSuccess)
                applied.Add(engaged);
            else
            {
                applyFailed.Add(engaged.Control);
                _logger.LogWarning(
                    "Lockdown could not tighten {Control} on {Platform} for {BroadcasterId}: {Error}",
                    engaged.Control,
                    key,
                    broadcasterId,
                    locked.ErrorMessage
                );
            }
        }

        window.EngagedControlsJson = Serialize(applied);
        window.ApplyFailedControlsJson = Serialize(applyFailed);
        await _db.SaveChangesAsync(ct);

        LockdownWindowStatus status = ToStatus(window);
        await _events.PublishAsync(
            new LockdownEngagedEvent
            {
                BroadcasterId = broadcasterId,
                OccurredAt = _time.GetUtcNow(),
                WindowId = status.Id,
                Platform = status.Platform,
                Trigger = status.Trigger,
                StartedAt = status.StartedAt,
                ExpiresAt = status.ExpiresAt,
                Engaged = status.Engaged,
                Unavailable = status.Unavailable,
                ApplyFailed = status.ApplyFailed,
            },
            ct
        );
        return status;
    }

    private async Task<IReadOnlyDictionary<LockdownControl, Result<ControlReading>>> ReadAsync(
        Guid broadcasterId,
        IPlatformLockdownAdapter? adapter,
        IReadOnlyCollection<LockdownControl> controls,
        CancellationToken ct
    )
    {
        if (adapter is null || controls.Count == 0)
            return new Dictionary<LockdownControl, Result<ControlReading>>();

        try
        {
            return await adapter.ReadAsync(broadcasterId, controls, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Lockdown could not read {Platform} room settings",
                adapter.Platform
            );
            return new Dictionary<LockdownControl, Result<ControlReading>>();
        }
    }

    /// <summary>
    /// Put the window's controls back. A retry only touches the controls the last attempt left behind:
    /// the ones already restored may since have been changed by a moderator, and a retry must not undo that.
    /// </summary>
    private async Task RestoreWindowAsync(
        LockdownWindowRecord window,
        IPlatformLockdownAdapter? adapter,
        DateTime now,
        CancellationToken ct
    )
    {
        List<EngagedControl> engaged = Deserialize<EngagedControl>(window.EngagedControlsJson);
        List<LockdownControl> leftBehind = Deserialize<LockdownControl>(
            window.RestorationFailedControlsJson
        );
        List<EngagedControl> toRestore =
            leftBehind.Count > 0
                ? engaged.Where(e => leftBehind.Contains(e.Control)).ToList()
                : engaged;

        List<LockdownControl> failed = [];
        foreach (EngagedControl control in toRestore)
        {
            Result restored = adapter is null
                ? Result.Failure("No adapter for this platform.", "NOT_SUPPORTED")
                : await TryAsync(() => adapter.RestoreAsync(window.BroadcasterId, control, ct));
            if (restored.IsFailure)
            {
                failed.Add(control.Control);
                _logger.LogWarning(
                    "Lockdown could not restore {Control} on {Platform} for {BroadcasterId}: {Error}",
                    control.Control,
                    window.Platform,
                    window.BroadcasterId,
                    restored.ErrorMessage
                );
            }
        }

        window.RestorationFailedControlsJson = Serialize(failed);
        if (failed.Count == 0)
            window.RestoredAt = now;
        await _db.SaveChangesAsync(ct);

        if (failed.Count == 0)
            await _events.PublishAsync(
                new LockdownRestoredEvent
                {
                    BroadcasterId = window.BroadcasterId,
                    OccurredAt = _time.GetUtcNow(),
                    WindowId = window.Id,
                    Platform = window.Platform,
                    RestoredAt = AsUtc(now),
                    Restored = toRestore.Select(e => e.Control).ToList(),
                },
                ct
            );
        else if (!failed.ToHashSet().SetEquals(leftBehind))
            await _events.PublishAsync(
                new LockdownRestoreFailedEvent
                {
                    BroadcasterId = window.BroadcasterId,
                    OccurredAt = _time.GetUtcNow(),
                    WindowId = window.Id,
                    Platform = window.Platform,
                    Failed = failed,
                },
                ct
            );
    }

    private async Task<Result> TryAsync(Func<Task<Result>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Failure(ex.Message, "UPSTREAM_ERROR");
        }
    }

    private static bool IsActive(LockdownWindowRecord window, DateTime now) =>
        window.EndedAt is null && window.ExpiresAt > now;

    private static string Serialize<T>(IEnumerable<T> items) =>
        JsonSerializer.Serialize(items.ToList(), Json);

    private static List<T> Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<List<T>>(json, Json) ?? [];

    private static LockdownWindowStatus ToStatus(LockdownWindowRecord window) =>
        new(
            window.Id,
            window.Platform,
            window.Trigger,
            AsUtc(window.StartedAt),
            AsUtc(window.ExpiresAt),
            window.EndedAt is null ? null : AsUtc(window.EndedAt.Value),
            window.RestoredAt is null ? null : AsUtc(window.RestoredAt.Value),
            Deserialize<EngagedControl>(window.EngagedControlsJson).Select(e => e.Control).ToList(),
            Deserialize<LockdownControl>(window.UnavailableControlsJson),
            Deserialize<LockdownControl>(window.ApplyFailedControlsJson),
            Deserialize<LockdownControl>(window.RestorationFailedControlsJson)
        );

    private static DateTimeOffset AsUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
