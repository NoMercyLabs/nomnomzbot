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
