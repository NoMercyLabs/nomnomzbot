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
