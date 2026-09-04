// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Platform.Eventing;

/// <summary>
/// Durable, cross-process implementation of <see cref="IDuplicateNotificationSuppressor"/> over the shared
/// <see cref="IdempotencyKey"/> table (schema §O.4) every deployment profile already has — Postgres on
/// full/SaaS, SQLite on self_host_lite — rather than an in-process cache, which is blind to the SECOND live
/// process a zero-downtime deploy always runs (S-DUPE): <c>scripts/switchover.ps1</c> starts the incoming
/// colour and waits for it to pass health-ready WHILE the outgoing colour is still serving, so for that
/// overlap both hold a live EventSub session and both can receive the same real event.
/// <para>
/// The claim is the table's existing unique <c>(Scope, Key, BroadcasterId)</c> index doing exactly the job it
/// was built for (<see cref="Domain.Platform.Entities.IdempotencyKey"/>'s own doc comment: "records that a
/// unit of work ... already ran, so a redelivery is short-circuited") — an atomic insert, not a
/// check-then-act: two processes racing to claim the same triple both attempt the insert, the database's
/// unique constraint admits exactly one, and the loser's <see cref="DbUpdateException"/> is the "already
/// claimed" signal. <see cref="Key"/> hashes the raw payload down to a fixed, indexable length; the semantic
/// identity is still the full payload bytes (see the interface's doc comment), only its on-disk representation
/// is compact.
/// </para>
/// </summary>
public sealed class DuplicateNotificationSuppressor : IDuplicateNotificationSuppressor
{
    // A namespace distinct from every other IdempotencyKey writer (kick-webhook, deployment, ...) so this
    // guard's claims can never collide with an unrelated feature's.
    private const string SemanticScope = "eventsub-semantic";

    private readonly IApplicationDbContext _db;

    public DuplicateNotificationSuppressor(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<bool> TryClaimAsync(
        Guid broadcasterId,
        string subscriptionType,
        string rawPayloadJson,
        DateTimeOffset now,
        TimeSpan window,
        CancellationToken ct = default
    )
    {
        string key = BuildKey(subscriptionType, rawPayloadJson);
        DateTime nowUtc = now.UtcDateTime;

        // Free this exact key the first time anyone lands after its window has closed, so a payload-sparse
        // topic (no per-occurrence id in the wire body) can legitimately repeat later without being wrongly
        // suppressed forever. A targeted delete on the unique key, not a scan.
        await _db
            .IdempotencyKeys.Where(k =>
                k.Scope == SemanticScope
                && k.Key == key
                && k.BroadcasterId == broadcasterId
                && k.ExpiresAt <= nowUtc
            )
            .ExecuteDeleteAsync(ct);

        _db.IdempotencyKeys.Add(
            new IdempotencyKey
            {
                Scope = SemanticScope,
                Key = key,
                BroadcasterId = broadcasterId,
                CreatedAt = nowUtc,
                ExpiresAt = now.Add(window).UtcDateTime,
            }
        );

        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex)
        {
            // Lost the race: another process (or this one, on an earlier delivery still inside the window)
            // already holds this exact claim. Detach so the failed insert never lingers in the change
            // tracker for whatever this scope's DbContext does next.
            foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry in ex.Entries)
                entry.State = EntityState.Detached;
            return false;
        }
    }

    private static string BuildKey(string subscriptionType, string rawPayloadJson)
    {
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{subscriptionType}|{rawPayloadJson}")
        );
        return Convert.ToHexStringLower(hash);
    }
}
