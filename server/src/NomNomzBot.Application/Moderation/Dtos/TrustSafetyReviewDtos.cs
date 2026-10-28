// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Application.Moderation.Dtos;

/// <summary>
/// One tenant this actor's abuse signal was recorded in (S-ADMIN-8a) — a real, persisted
/// <c>SpamDetection</c> row, never a heuristic guess.
/// </summary>
public sealed record CrossTenantAbuseHitDto(
    Guid BroadcasterId,
    string ChannelName,
    Guid DetectionId,
    SpamConfidence Confidence,
    SpamOutcome Outcome,
    string Reason,
    DateTime DetectedAt
);

/// <summary>
/// One actor (a platform account) whose spam-defence detections were recorded in TWO OR MORE tenants of
/// this deployment — cross-tenant abuse correlation computed purely from real recorded
/// <c>SpamDetection</c> rows, never a lookalike heuristic. <see cref="Hits"/> names every tenant and
/// detection the correlation rests on, so an operator can verify the call rather than trust it.
/// </summary>
public sealed record CrossTenantAbuseSignalDto(
    string Provider,
    string SubjectPlatformUserId,
    string SubjectDisplayName,
    int TenantCount,
    int DetectionCount,
    IReadOnlyList<CrossTenantAbuseHitDto> Hits
);

/// <summary>
/// One automatic account action the spam-defence engine took on its own (S-ADMIN-8a) — carries the
/// evidence that caused it and the current review state. <see cref="ReversalPreview"/> is the human-
/// readable blast radius of an overturn, computed before the operator commits to one.
/// </summary>
public sealed record TrustSafetyReviewItemDto(
    Guid DetectionId,
    Guid BroadcasterId,
    string ChannelName,
    string SubjectPlatformUserId,
    string SubjectDisplayName,
    string Provider,
    string MessageText,
    string Signals,
    SpamConfidence Confidence,
    SpamOutcome Outcome,
    string Reason,
    DateTime DetectedAt,
    DateTime? ConfirmedAt,
    Guid? ConfirmedByUserId,
    DateTime? OverturnedAt,
    Guid? OverturnedByUserId,
    string ReversalPreview
);
