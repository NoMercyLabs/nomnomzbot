// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Gdpr;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// The GDPR erasure / export / opt-out orchestrator (gdpr-crypto.md §3.7, §5) — supersedes the legacy
/// <c>GdprService</c>, porting its proven mechanics (profile anonymization, cross-channel chat / records /
/// viewer-data scrub, vaulted-OAuth revocation through <see cref="IIntegrationTokenVault"/>) and adding the
/// crypto-shred (<see cref="ISubjectKeyService.DestroyKeyAsync"/>), auth-session revocation + residual-IP
/// scrub, consent withdrawal, and the <see cref="ErasureRequest"/> + <see cref="ComplianceAuditLog"/> ledger.
///
/// <para><b>Two-phase write semantics.</b> The <see cref="ErasureRequest"/> row is persisted in its OWN save
/// before the pipeline transaction begins — the request record must survive any pipeline rollback. The
/// destructive pipeline then runs inside one <see cref="IUnitOfWork"/> transaction (collaborators like the
/// token vault and the DEK store save mid-pipeline on the same scoped context, so their writes join the same
/// transaction). On failure the transaction rolls back, the change tracker is cleared (so the rolled-back
/// mutations cannot be silently re-flushed), and a third, separate save stamps <c>Status=failed</c> +
/// the <c>Outcome=failed</c> audit row onto the surviving request.</para>
/// </summary>
public sealed class ErasureService : IErasureService
{
    private static readonly IReadOnlySet<string> RequestedByValues = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "self_service",
        "broadcaster",
        "platform_iam",
    };

    private static readonly IReadOnlySet<string> ScopeValues = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "deployment",
        "instance",
        "channel",
    };

    /// <summary>Consent types an opt-out withdraws (legitimate-interest processing, gdpr-crypto.md §3.7).</summary>
    private static readonly IReadOnlyList<string> OptOutConsentTypes =
    [
        "marketing",
        "leaderboard_opt_in",
    ];

    private readonly IApplicationDbContext _db;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IIntegrationTokenVault _vault;
    private readonly ISubjectKeyService _subjectKeys;
    private readonly IConsentService _consents;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ErasureService> _logger;

    public ErasureService(
        IApplicationDbContext db,
        IUnitOfWork unitOfWork,
        IIntegrationTokenVault vault,
        ISubjectKeyService subjectKeys,
        IConsentService consents,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<ErasureService> logger
    )
    {
        _db = db;
        _unitOfWork = unitOfWork;
        _vault = vault;
        _subjectKeys = subjectKeys;
        _consents = consents;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // ── Erasure ────────────────────────────────────────────────────────────────

    public async Task<Result<ErasureRequestDto>> RequestErasureAsync(
        RequestErasureRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!RequestedByValues.Contains(request.RequestedBy))
            return Result.Failure<ErasureRequestDto>(
                $"Unknown requester kind '{request.RequestedBy}'.",
                "VALIDATION_FAILED"
            );
        string scope = string.IsNullOrWhiteSpace(request.Scope) ? "deployment" : request.Scope;
        if (!ScopeValues.Contains(scope))
            return Result.Failure<ErasureRequestDto>(
                $"Unknown erasure scope '{scope}'.",
                "VALIDATION_FAILED"
            );

        User? user = await _db.Users.FirstOrDefaultAsync(
            u => u.Id == request.SubjectUserId,
            cancellationToken
        );
        if (user is null)
            return Result.Failure<ErasureRequestDto>("The subject was not found.", "NOT_FOUND");

        string subjectIdHash = ConsentService.ComputeSubjectIdHash(user.Id);
        ErasureRequest erasureRequest = await CreateRequestRowAsync(
            user,
            subjectIdHash,
            request.BroadcasterId,
            requestType: "erasure",
            request.RequestedBy,
            scope,
            cancellationToken
        );

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            Result<ErasurePipelineOutcome> pipeline = await ExecuteErasurePipelineAsync(
                user,
                erasureRequest,
                subjectIdHash,
                cancellationToken
            );
            if (pipeline.IsFailure)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                return await MarkRequestFailedAsync(
                    erasureRequest.Id,
                    user.Id,
                    subjectIdHash,
                    request.BroadcasterId,
                    request.RequestedBy,
                    requestTypeForAudit: "erasure",
                    pipeline.ErrorMessage ?? "The erasure pipeline failed.",
                    cancellationToken
                );
            }

            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            ErasurePipelineOutcome outcome = pipeline.Value;
            await _eventBus.PublishAsync(
                new SubjectErasureCompletedEvent
                {
                    BroadcasterId = request.BroadcasterId ?? Guid.Empty,
                    ErasureRequestId = erasureRequest.Id,
                    SubjectUserId = user.Id,
                    SubjectIdHash = subjectIdHash,
                    CryptoShredApplied = erasureRequest.CryptoShredApplied,
                    AnonymizationApplied = erasureRequest.AnonymizationApplied,
                    KeysShredded = outcome.KeysShredded,
                    RowsAffected = outcome.Report.RowsAffected,
                    OccurredAt = _timeProvider.GetUtcNow(),
                },
                cancellationToken
            );
            _logger.LogInformation(
                "GDPR erasure {ErasureRequestId} completed for subject hash {SubjectIdHash}: "
                    + "{RowsAffected} rows across {TableCount} tables, {KeysShredded} DEK(s) shredded.",
                erasureRequest.Id,
                subjectIdHash,
                outcome.Report.RowsAffected,
                outcome.Report.TablesAffectedCount,
                outcome.KeysShredded
            );
            return Result.Success(ToDto(erasureRequest));
        }
        catch (Exception exception)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(
                exception,
                "GDPR erasure {ErasureRequestId} failed for subject hash {SubjectIdHash}.",
                erasureRequest.Id,
                subjectIdHash
            );
            return await MarkRequestFailedAsync(
                erasureRequest.Id,
                user.Id,
                subjectIdHash,
                request.BroadcasterId,
                request.RequestedBy,
                requestTypeForAudit: "erasure",
                exception.Message,
                cancellationToken
            );
        }
    }

    /// <summary>
    /// Schema §5 steps inside the ambient transaction: anonymize → scrub → revoke tokens/auth → withdraw
    /// consent → crypto-shred → audit. Marks the request completed and writes the audit row; the caller owns
    /// commit/rollback.
    /// </summary>
    private async Task<Result<ErasurePipelineOutcome>> ExecuteErasurePipelineAsync(
        User user,
        ErasureRequest erasureRequest,
        string subjectIdHash,
        CancellationToken cancellationToken
    )
    {
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        List<string> tablesAffected = [];
        int rowsAffected = 0;

        // 1. Anonymize the profile in place (preserves referential integrity — FKs all point at Users.Id).
        //    Idempotent: an already-anonymized subject is left as-is and counts zero rows.
        if (!user.IsAnonymized)
        {
            string deletedName = $"deleted_{user.Id:N}";
            user.Username = deletedName;
            user.UsernameNormalized = deletedName; // indexed lookup column — must not leak the original login
            user.DisplayName = "Deleted User";
            user.NickName = null;
            user.ProfileImageUrl = null;
            user.OfflineImageUrl = null;
            user.Description = null;
            user.Color = null;
            user.EmailCipher = null;
            user.PronounId = null; // [PII-S9] special category
            user.AltPronounId = null;
            user.Enabled = false;
            user.IsAnonymized = true;
            tablesAffected.Add("Users");
            rowsAffected += 1;
        }

        // 2. Hard delete: chat messages (erasure is a hard delete, not a soft delete).
        List<ChatMessage> messages = await _db
            .ChatMessages.Where(m => m.UserId == user.Id.ToString())
            .ToListAsync(cancellationToken);
        _db.ChatMessages.RemoveRange(messages);
        CountStep(tablesAffected, ref rowsAffected, "ChatMessages", messages.Count);

        // 3. Hard delete: records (redemption/moderation history rows keyed by the user).
        List<Record> records = await _db
            .Records.Where(r => r.UserId == user.Id.ToString())
            .ToListAsync(cancellationToken);
        _db.Records.RemoveRange(records);
        CountStep(tablesAffected, ref rowsAffected, "Records", records.Count);

        // 4. Hard delete: per-viewer custom data (G.14) across EVERY channel — erasure ignores the
        //    soft-delete filter so already-soft-deleted rows are scrubbed too.
        List<Domain.ViewerData.Entities.ViewerDatum> viewerData = await _db
            .ViewerData.IgnoreQueryFilters()
            .Where(d => d.ViewerUserId == user.Id)
            .ToListAsync(cancellationToken);
        _db.ViewerData.RemoveRange(viewerData);
        CountStep(tablesAffected, ref rowsAffected, "ViewerData", viewerData.Count);

        // 5. Hard delete: legacy service tokens (Service.UserId stores the external Twitch user id).
        List<Service> services = await _db
            .Services.Where(s => s.UserId == user.TwitchUserId)
            .ToListAsync(cancellationToken);
        _db.Services.RemoveRange(services);
        CountStep(tablesAffected, ref rowsAffected, "Services", services.Count);

        // 6. Revoke the subject's vaulted OAuth connections — the REAL token store. RevokeConnectionAsync
        //    soft-deletes the IntegrationToken ciphertext and flips the connection to revoked.
        List<Guid> connectionIds = await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c => c.ConnectedByUserId == user.Id && c.DeletedAt == null)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        foreach (Guid connectionId in connectionIds)
        {
            Result revoked = await _vault.RevokeConnectionAsync(
                connectionId,
                "gdpr_erasure",
                cancellationToken
            );
            if (revoked.IsFailure)
                return revoked.ToTyped<ErasurePipelineOutcome>();
        }
        CountStep(tablesAffected, ref rowsAffected, "IntegrationConnections", connectionIds.Count);

        // 7. Revoke auth: refresh tokens + sessions by UserId, and scrub the residual sealed IP (§5 step 4).
        List<RefreshToken> refreshTokens = await _db
            .RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (RefreshToken token in refreshTokens)
        {
            token.RevokedAt = now;
            token.RevokedReason = "gdpr_erasure";
        }
        CountStep(tablesAffected, ref rowsAffected, "RefreshTokens", refreshTokens.Count);

        List<AuthSession> sessions = await _db
            .AuthSessions.Where(s => s.UserId == user.Id)
            .ToListAsync(cancellationToken);
        foreach (AuthSession session in sessions)
        {
            session.RevokedAt ??= now;
            session.IpAddressCipher = null;
        }
        CountStep(tablesAffected, ref rowsAffected, "AuthSessions", sessions.Count);

        // 8. Withdraw every active consent (erasure step 5, via the §3.6 ledger so ConsentChanged fires).
        List<ConsentRecord> activeConsents = await _db
            .ConsentRecords.Where(r =>
                r.SubjectUserId == user.Id && r.Status == "granted" && r.WithdrawnAt == null
            )
            .ToListAsync(cancellationToken);
        foreach (ConsentRecord consent in activeConsents)
        {
            Result withdrawn = await _consents.WithdrawAsync(
                user.Id,
                consent.BroadcasterId,
                consent.ConsentType,
                cancellationToken
            );
            if (withdrawn.IsFailure)
                return withdrawn.ToTyped<ErasurePipelineOutcome>();
        }
        CountStep(tablesAffected, ref rowsAffected, "ConsentRecords", activeConsents.Count);

        // 9. CRYPTO-SHRED (O(1)): destroy the subject's DEK(s) — the FK'd Users.SubjectKeyId plus any
        //    subject-scope key registered under the subject's hash. Every ciphertext sealed under them
        //    (backups included) becomes permanently unreadable.
        List<Guid> keyIds = await _db
            .CryptoKeys.Where(k =>
                k.KeyScope == "subject" && k.SubjectIdHash == subjectIdHash && k.Status == "active"
            )
            .Select(k => k.Id)
            .ToListAsync(cancellationToken);
        if (user.SubjectKeyId is Guid subjectKeyId && !keyIds.Contains(subjectKeyId))
        {
            bool stillActive = await _db.CryptoKeys.AnyAsync(
                k => k.Id == subjectKeyId && k.Status == "active",
                cancellationToken
            );
            if (stillActive)
                keyIds.Add(subjectKeyId);
        }
        foreach (Guid keyId in keyIds)
        {
            Result destroyed = await _subjectKeys.DestroyKeyAsync(
                keyId,
                erasureRequest.Id,
                cancellationToken
            );
            if (destroyed.IsFailure)
                return destroyed.ToTyped<ErasurePipelineOutcome>();
        }
        int keysShredded = keyIds.Count;
        CountStep(tablesAffected, ref rowsAffected, "CryptoKeys", keysShredded);

        // 10. Complete the request + append the audit row — inside the same transaction, so a completed
        //     status can never exist without its audit trail (and vice versa).
        AnonymizationReport report = new(tablesAffected.Count, rowsAffected, tablesAffected);
        erasureRequest.Status = "completed";
        erasureRequest.CryptoShredApplied = keysShredded > 0;
        erasureRequest.AnonymizationApplied = true;
        erasureRequest.RowsAffected = rowsAffected;
        erasureRequest.ReportJson = JsonConvert.SerializeObject(report);
        erasureRequest.CompletedAt = now;

        _db.ComplianceAuditLogs.Add(
            BuildAuditRow(
                requestType: "erasure",
                erasureRequest.Id,
                subjectIdHash,
                erasureRequest.BroadcasterId,
                erasureRequest.RequestedBy,
                tablesAffected,
                rowsAffected,
                keysShredded,
                outcome: "completed",
                now
            )
        );

        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(new ErasurePipelineOutcome(report, keysShredded));
    }

    // ── Export ─────────────────────────────────────────────────────────────────

    public async Task<Result<DataExportDto>> RequestExportAsync(
        RequestExportRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!RequestedByValues.Contains(request.RequestedBy))
            return Result.Failure<DataExportDto>(
                $"Unknown requester kind '{request.RequestedBy}'.",
                "VALIDATION_FAILED"
            );

        User? user = await _db
            .Users.Include(u => u.Pronoun)
            .FirstOrDefaultAsync(u => u.Id == request.SubjectUserId, cancellationToken);
        if (user is null)
            return Result.Failure<DataExportDto>("The subject was not found.", "NOT_FOUND");

        string subjectIdHash = ConsentService.ComputeSubjectIdHash(user.Id);
        ErasureRequest erasureRequest = await CreateRequestRowAsync(
            user,
            subjectIdHash,
            request.BroadcasterId,
            requestType: "export",
            request.RequestedBy,
            scope: "deployment",
            cancellationToken
        );

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

        var chatMessages = await _db
            .ChatMessages.Where(m => m.UserId == user.Id.ToString())
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new
            {
                m.Id,
                m.BroadcasterId,
                m.Message,
                m.MessageType,
                m.CreatedAt,
            })
            .Take(10_000)
            .ToListAsync(cancellationToken);

        var records = await _db
            .Records.Where(r => r.UserId == user.Id.ToString())
            .Select(r => new
            {
                r.BroadcasterId,
                r.RecordType,
                r.Data,
                r.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        // Service.UserId stores the external Twitch user id (not the internal key).
        var services = await _db
            .Services.Where(s => s.UserId == user.TwitchUserId)
            .Select(s => new
            {
                s.Name,
                s.BroadcasterId,
                s.Scopes,
                s.TokenExpiry,
            })
            .ToListAsync(cancellationToken);

        // The vaulted OAuth connections the subject established — provider/status/scopes only, never ciphertext.
        var connections = await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c => c.ConnectedByUserId == user.Id && c.DeletedAt == null)
            .Select(c => new
            {
                c.Provider,
                c.ProviderAccountName,
                c.Status,
                c.Scopes,
                c.ConnectedAt,
            })
            .ToListAsync(cancellationToken);

        var consents = await _db
            .ConsentRecords.AsNoTracking()
            .Where(r => r.SubjectUserId == user.Id)
            .Select(r => new
            {
                r.BroadcasterId,
                r.ConsentType,
                r.Status,
                r.LawfulBasis,
                r.GrantedAt,
                r.WithdrawnAt,
            })
            .ToListAsync(cancellationToken);

        var export = new
        {
            ExportedAt = now,
            ExportedForUserId = user.Id,
            Profile = new
            {
                user.Id,
                user.Username,
                user.DisplayName,
                user.ProfileImageUrl,
                user.BroadcasterType,
                Pronoun = user.Pronoun?.Name,
                user.CreatedAt,
                user.UpdatedAt,
            },
            ChatMessages = chatMessages,
            Records = records,
            ConnectedServices = services,
            Connections = connections,
            Consents = consents,
        };

        // Newtonsoft is the app JSON convention (gdpr-crypto.md §8) — the export document included.
        string document = JsonConvert.SerializeObject(export, Formatting.Indented);
        int rowsAffected =
            1
            + chatMessages.Count
            + records.Count
            + services.Count
            + connections.Count
            + consents.Count;
        string exportLocation = $"gdpr/requests/{erasureRequest.Id}/export";

        erasureRequest.Status = "completed";
        erasureRequest.RowsAffected = rowsAffected;
        erasureRequest.ExportLocation = exportLocation;
        erasureRequest.ExportFormat = "json";
        erasureRequest.CompletedAt = now;
        _db.ComplianceAuditLogs.Add(
            BuildAuditRow(
                requestType: "export",
                erasureRequest.Id,
                subjectIdHash,
                request.BroadcasterId,
                request.RequestedBy,
                tablesAffected:
                [
                    "Users",
                    "ChatMessages",
                    "Records",
                    "Services",
                    "IntegrationConnections",
                    "ConsentRecords",
                ],
                rowsAffected,
                keysShredded: 0,
                outcome: "completed",
                now
            )
        );
        await _db.SaveChangesAsync(cancellationToken);

        await _eventBus.PublishAsync(
            new SubjectDataExportedEvent
            {
                BroadcasterId = request.BroadcasterId ?? Guid.Empty,
                ErasureRequestId = erasureRequest.Id,
                SubjectUserId = user.Id,
                SubjectIdHash = subjectIdHash,
                ExportFormat = "json",
                ExportLocation = exportLocation,
                RowsAffected = rowsAffected,
                OccurredAt = _timeProvider.GetUtcNow(),
            },
            cancellationToken
        );
        _logger.LogInformation(
            "GDPR export {ErasureRequestId} produced for subject hash {SubjectIdHash} ({RowsAffected} rows).",
            erasureRequest.Id,
            subjectIdHash,
            rowsAffected
        );

        return Result.Success(
            new DataExportDto(
                erasureRequest.Id,
                "json",
                exportLocation,
                System.Text.Encoding.UTF8.GetByteCount(document),
                rowsAffected,
                now,
                document
            )
        );
    }

    // ── Opt-out ────────────────────────────────────────────────────────────────

    public async Task<Result<ErasureRequestDto>> RequestOptOutAsync(
        RequestOptOutRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!RequestedByValues.Contains(request.RequestedBy))
            return Result.Failure<ErasureRequestDto>(
                $"Unknown requester kind '{request.RequestedBy}'.",
                "VALIDATION_FAILED"
            );

        User? user = await _db.Users.FirstOrDefaultAsync(
            u => u.Id == request.SubjectUserId,
            cancellationToken
        );
        if (user is null)
            return Result.Failure<ErasureRequestDto>("The subject was not found.", "NOT_FOUND");

        string subjectIdHash = ConsentService.ComputeSubjectIdHash(user.Id);
        ErasureRequest erasureRequest = await CreateRequestRowAsync(
            user,
            subjectIdHash,
            request.BroadcasterId,
            requestType: "opt_out",
            request.RequestedBy,
            scope: request.BroadcasterId is null ? "deployment" : "channel",
            cancellationToken
        );

        try
        {
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
            List<string> tablesAffected = [];
            int rowsAffected = 0;

            // Withdraw the legitimate-interest consents (marketing + leaderboard) via the §3.6 ledger.
            List<ConsentRecord> activeConsents = await _db
                .ConsentRecords.Where(r =>
                    r.SubjectUserId == user.Id
                    && r.Status == "granted"
                    && r.WithdrawnAt == null
                    && OptOutConsentTypes.Contains(r.ConsentType)
                    && (request.BroadcasterId == null || r.BroadcasterId == request.BroadcasterId)
                )
                .ToListAsync(cancellationToken);
            foreach (ConsentRecord consent in activeConsents)
            {
                Result withdrawn = await _consents.WithdrawAsync(
                    user.Id,
                    consent.BroadcasterId,
                    consent.ConsentType,
                    cancellationToken
                );
                if (withdrawn.IsFailure)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return await MarkRequestFailedAsync(
                        erasureRequest.Id,
                        user.Id,
                        subjectIdHash,
                        request.BroadcasterId,
                        request.RequestedBy,
                        requestTypeForAudit: "consent_change",
                        withdrawn.ErrorMessage ?? "Consent withdrawal failed.",
                        cancellationToken
                    );
                }
            }
            CountStep(tablesAffected, ref rowsAffected, "ConsentRecords", activeConsents.Count);

            // Flag analytics processing opt-out on the subject's viewer profiles — a cross-tenant write, so
            // the tenant filter is bypassed and the soft-delete predicate re-applied by hand.
            List<Domain.Analytics.Entities.ViewerProfile> profiles = await _db
                .ViewerProfiles.IgnoreQueryFilters()
                .Where(p =>
                    p.ViewerUserId == user.Id
                    && p.DeletedAt == null
                    && (request.BroadcasterId == null || p.BroadcasterId == request.BroadcasterId)
                    && !p.IsAnalyticsOptedOut
                )
                .ToListAsync(cancellationToken);
            foreach (Domain.Analytics.Entities.ViewerProfile profile in profiles)
                profile.IsAnalyticsOptedOut = true;
            CountStep(tablesAffected, ref rowsAffected, "ViewerProfiles", profiles.Count);

            erasureRequest.Status = "completed";
            erasureRequest.RowsAffected = rowsAffected;
            erasureRequest.CompletedAt = now;
            _db.ComplianceAuditLogs.Add(
                BuildAuditRow(
                    requestType: "consent_change",
                    erasureRequest.Id,
                    subjectIdHash,
                    request.BroadcasterId,
                    request.RequestedBy,
                    tablesAffected,
                    rowsAffected,
                    keysShredded: 0,
                    outcome: "completed",
                    now
                )
            );
            await _db.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation(
                "GDPR opt-out {ErasureRequestId} completed for subject hash {SubjectIdHash} ({RowsAffected} rows).",
                erasureRequest.Id,
                subjectIdHash,
                rowsAffected
            );
            return Result.Success(ToDto(erasureRequest));
        }
        catch (Exception exception)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(
                exception,
                "GDPR opt-out {ErasureRequestId} failed for subject hash {SubjectIdHash}.",
                erasureRequest.Id,
                subjectIdHash
            );
            return await MarkRequestFailedAsync(
                erasureRequest.Id,
                user.Id,
                subjectIdHash,
                request.BroadcasterId,
                request.RequestedBy,
                requestTypeForAudit: "consent_change",
                exception.Message,
                cancellationToken
            );
        }
    }

    // ── Reads ──────────────────────────────────────────────────────────────────

    public async Task<Result<ErasureRequestDto>> GetRequestAsync(
        Guid erasureRequestId,
        CancellationToken cancellationToken = default
    )
    {
        ErasureRequest? request = await _db
            .ErasureRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == erasureRequestId, cancellationToken);
        return request is null
            ? Result.Failure<ErasureRequestDto>("The request was not found.", "NOT_FOUND")
            : Result.Success(ToDto(request));
    }

    public async Task<Result<PagedList<ErasureRequestDto>>> ListRequestsAsync(
        PaginationParams pagination,
        Guid? subjectUserId,
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        IQueryable<ErasureRequest> query = _db.ErasureRequests.AsNoTracking();
        if (subjectUserId is not null)
            query = query.Where(r => r.SubjectUserId == subjectUserId);
        if (broadcasterId is not null)
            query = query.Where(r => r.BroadcasterId == broadcasterId);

        int total = await query.CountAsync(cancellationToken);
        List<ErasureRequest> page = await query
            .OrderByDescending(r => r.RequestedAt)
            .ThenByDescending(r => r.Id)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        return Result.Success(
            new PagedList<ErasureRequestDto>(
                [.. page.Select(ToDto)],
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    // ── Shared plumbing ────────────────────────────────────────────────────────

    /// <summary>
    /// Phase 1: persists the request row in its own save (before any transaction) and emits
    /// <see cref="SubjectErasureRequestedEvent"/> — the record that survives a pipeline rollback.
    /// </summary>
    private async Task<ErasureRequest> CreateRequestRowAsync(
        User user,
        string subjectIdHash,
        Guid? broadcasterId,
        string requestType,
        string requestedBy,
        string scope,
        CancellationToken cancellationToken
    )
    {
        ErasureRequest request = new()
        {
            SubjectUserId = user.Id,
            SubjectKeyId = user.SubjectKeyId,
            SubjectIdHash = subjectIdHash,
            BroadcasterId = broadcasterId,
            RequestType = requestType,
            RequestedBy = requestedBy,
            Status = "running",
            Scope = scope,
            RequestedAt = _timeProvider.GetUtcNow().UtcDateTime,
        };
        _db.ErasureRequests.Add(request);
        await _db.SaveChangesAsync(cancellationToken);

        await _eventBus.PublishAsync(
            new SubjectErasureRequestedEvent
            {
                BroadcasterId = broadcasterId ?? Guid.Empty,
                ErasureRequestId = request.Id,
                SubjectUserId = user.Id,
                SubjectIdHash = subjectIdHash,
                RequestType = requestType,
                RequestedBy = requestedBy,
                Scope = scope,
                OccurredAt = _timeProvider.GetUtcNow(),
            },
            cancellationToken
        );
        return request;
    }

    /// <summary>
    /// Phase 3 (failure path): after the rollback, drops the rolled-back mutations from the change tracker
    /// (every <see cref="IApplicationDbContext"/> in this app IS a DbContext), re-reads the surviving request
    /// row, and stamps <c>failed</c> + the <c>Outcome=failed</c> audit row in a separate save.
    /// </summary>
    private async Task<Result<ErasureRequestDto>> MarkRequestFailedAsync(
        Guid erasureRequestId,
        Guid subjectUserId,
        string subjectIdHash,
        Guid? broadcasterId,
        string requestedBy,
        string requestTypeForAudit,
        string failureReason,
        CancellationToken cancellationToken
    )
    {
        if (_db is DbContext context)
            context.ChangeTracker.Clear();

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        ErasureRequest? request = await _db.ErasureRequests.FirstOrDefaultAsync(
            r => r.Id == erasureRequestId,
            cancellationToken
        );
        if (request is not null)
        {
            request.Status = "failed";
            request.FailureReason = failureReason;
            request.CompletedAt = now;
        }
        _db.ComplianceAuditLogs.Add(
            BuildAuditRow(
                requestTypeForAudit,
                erasureRequestId,
                subjectIdHash,
                broadcasterId,
                requestedBy,
                tablesAffected: [],
                rowsAffected: 0,
                keysShredded: 0,
                outcome: "failed",
                now
            )
        );
        await _db.SaveChangesAsync(cancellationToken);

        await _eventBus.PublishAsync(
            new SubjectErasureFailedEvent
            {
                BroadcasterId = broadcasterId ?? Guid.Empty,
                ErasureRequestId = erasureRequestId,
                SubjectUserId = subjectUserId,
                SubjectIdHash = subjectIdHash,
                FailureReason = failureReason,
                OccurredAt = _timeProvider.GetUtcNow(),
            },
            cancellationToken
        );
        return Result.Failure<ErasureRequestDto>(failureReason, "ERASURE_FAILED");
    }

    private ComplianceAuditLog BuildAuditRow(
        string requestType,
        Guid erasureRequestId,
        string subjectIdHash,
        Guid? broadcasterId,
        string requestedBy,
        List<string> tablesAffected,
        int rowsAffected,
        int keysShredded,
        string outcome,
        DateTime now
    ) =>
        new()
        {
            RequestType = requestType,
            ErasureRequestId = erasureRequestId,
            SubjectIdHash = subjectIdHash,
            BroadcasterId = broadcasterId,
            RequestedBy = requestedBy,
            TablesAffected = tablesAffected,
            RowsAffected = rowsAffected,
            KeysShredded = keysShredded,
            Outcome = outcome,
            CompletedAt = now,
            CreatedAt = now,
        };

    private static void CountStep(
        List<string> tablesAffected,
        ref int rowsAffected,
        string table,
        int count
    )
    {
        if (count <= 0)
            return;
        tablesAffected.Add(table);
        rowsAffected += count;
    }

    private static ErasureRequestDto ToDto(ErasureRequest r) =>
        new(
            r.Id,
            r.SubjectUserId,
            r.SubjectIdHash,
            r.BroadcasterId,
            r.RequestType,
            r.RequestedBy,
            r.Status,
            r.Scope,
            r.CryptoShredApplied,
            r.AnonymizationApplied,
            r.RowsAffected,
            r.ExportLocation,
            r.ExportFormat,
            r.FailureReason,
            r.RequestedAt,
            r.CompletedAt
        );

    private sealed record ErasurePipelineOutcome(AnonymizationReport Report, int KeysShredded);
}
