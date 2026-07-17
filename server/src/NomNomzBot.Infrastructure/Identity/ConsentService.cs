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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Gdpr;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// The consent / lawful-basis ledger over <see cref="ConsentRecord"/> (gdpr-crypto.md §3.6): one active row
/// per <c>(BroadcasterId, SubjectUserId, ConsentType)</c>, latest-wins (uniqueness enforced here, not as a DB
/// constraint, because the table is soft-deletable). Proof-of-consent IP is deliberately not sealed —
/// <c>ConsentRecord.IpAddressCipher</c> is unused by design per the entity's own doc-comment.
/// </summary>
public sealed class ConsentService : IConsentService
{
    /// <summary>The closed consent-type vocabulary (gdpr-crypto.md O.5 [VC:enum]).</summary>
    private static readonly IReadOnlySet<string> ConsentTypes = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "tos_privacy",
        "age_18_gambling",
        "pronoun_special_category",
        "leaderboard_opt_in",
        "marketing",
    };

    private static readonly IReadOnlySet<string> LawfulBases = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "consent",
        "contract",
        "legitimate_interest",
    };

    private readonly IApplicationDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ConsentService> _logger;

    public ConsentService(
        IApplicationDbContext db,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<ConsentService> logger
    )
    {
        _db = db;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<ConsentRecordDto>> GrantAsync(
        GrantConsentRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!ConsentTypes.Contains(request.ConsentType))
            return Result.Failure<ConsentRecordDto>(
                $"Unknown consent type '{request.ConsentType}'.",
                "VALIDATION_FAILED"
            );
        if (!LawfulBases.Contains(request.LawfulBasis))
            return Result.Failure<ConsentRecordDto>(
                $"Unknown lawful basis '{request.LawfulBasis}'.",
                "VALIDATION_FAILED"
            );

        bool subjectExists = await _db.Users.AnyAsync(
            u => u.Id == request.SubjectUserId,
            cancellationToken
        );
        if (!subjectExists)
            return Result.Failure<ConsentRecordDto>("The subject was not found.", "NOT_FOUND");

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        ConsentRecord? record = await FindRowAsync(
            request.SubjectUserId,
            request.BroadcasterId,
            request.ConsentType,
            cancellationToken
        );

        if (record is null)
        {
            record = new ConsentRecord
            {
                BroadcasterId = request.BroadcasterId,
                SubjectUserId = request.SubjectUserId,
                SubjectIdHash = ComputeSubjectIdHash(request.SubjectUserId),
                ConsentType = request.ConsentType,
            };
            _db.ConsentRecords.Add(record);
        }

        record.Status = "granted";
        record.LawfulBasis = request.LawfulBasis;
        record.ConsentVersion = request.ConsentVersion;
        record.Source = request.Source;
        record.GrantedAt = now;
        record.WithdrawnAt = null;
        record.ExpiresAt = null;

        await _db.SaveChangesAsync(cancellationToken);

        await PublishConsentChangedAsync(record, cancellationToken);
        _logger.LogInformation(
            "Consent {ConsentType} granted for subject hash {SubjectIdHash}.",
            record.ConsentType,
            record.SubjectIdHash
        );
        return Result.Success(ToDto(record));
    }

    public async Task<Result> WithdrawAsync(
        Guid subjectUserId,
        Guid? broadcasterId,
        string consentType,
        CancellationToken cancellationToken = default
    )
    {
        ConsentRecord? record = await FindRowAsync(
            subjectUserId,
            broadcasterId,
            consentType,
            cancellationToken
        );
        if (record is null || record.Status != "granted" || record.WithdrawnAt is not null)
            return Result.Failure("No active consent of that type exists.", "NOT_FOUND");

        record.Status = "withdrawn";
        record.WithdrawnAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken);

        await PublishConsentChangedAsync(record, cancellationToken);
        _logger.LogInformation(
            "Consent {ConsentType} withdrawn for subject hash {SubjectIdHash}.",
            record.ConsentType,
            record.SubjectIdHash
        );
        return Result.Success();
    }

    public async Task<Result<bool>> HasActiveConsentAsync(
        Guid subjectUserId,
        Guid? broadcasterId,
        string consentType,
        CancellationToken cancellationToken = default
    )
    {
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        bool active = await _db.ConsentRecords.AnyAsync(
            r =>
                r.SubjectUserId == subjectUserId
                && r.BroadcasterId == broadcasterId
                && r.ConsentType == consentType
                && r.Status == "granted"
                && r.WithdrawnAt == null
                && (r.ExpiresAt == null || r.ExpiresAt > now),
            cancellationToken
        );
        return Result.Success(active);
    }

    public async Task<Result<IReadOnlyList<ConsentRecordDto>>> ListForSubjectAsync(
        Guid subjectUserId,
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        IQueryable<ConsentRecord> query = _db
            .ConsentRecords.AsNoTracking()
            .Where(r => r.SubjectUserId == subjectUserId);

        // A channel context returns that channel's rows plus platform-wide (null-broadcaster) rows; no
        // context returns everything — the my-data page shows the subject's whole ledger.
        if (broadcasterId is not null)
            query = query.Where(r => r.BroadcasterId == broadcasterId || r.BroadcasterId == null);

        List<ConsentRecord> records = await query
            .OrderBy(r => r.ConsentType)
            .ThenBy(r => r.BroadcasterId)
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<ConsentRecordDto>>([.. records.Select(ToDto)]);
    }

    /// <summary>The single (soft-delete-filtered) row for the ledger key — latest-wins upsert target.</summary>
    private Task<ConsentRecord?> FindRowAsync(
        Guid subjectUserId,
        Guid? broadcasterId,
        string consentType,
        CancellationToken cancellationToken
    ) =>
        _db.ConsentRecords.FirstOrDefaultAsync(
            r =>
                r.SubjectUserId == subjectUserId
                && r.BroadcasterId == broadcasterId
                && r.ConsentType == consentType,
            cancellationToken
        );

    private Task PublishConsentChangedAsync(
        ConsentRecord record,
        CancellationToken cancellationToken
    ) =>
        _eventBus.PublishAsync(
            new ConsentChangedEvent
            {
                BroadcasterId = record.BroadcasterId ?? Guid.Empty,
                ConsentRecordId = record.Id,
                SubjectUserId = record.SubjectUserId,
                SubjectIdHash = record.SubjectIdHash,
                ConsentType = record.ConsentType,
                Status = record.Status,
                LawfulBasis = record.LawfulBasis,
                ConsentVersion = record.ConsentVersion,
                OccurredAt = _timeProvider.GetUtcNow(),
            },
            cancellationToken
        );

    /// <summary>
    /// The deterministic subject hash shared across the GDPR tables (same construction as
    /// <c>AgeConsentService</c>): SHA-256 hex of the internal user id, 64 chars, joins stay linked post-erasure.
    /// </summary>
    internal static string ComputeSubjectIdHash(Guid subjectUserId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subjectUserId.ToString())));

    private static ConsentRecordDto ToDto(ConsentRecord r) =>
        new(
            r.Id,
            r.BroadcasterId,
            r.SubjectUserId,
            r.ConsentType,
            r.Status,
            r.LawfulBasis,
            r.ConsentVersion,
            r.GrantedAt,
            r.WithdrawnAt,
            r.ExpiresAt
        );
}
