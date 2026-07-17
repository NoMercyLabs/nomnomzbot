// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Events;

// GDPR domain events (gdpr-crypto.md §2). Payloads carry only the HASHED subject id beside the internal
// surrogate — never a raw Twitch id or username, so the events themselves survive the erasure they report.
// Tenant-scoped requests carry the channel in BroadcasterId; platform-wide ones leave the Guid.Empty sentinel.

public sealed class SubjectErasureRequestedEvent : DomainEventBase
{
    public required Guid ErasureRequestId { get; init; }
    public required Guid SubjectUserId { get; init; }
    public required string SubjectIdHash { get; init; }
    public required string RequestType { get; init; }
    public required string RequestedBy { get; init; }
    public required string Scope { get; init; }
}

public sealed class SubjectErasureCompletedEvent : DomainEventBase
{
    public required Guid ErasureRequestId { get; init; }
    public required Guid SubjectUserId { get; init; }
    public required string SubjectIdHash { get; init; }
    public required bool CryptoShredApplied { get; init; }
    public required bool AnonymizationApplied { get; init; }
    public required int KeysShredded { get; init; }
    public required int RowsAffected { get; init; }
}

public sealed class SubjectErasureFailedEvent : DomainEventBase
{
    public required Guid ErasureRequestId { get; init; }
    public required Guid SubjectUserId { get; init; }
    public required string SubjectIdHash { get; init; }
    public required string FailureReason { get; init; }
}

public sealed class SubjectDataExportedEvent : DomainEventBase
{
    public required Guid ErasureRequestId { get; init; }
    public required Guid SubjectUserId { get; init; }
    public required string SubjectIdHash { get; init; }
    public required string ExportFormat { get; init; }
    public required string ExportLocation { get; init; }
    public required int RowsAffected { get; init; }
}

public sealed class ConsentChangedEvent : DomainEventBase
{
    public required Guid ConsentRecordId { get; init; }
    public required Guid SubjectUserId { get; init; }
    public required string SubjectIdHash { get; init; }
    public required string ConsentType { get; init; }
    public required string Status { get; init; }
    public required string LawfulBasis { get; init; }
    public string? ConsentVersion { get; init; }
}
