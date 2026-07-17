// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.EventStore;

/// <summary>
/// The journal's payload-encryption seam (gdpr-crypto.md §3.4). Decides whether an appended event carries PII
/// and, when it does, seals the payload JSON under the acting subject's per-subject DEK so a crypto-shred of
/// that DEK renders the row permanently unreadable (backups included). The inverse opens an encrypted row on
/// read. It is the only place that maps an event to a subject DEK at write time, so it OWNS the guarantee that
/// the key it seals under is the same key the erasure pipeline resolves and shreds for that subject.
/// </summary>
public interface IEventPayloadProtector
{
    /// <summary>
    /// Prepares the payload to persist for an append. A PII-bearing event (attributed to an internal subject via
    /// <see cref="AppendEventRequest.ActorUserId"/>) is sealed under that subject's DEK and returned as a
    /// self-describing ciphertext envelope with <c>IsEncrypted = true</c> and the sealing <c>SubjectKeyId</c>. A
    /// subject-less event returns its plaintext unchanged (<c>IsEncrypted = false</c>, <c>SubjectKeyId = null</c>).
    /// Fails CLOSED — a PII payload whose seal fails is NEVER downgraded to plaintext; the failure propagates so
    /// the caller does not journal the row in the clear.
    /// </summary>
    Task<Result<ProtectedPayload>> ProtectAsync(
        AppendEventRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Opens a journaled row's payload. A plaintext row returns its payload as-is; an encrypted row is decrypted
    /// under its <see cref="EventRecord.SubjectKeyId"/> with the AAD reconstructed from the row's own context.
    /// Returns failure <c>KEY_DESTROYED</c> once the subject DEK has been crypto-shredded (the GDPR guarantee,
    /// surfaced as a closed failure, not an exception).
    /// </summary>
    Task<Result<string>> UnprotectAsync(
        EventRecord record,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// The result of <see cref="IEventPayloadProtector.ProtectAsync"/>: the exact value to store in
/// <c>EventJournal.Payload</c> plus the two columns that describe it (<c>PayloadIsEncrypted</c>,
/// <c>SubjectKeyId</c>). For a plaintext event these mirror today's row (payload unchanged, false, null).
/// </summary>
public sealed record ProtectedPayload(string PayloadJson, bool IsEncrypted, Guid? SubjectKeyId);
