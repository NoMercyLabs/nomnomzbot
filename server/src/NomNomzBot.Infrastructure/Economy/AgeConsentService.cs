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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Economy;

/// <summary>
/// The lightweight 18+ gambling gate (economy.md §3.6). <see cref="HasGrantedAsync"/> passes an adult through
/// by affirmative consent, a MONOTONIC account-age inference, or a live Twitch-personnel inference — fail-closed
/// otherwise. Inferences are recorded only in the K.8 cache (<c>ConfirmationMethod=inferred_*</c>,
/// <c>LawfulBasis=legitimate_interest</c>), never as a <see cref="ConsentRecord"/> consent row. (Interim:
/// this service writes <see cref="ConsentRecord"/> directly until the GDPR-crypto subsystem's IConsentService
/// owns the ledger; the request IP is not stored — the crypto cipher is deferred.)
/// </summary>
public sealed class AgeConsentService(
    IApplicationDbContext db,
    IEventBus eventBus,
    TimeProvider clock
) : IAgeConsentService
{
    private const int Age18AccountYears = 7; // ≥5y is the proven floor (Twitch min signup age 13); 7 hedges
    private const string ConsentType = "age_18_gambling";
    private const string SelfConfirm = "self_confirm";
    private const string InferredAccountAge = "inferred_account_age";
    private const string InferredPersonnel = "inferred_twitch_personnel";

    private static readonly HashSet<string> PersonnelTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "staff",
        "admin",
        "global_mod",
    };

    public async Task<Result<bool>> HasGrantedAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CancellationToken ct = default
    )
    {
        ViewerAgeConsent? cache = await FindCacheAsync(broadcasterId, viewerUserId, ct);
        // Affirmative consent and the monotonic account-age inference are authoritative once recorded; a
        // personnel inference is revocable, so it is NOT short-circuited (re-checked live below).
        if (
            cache is { Granted: true, RevokedAt: null }
            && cache.ConfirmationMethod != InferredPersonnel
        )
            return Result.Success(true);

        User? user = await db.Users.FirstOrDefaultAsync(u => u.Id == viewerUserId, ct);
        if (user is null)
            return Result.Success(false); // fail-closed

        if (
            user.AccountCreatedAt is DateTime created
            && (clock.GetUtcNow().UtcDateTime - created).TotalDays >= Age18AccountYears * 365.25
        )
        {
            await MaterializeInferenceAsync(cache, broadcasterId, user, InferredAccountAge, ct);
            return Result.Success(true);
        }

        if (PersonnelTypes.Contains(user.Type))
        {
            await MaterializeInferenceAsync(cache, broadcasterId, user, InferredPersonnel, ct);
            return Result.Success(true);
        }

        return Result.Success(false); // fail-closed
    }

    public async Task<Result<AgeConsentDto>> GrantAsync(
        Guid broadcasterId,
        GrantAgeConsentRequest request,
        CancellationToken ct = default
    )
    {
        User? user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.ViewerUserId, ct);
        if (user is null)
            return Result.Failure<AgeConsentDto>("Viewer not found.", "NOT_FOUND");
        DateTime now = clock.GetUtcNow().UtcDateTime;

        ConsentRecord record = await UpsertConsentRecordAsync(broadcasterId, request, now, ct);

        ViewerAgeConsent? cache = await FindCacheAsync(
            broadcasterId,
            request.ViewerUserId,
            ct,
            includeDeleted: true
        );
        if (cache is null)
        {
            cache = new ViewerAgeConsent
            {
                BroadcasterId = broadcasterId,
                ViewerUserId = request.ViewerUserId,
                ViewerTwitchUserId = user.TwitchUserId!,
                ConsentRecordId = record.Id,
                Granted = true,
                ConfirmedAt = now,
                ConfirmationMethod = SelfConfirm,
            };
            db.ViewerAgeConsents.Add(cache);
        }
        else
        {
            cache.ConsentRecordId = record.Id;
            cache.Granted = true;
            cache.ConfirmedAt = now;
            cache.RevokedAt = null;
            cache.DeletedAt = null;
            cache.ConfirmationMethod = SelfConfirm;
        }
        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(
            new AgeConsentGrantedEvent
            {
                BroadcasterId = broadcasterId,
                ViewerUserId = request.ViewerUserId,
                ConsentRecordId = record.Id,
                ConfirmationMethod = SelfConfirm,
            },
            ct
        );
        return Result.Success(ToDto(cache));
    }

    public async Task<Result<AgeConsentDto>> RevokeAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CancellationToken ct = default
    )
    {
        ViewerAgeConsent? cache = await FindCacheAsync(broadcasterId, viewerUserId, ct);
        if (cache is null)
            return Result.Failure<AgeConsentDto>("No consent on record.", "NOT_FOUND");
        DateTime now = clock.GetUtcNow().UtcDateTime;
        cache.Granted = false;
        cache.RevokedAt = now;

        ConsentRecord? record = await db
            .ConsentRecords.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c =>
                    c.BroadcasterId == broadcasterId
                    && c.SubjectUserId == viewerUserId
                    && c.ConsentType == ConsentType,
                ct
            );
        if (record is not null)
        {
            record.Status = "withdrawn";
            record.WithdrawnAt = now;
        }
        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(
            new AgeConsentRevokedEvent
            {
                BroadcasterId = broadcasterId,
                ViewerUserId = viewerUserId,
                ConsentRecordId = cache.ConsentRecordId,
            },
            ct
        );
        return Result.Success(ToDto(cache));
    }

    private async Task<ConsentRecord> UpsertConsentRecordAsync(
        Guid broadcasterId,
        GrantAgeConsentRequest request,
        DateTime now,
        CancellationToken ct
    )
    {
        ConsentRecord? record = await db
            .ConsentRecords.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c =>
                    c.BroadcasterId == broadcasterId
                    && c.SubjectUserId == request.ViewerUserId
                    && c.ConsentType == ConsentType,
                ct
            );
        if (record is null)
        {
            record = new ConsentRecord
            {
                BroadcasterId = broadcasterId,
                SubjectUserId = request.ViewerUserId,
                SubjectIdHash = HashSubject(request.ViewerUserId),
                ConsentType = ConsentType,
                Status = "granted",
                LawfulBasis = "consent",
                ConsentVersion = request.ConsentVersion,
                Source = request.ConfirmationMethod,
                GrantedAt = now,
            };
            db.ConsentRecords.Add(record);
        }
        else
        {
            record.Status = "granted";
            record.LawfulBasis = "consent";
            record.GrantedAt = now;
            record.WithdrawnAt = null;
            record.DeletedAt = null;
            record.ConsentVersion = request.ConsentVersion;
        }
        return record;
    }

    private async Task MaterializeInferenceAsync(
        ViewerAgeConsent? cache,
        Guid broadcasterId,
        User user,
        string method,
        CancellationToken ct
    )
    {
        if (cache is { Granted: true, RevokedAt: null } && cache.ConfirmationMethod == method)
            return; // idempotent — already recorded

        if (cache is null)
        {
            db.ViewerAgeConsents.Add(
                new ViewerAgeConsent
                {
                    BroadcasterId = broadcasterId,
                    ViewerUserId = user.Id,
                    ViewerTwitchUserId = user.TwitchUserId!,
                    ConsentRecordId = Guid.Empty, // an inference is not backed by a consent record
                    Granted = true,
                    ConfirmedAt = clock.GetUtcNow().UtcDateTime,
                    ConfirmationMethod = method,
                }
            );
        }
        else
        {
            cache.Granted = true;
            cache.RevokedAt = null;
            cache.DeletedAt = null;
            cache.ConsentRecordId = Guid.Empty;
            cache.ConfirmationMethod = method;
            cache.ConfirmedAt = clock.GetUtcNow().UtcDateTime;
        }
        await db.SaveChangesAsync(ct);
    }

    private Task<ViewerAgeConsent?> FindCacheAsync(
        Guid broadcasterId,
        Guid viewerUserId,
        CancellationToken ct,
        bool includeDeleted = false
    )
    {
        IQueryable<ViewerAgeConsent> query = includeDeleted
            ? db.ViewerAgeConsents.IgnoreQueryFilters()
            : db.ViewerAgeConsents;
        return query.FirstOrDefaultAsync(
            c =>
                c.BroadcasterId == broadcasterId
                && c.ViewerUserId == viewerUserId
                && (includeDeleted || c.DeletedAt == null),
            ct
        );
    }

    private static string HashSubject(Guid viewerUserId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(viewerUserId.ToString())));

    private static AgeConsentDto ToDto(ViewerAgeConsent c) =>
        new(
            c.ViewerUserId,
            c.ConsentRecordId,
            c.Granted,
            c.ConfirmedAt,
            c.RevokedAt,
            c.ConfirmationMethod
        );
}
