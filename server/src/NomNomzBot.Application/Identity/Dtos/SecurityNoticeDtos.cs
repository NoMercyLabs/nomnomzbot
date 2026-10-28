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

namespace NomNomzBot.Application.Identity.Dtos;

/// <summary>Payload to durably record one security-relevant action against a channel (S-IMPERSONATION-NOTICE).</summary>
public sealed record RecordSecurityNoticeRequest(
    Guid BroadcasterId,
    string NoticeType,
    string Summary,
    Guid? ActorPrincipalId,
    Guid? TargetUserId,
    Guid? AccessGrantId,
    string? Reason,
    string? Scope,
    DateTime? ExpiresAt
);

/// <summary>The transport/read shape of a durable security notice.</summary>
public sealed record SecurityNoticeDto(
    Guid Id,
    string NoticeType,
    string Summary,
    Guid? ActorPrincipalId,
    Guid? TargetUserId,
    Guid? AccessGrantId,
    string? Reason,
    string? Scope,
    DateTime? ExpiresAt,
    DateTime CreatedAt,
    DateTime? AcknowledgedAt,
    Guid? AcknowledgedByUserId
);
