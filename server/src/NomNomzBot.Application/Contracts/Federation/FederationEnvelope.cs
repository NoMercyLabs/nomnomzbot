// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Federation;

/// <summary>
/// The signed cross-instance message (federation-oidc.md §4). The body is serialized canonically (sorted keys,
/// UTF-8) for signature stability. <see cref="EventId"/> is the end-to-end dedupe key (== EventJournal.EventId).
/// </summary>
public sealed record FederationEventEnvelope(
    Guid EventId,
    string OriginInstanceId,
    Guid? OriginBroadcasterId,
    Guid? TargetBroadcasterId,
    string FederatedEventType,
    int SchemaVersion,
    string PayloadJson,
    DateTimeOffset OccurredAt
);

/// <summary>A detached per-message signature over an envelope's canonical body.</summary>
public sealed record FederationSignature(string KeyId, string Algorithm, string SignatureBase64);
