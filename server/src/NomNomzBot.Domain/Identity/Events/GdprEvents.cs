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

/// <summary>When someone asks to have their personal data erased.</summary>
public sealed class SubjectErasureRequestedEvent : DomainEventBase
{
    /// <summary>The id of the erasure request.</summary>
    public required Guid ErasureRequestId { get; init; }

    /// <summary>The id of the user whose data is erased.</summary>
    public required Guid SubjectUserId { get; init; }

    /// <summary>A hash of the user id. It lets the logs refer to the user without the real id.</summary>
    public required string SubjectIdHash { get; init; }

    /// <summary>The kind of request: erasure, export, opt_out or consent_change.</summary>
    public required string RequestType { get; init; }

    /// <summary>Who made the request: self_service, broadcaster or platform_iam.</summary>
    public required string RequestedBy { get; init; }

    /// <summary>How much data the request covers: deployment, instance or channel.</summary>
    public required string Scope { get; init; }
}

/// <summary>When the personal data of a user has been erased.</summary>
public sealed class SubjectErasureCompletedEvent : DomainEventBase
{
    /// <summary>The id of the erasure request.</summary>
    public required Guid ErasureRequestId { get; init; }

    /// <summary>The id of the user whose data was erased.</summary>
    public required Guid SubjectUserId { get; init; }

    /// <summary>A hash of the user id. It lets the logs refer to the user without the real id.</summary>
    public required string SubjectIdHash { get; init; }

    /// <summary>True when the encryption keys for the data were destroyed.</summary>
    public required bool CryptoShredApplied { get; init; }

    /// <summary>True when the remaining data was made anonymous.</summary>
    public required bool AnonymizationApplied { get; init; }

    /// <summary>How many encryption keys were destroyed.</summary>
    public required int KeysShredded { get; init; }

    /// <summary>How many data records were changed or removed.</summary>
    public required int RowsAffected { get; init; }
}

/// <summary>When an erasure request fails.</summary>
public sealed class SubjectErasureFailedEvent : DomainEventBase
{
    /// <summary>The id of the erasure request.</summary>
    public required Guid ErasureRequestId { get; init; }

    /// <summary>The id of the user whose data should have been erased.</summary>
    public required Guid SubjectUserId { get; init; }

    /// <summary>A hash of the user id. It lets the logs refer to the user without the real id.</summary>
    public required string SubjectIdHash { get; init; }

    /// <summary>Why the erasure failed, in plain text.</summary>
    public required string FailureReason { get; init; }
}

/// <summary>When the personal data of a user is exported.</summary>
public sealed class SubjectDataExportedEvent : DomainEventBase
{
    /// <summary>The id of the request that asked for the export.</summary>
    public required Guid ErasureRequestId { get; init; }

    /// <summary>The id of the user whose data was exported.</summary>
    public required Guid SubjectUserId { get; init; }

    /// <summary>A hash of the user id. It lets the logs refer to the user without the real id.</summary>
    public required string SubjectIdHash { get; init; }

    /// <summary>The file format of the export.</summary>
    public required string ExportFormat { get; init; }

    /// <summary>Where the export file is stored.</summary>
    public required string ExportLocation { get; init; }

    /// <summary>How many data records the export holds.</summary>
    public required int RowsAffected { get; init; }
}

/// <summary>When a user gives or withdraws a consent.</summary>
public sealed class ConsentChangedEvent : DomainEventBase
{
    /// <summary>The id of the consent record.</summary>
    public required Guid ConsentRecordId { get; init; }

    /// <summary>The id of the user the consent belongs to.</summary>
    public required Guid SubjectUserId { get; init; }

    /// <summary>A hash of the user id. It lets the logs refer to the user without the real id.</summary>
    public required string SubjectIdHash { get; init; }

    /// <summary>What the consent is for: tos_privacy, age_18_gambling, pronoun_special_category, leaderboard_opt_in or marketing.</summary>
    public required string ConsentType { get; init; }

    /// <summary>The new status of the consent: granted or withdrawn.</summary>
    public required string Status { get; init; }

    /// <summary>The legal reason for using the data: consent, contract or legitimate_interest.</summary>
    public required string LawfulBasis { get; init; }

    /// <summary>The version of the consent text the user saw. Empty when there is no version.</summary>
    public string? ConsentVersion { get; init; }
}
