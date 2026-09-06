// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Identity.Dtos;

public sealed record AdminStatsDto(
    int TotalChannels,
    int ActiveChannels,
    int TotalUsers,
    string SystemStatus,
    long BotUptimeSeconds,
    int EventsProcessedToday
);

public sealed record AdminChannelDto(
    string Id,
    string DisplayName,
    string Login,
    bool IsLive,
    bool IsActive,
    int ViewerCount,
    string Plan,
    DateTime CreatedAt
);

public sealed record ServiceHealthDto(string Name, string Status, int? LatencyMs);

public sealed record AdminUserDto(
    string Id,
    string DisplayName,
    string Login,
    string? Email,
    string Role,
    int ChannelCount,
    DateTime CreatedAt,
    DateTime? LastActive
);

public sealed record AdminSystemDto(
    string Overall,
    List<ServiceHealthDto> Services,
    string BotVersion,
    long MemoryUsageMb,
    double CpuPercent
);

// ── EventSub subscription health (S-ADMIN-6a) ──

/// <summary>
/// One topic in the real EventSub registry <c>TwitchEventSubHostedService</c> maintains — never a fabricated
/// row. <see cref="LastConfirmedAt"/> is the registry row's own <c>UpdatedAt</c>, the last time this exact
/// status was persisted (created, re-homed on reconnect, or flipped by a Twitch revocation notice).
/// </summary>
public sealed record AdminEventSubTopicHealthDto(
    Guid SubscriptionId,
    string EventType,
    string Version,
    string Status,
    bool Enabled,
    string? LastError,
    DateTime LastConfirmedAt
);

/// <summary>One tenant's EventSub registry rows, grouped for the 2am operator console (S-ADMIN-6a).</summary>
public sealed record AdminEventSubTenantHealthDto(
    Guid BroadcasterId,
    string ChannelDisplayName,
    IReadOnlyList<AdminEventSubTopicHealthDto> Topics
);

// ── Outbound webhook delivery log + replay (S-ADMIN-6a) ──

/// <summary>One delivery attempt across ALL tenants, for the platform-wide delivery log. <see cref="EndpointName"/>
/// reads "(deleted endpoint)" when the endpoint has since been soft-deleted — the row is kept, never hidden.</summary>
public sealed record AdminWebhookDeliveryDto(
    long Id,
    Guid BroadcasterId,
    Guid EndpointId,
    string EndpointName,
    bool EndpointCanReplay,
    string EventType,
    int Attempt,
    string Status,
    int? ResponseCode,
    int? DurationMs,
    string? Error,
    DateTime CreatedAt
);

/// <summary>
/// The outcome of an admin-initiated replay: a genuinely NEW delivery row was created and sent — the original
/// attempt this replays is never mutated.
/// </summary>
public sealed record AdminWebhookReplayResultDto(
    long OriginalDeliveryId,
    long NewDeliveryId,
    string Status,
    int? ResponseCode
);

// ── Background job queue + retry (S-ADMIN-6b) ──

/// <summary>
/// One row of the REAL background job queue — the <c>ScheduledPipelineTask</c> deferred-dispatch primitive
/// (a voice-swap auto-revert, a feather auto-hide, a timed reward) that a background sweeper fires when due.
/// This is never a fabricated parallel queue: it reads the exact rows <c>ScheduledPipelineExpiryService</c>
/// dispatches. <see cref="Status"/> is the raw persisted value (pending/fired/cancelled/expired);
/// <see cref="DisplayState"/> is the operator-facing label (queued/running/succeeded/failed/cancelled) derived
/// from it plus <see cref="DueAt"/> against the current clock. <see cref="CanRetry"/> is true only for a
/// genuinely failed (expired) job whose target pipeline still exists.
/// </summary>
public sealed record AdminScheduledJobDto(
    Guid Id,
    Guid BroadcasterId,
    string ChannelDisplayName,
    Guid PipelineId,
    string? PipelineName,
    bool PipelineExists,
    string Status,
    string DisplayState,
    DateTime DueAt,
    DateTime? FiredAt,
    DateTime CreatedAt,
    string TriggeredByDisplayName,
    bool CanRetry
);

/// <summary>
/// The outcome of an admin-initiated job retry: a brand-new <c>ScheduledPipelineTask</c> row was appended,
/// due immediately — the original failed attempt this retries is never mutated or replaced.
/// </summary>
public sealed record AdminScheduledJobRetryResultDto(
    Guid OriginalTaskId,
    Guid NewTaskId,
    Guid PipelineId,
    string PipelineName,
    DateTime NewDueAt
);

// ── Per-tenant usage (S-ADMIN-6b) + cost (S-ADMIN-4d) ──

/// <summary>
/// One metered quantity for a tenant's usage period, straight off the real <c>UsageRecord</c> rows, joined
/// against the owner-authored <c>PricedUnit</c> catalogue (S-ADMIN-4d). <see cref="CostMinorUnits"/> and
/// <see cref="Currency"/> are both <c>null</c> when <see cref="MetricKey"/> has no <c>PricedUnit</c> row —
/// UNPRICED, never reported as a zero cost.
/// </summary>
public sealed record AdminTenantUsageMetricDto(
    string MetricKey,
    long Quantity,
    long? CostMinorUnits = null,
    string? Currency = null
);

/// <summary>
/// One tenant's usage for its most recent metering period, computed purely from recorded rows
/// (<c>UsageRecord</c> + <c>TtsUsageRecord</c>) — never a placeholder figure. <see cref="PeriodStart"/>/
/// <see cref="PeriodEnd"/> state exactly which window the figures cover.
/// <para>
/// Cost (S-ADMIN-4d) is reported ONLY for units the owner has actually priced via the <c>PricedUnit</c>
/// catalogue: each priced metric in <see cref="Metrics"/> carries its own cost, <see cref="TtsCostMinorUnits"/>
/// is the TTS character cost (or <c>null</c> if unpriced), <see cref="TotalCostsByCurrency"/> sums every
/// priced figure grouped by currency, and <see cref="UnpricedUnitKeys"/> names every unit this tenant
/// actually used that has no price yet — so an unpriced unit is never silently read as costing nothing.
/// </para>
/// </summary>
public sealed record AdminTenantUsageDto(
    Guid BroadcasterId,
    string ChannelDisplayName,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    IReadOnlyList<AdminTenantUsageMetricDto> Metrics,
    long TtsCharacterCount,
    long? TtsCostMinorUnits = null,
    string? TtsCurrency = null,
    IReadOnlyList<AdminTenantUsageCostDto>? TotalCostsByCurrency = null,
    IReadOnlyList<string>? UnpricedUnitKeys = null
);

/// <summary>One currency's worth of a tenant's total priced usage cost for the period.</summary>
public sealed record AdminTenantUsageCostDto(string Currency, long MinorUnits);

// ── Error budget (S-ADMIN-6c) ──

/// <summary>
/// One tenant's error budget for the trailing 24-hour window, computed purely from real
/// <c>OutboundWebhookDelivery</c> outcomes (<c>Delivered</c> = success, <c>Failed</c>/<c>DeadLetter</c> = error;
/// <c>Pending</c> attempts are excluded — they have not resolved yet). There is no platform-wide blended figure:
/// one tenant's failures never count toward another's, because the entire point of a 2am tool is finding WHICH
/// tenant's integration is actually broken. A tenant with no resolved delivery attempts in the window is simply
/// absent from the page — never padded with a fabricated 0%. <see cref="ErrorRate"/> and
/// <see cref="BudgetRemainingFraction"/> are <c>null</c> when <see cref="Attempts"/> is 0.
/// <see cref="TargetSuccessRate"/> is a stated policy threshold (99%), not a measured quantity — the allowed
/// error rate it implies is the denominator <see cref="BudgetRemainingFraction"/> is computed against.
/// </summary>
public sealed record AdminTenantErrorBudgetDto(
    Guid BroadcasterId,
    string ChannelDisplayName,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    long Attempts,
    long Errors,
    double? ErrorRate,
    double TargetSuccessRate,
    double? BudgetRemainingFraction
);

// ── Event-store replay (S-ADMIN-6c) ──

/// <summary>One projection an admin replay can target, and the REAL event types it subscribes to — reflected
/// straight off the registered <c>IProjection</c> instances, never a hardcoded dropdown.</summary>
public sealed record AdminReplayableProjectionDto(
    string ProjectionName,
    bool IsGlobal,
    IReadOnlyList<string> SubscribedEventTypes
);

/// <summary>
/// The REAL count of journal events a tenant + window + optional event-type scope would replay into the named
/// projection, computed at preview time — never an estimate. The operator must see and confirm this exact
/// count before <see cref="AdminEventReplayResultDto"/> can be produced.
/// </summary>
public sealed record AdminEventReplayPreviewDto(
    Guid BroadcasterId,
    string ProjectionName,
    DateTime FromUtc,
    DateTime ToUtc,
    string? EventType,
    long MatchingEventCount
);

/// <summary>
/// The outcome of an admin-initiated replay: <see cref="AppliedCount"/> journal events in the stated scope
/// were re-applied, in order, to the named projection's <c>ApplyAsync</c> — the exact per-projection fold the
/// live driver uses, not a second engine. Never mutates the journal itself (append-only, replay only reads it).
/// </summary>
public sealed record AdminEventReplayResultDto(
    Guid BroadcasterId,
    string ProjectionName,
    DateTime FromUtc,
    DateTime ToUtc,
    string? EventType,
    long AppliedCount
);
