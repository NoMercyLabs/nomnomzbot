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
using NomNomzBot.Application.Common.Models.Crypto;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Services;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.EventStore;

/// <summary>
/// The journal payload-encryption seam (gdpr-crypto.md §3.4). Seals a PII-bearing event's payload under the
/// acting subject's per-subject DEK (via <see cref="ISubjectKeyService"/>) and opens it back on read. Envelope
/// encryption: a per-subject DEK (AES-256-GCM) seals the payload, the KEK wraps the DEK, and crypto-shred of the
/// DEK renders every row sealed under it permanently unreadable.
///
/// <para><b>Shred reachability is the whole point.</b> The subject identity used to resolve the DEK
/// (<c>subjectUserId</c> + <see cref="ConsentService.ComputeSubjectIdHash"/>) is the SAME identity the erasure
/// pipeline (<c>ISubjectKeyService.ResolveSubjectKeysAsync</c>) resolves and destroys, so a row sealed here is
/// always in the subject's crypto-shred set. Encrypting under a key the erasure planner could not reach would
/// leave PII that erasure could never destroy — so the classification below only encrypts when a shred-reachable
/// internal subject id is present; a subject-less event stays plaintext.</para>
/// </summary>
public sealed class EventPayloadProtector : IEventPayloadProtector
{
    // Self-describing envelope stored in the single Payload column: version | nonce | ciphertext (both base64,
    // whose alphabet never contains '|', so the split is unambiguous). The key id lives in SubjectKeyId, so —
    // unlike the token envelope — it is not embedded here.
    private const string EnvelopeVersion = "v1";
    private const char EnvelopeSeparator = '|';

    // AAD (anti-transplant) = tenant ‖ resource-domain ‖ event-type ‖ key-version, reconstructed identically at
    // read from the row's own columns. Binding the event type stops a chat-message ciphertext being replayed
    // onto a subscription row under the same subject DEK; binding the tenant stops a cross-channel transplant.
    private const string ResourceDomain = "eventjournal";
    private const string ResourceColumn = "Payload";
    private const string PlatformTenant = "platform";
    private const string AadKeyVersion = "1";

    private readonly ISubjectKeyService _subjectKeys;

    public EventPayloadProtector(ISubjectKeyService subjectKeys) => _subjectKeys = subjectKeys;

    public async Task<Result<ProtectedPayload>> ProtectAsync(
        AppendEventRequest request,
        CancellationToken cancellationToken = default
    )
    {
        // A payload is treated as PII-bearing exactly when the event is attributed to an internal subject. That
        // is the safe, shred-correct trigger: only then can we seal under a DEK the erasure pipeline will reach.
        if (request.ActorUserId is not Guid subjectUserId || subjectUserId == Guid.Empty)
            return Result.Success(
                new ProtectedPayload(request.PayloadJson, IsEncrypted: false, SubjectKeyId: null)
            );

        string subjectIdHash = ConsentService.ComputeSubjectIdHash(subjectUserId);

        Result<Guid> keyId = await _subjectKeys.GetOrCreateSubjectKeyAsync(
            subjectUserId,
            subjectIdHash,
            cancellationToken
        );
        if (keyId.IsFailure)
            return keyId.ToTyped<ProtectedPayload>();

        CipherAad aad = BuildAad(request.BroadcasterId, request.EventType);
        Result<CipherPayload> sealedPayload = await _subjectKeys.ProtectAsync(
            keyId.Value,
            request.PayloadJson,
            aad,
            ResourceDomain,
            ResourceColumn,
            cancellationToken
        );
        if (sealedPayload.IsFailure)
            return sealedPayload.ToTyped<ProtectedPayload>();

        return Result.Success(
            new ProtectedPayload(
                SerializeEnvelope(sealedPayload.Value),
                IsEncrypted: true,
                SubjectKeyId: keyId.Value
            )
        );
    }

    public async Task<Result<string>> UnprotectAsync(
        EventRecord record,
        CancellationToken cancellationToken = default
    )
    {
        if (!record.PayloadIsEncrypted)
            return Result.Success(record.PayloadJson);

        if (record.SubjectKeyId is not Guid keyId)
            return Result.Failure<string>(
                "An encrypted journal row carries no subject key.",
                "SUBJECT_KEY_MISSING"
            );

        if (!TryParseEnvelope(record.PayloadJson, out CipherPayload payload))
            return Result.Failure<string>(
                "The journal payload envelope is malformed.",
                "ENVELOPE_INVALID"
            );

        CipherAad aad = BuildAad(record.BroadcasterId, record.EventType);
        return await _subjectKeys.UnprotectAsync(keyId, payload, aad, cancellationToken);
    }

    private static CipherAad BuildAad(Guid? broadcasterId, string eventType) =>
        new(
            TenantId: broadcasterId?.ToString() ?? PlatformTenant,
            Provider: ResourceDomain,
            TokenType: eventType,
            KeyVersion: AadKeyVersion
        );

    private static string SerializeEnvelope(CipherPayload payload) =>
        string.Join(EnvelopeSeparator, EnvelopeVersion, payload.Nonce, payload.CipherText);

    private static bool TryParseEnvelope(string envelope, out CipherPayload payload)
    {
        payload = null!;
        string[] parts = envelope.Split(EnvelopeSeparator);
        if (parts.Length != 3 || parts[0] != EnvelopeVersion)
            return false;

        payload = new CipherPayload(CipherText: parts[2], Nonce: parts[1]);
        return true;
    }
}
