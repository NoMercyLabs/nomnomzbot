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
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Common.Models.Crypto;
using NomNomzBot.Application.Contracts.Gdpr;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Gdpr;

/// <summary>
/// Proves the §3.7 erasure pipeline end to end on a real relational store: the subject's vaulted OAuth
/// tokens are actually cleared (ported from the retired <c>GdprServiceTests</c>: revocation targets the
/// RIGHT subject and leaves another user's connection intact), viewer data is scrubbed cross-channel
/// including soft-deleted rows, the subject DEK is crypto-shredded (CryptoKey flips to destroyed with its
/// wrapped material nulled), consent is withdrawn, and the ErasureRequest + ComplianceAuditLog ledger
/// records it all — while a mid-pipeline failure rolls the whole thing back yet leaves a surviving
/// <c>failed</c> request row and a <c>failed</c> audit row (the two-phase semantics).
/// </summary>
public sealed class ErasureServiceTests
{
    private static readonly Guid SubjectUser = Guid.Parse("0192a000-0000-7000-8000-00000000a001");
    private static readonly Guid OtherUser = Guid.Parse("0192a000-0000-7000-8000-00000000a002");
    private static readonly Guid SubjectChannel = Guid.Parse(
        "0192a000-0000-7000-8000-00000000c001"
    );
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000c002");

    private sealed record Harness(
        ErasureService Sut,
        IntegrationTokenVault Vault,
        ISubjectKeyService SubjectKeys,
        GdprTestDbContext Db,
        RecordingEventBus Bus,
        GdprSqliteDatabase Database
    );

    /// <summary>The same deterministic subject hash the services compute — verified independently here.</summary>
    private static string HashOf(Guid userId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId.ToString())));

    private static Harness Build(ISubjectKeyService? subjectKeysOverride = null)
    {
        GdprSqliteDatabase database = GdprSqliteDatabase.Open();
        GdprTestDbContext db = database.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(
            db,
            out ISubjectKeyService realKeys
        );
        RecordingEventBus bus = new();
        IntegrationTokenVault vault = new(
            db,
            protector,
            realKeys,
            new NoopScopeGrantService(),
            bus,
            TimeProvider.System,
            NullLogger<IntegrationTokenVault>.Instance
        );
        ISubjectKeyService subjectKeys = subjectKeysOverride ?? realKeys;
        ConsentService consents = new(
            db,
            bus,
            TimeProvider.System,
            NullLogger<ConsentService>.Instance
        );
        ErasureService sut = new(
            db,
            new GdprTestUnitOfWork(db),
            vault,
            subjectKeys,
            consents,
            bus,
            TimeProvider.System,
            NullLogger<ErasureService>.Instance
        );
        return new Harness(sut, vault, subjectKeys, db, bus, database);
    }

    private static async Task SeedUsersAsync(GdprTestDbContext db)
    {
        db.Users.Add(
            new User
            {
                Id = SubjectUser,
                TwitchUserId = "tw-subject",
                Username = "subject",
                UsernameNormalized = "subject",
                DisplayName = "Subject",
            }
        );
        db.Users.Add(
            new User
            {
                Id = OtherUser,
                TwitchUserId = "tw-other",
                Username = "other",
                UsernameNormalized = "other",
                DisplayName = "Other",
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> StoreConnectionAsync(
        IntegrationTokenVault vault,
        Guid channel,
        Guid connectedBy,
        string accessToken
    )
    {
        Guid connectionId = (
            await vault.UpsertConnectionAsync(
                new UpsertConnectionDto(
                    channel,
                    AuthEnums.IntegrationProvider.Twitch,
                    "twitch-account",
                    "login",
                    ["channel:read:subscriptions"],
                    ClientId: "client",
                    IsByok: false,
                    ConnectedByUserId: connectedBy,
                    SettingsJson: null
                )
            )
        )
            .Value
            .Id;

        await vault.StoreTokensAsync(
            connectionId,
            new StoreTokensDto(accessToken, "refresh", AppToken: null, DateTime.UtcNow.AddHours(1)),
            ["channel:read:subscriptions"]
        );
        return connectionId;
    }

    private static RequestErasureRequest SelfErasure(Guid subject) =>
        new(subject, null, "self_service", "deployment");

    // ── Erasure: the full pipeline ─────────────────────────────────────────────

    [Fact]
    public async Task Erasure_RevokesTheSubjectsVaultedConnection_AndLeavesAnotherUsersIntact()
    {
        // Ported from GdprServiceTests: erasure must clear the REAL token store, for the right subject only.
        Harness h = Build();
        using GdprSqliteDatabase _ = h.Database;
        await SeedUsersAsync(h.Db);
        Guid subjectConn = await StoreConnectionAsync(
            h.Vault,
            SubjectChannel,
            SubjectUser,
            "subject-access-token"
        );
        Guid otherConn = await StoreConnectionAsync(
            h.Vault,
            OtherChannel,
            OtherUser,
            "other-access-token"
        );

        Result<ErasureRequestDto> result = await h.Sut.RequestErasureAsync(
            SelfErasure(SubjectUser)
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);

        IntegrationConnection subject = await h
            .Db.IntegrationConnections.IgnoreQueryFilters()
            .SingleAsync(c => c.Id == subjectConn);
        subject.Status.Should().Be(AuthEnums.IntegrationStatus.Revoked);
        List<IntegrationToken> subjectTokens = await h
            .Db.IntegrationTokens.IgnoreQueryFilters()
            .Where(t => t.ConnectionId == subjectConn)
            .ToListAsync();
        subjectTokens.Should().NotBeEmpty().And.OnlyContain(t => t.DeletedAt != null);

        IntegrationConnection other = await h
            .Db.IntegrationConnections.IgnoreQueryFilters()
            .SingleAsync(c => c.Id == otherConn);
        other.Status.Should().Be(AuthEnums.IntegrationStatus.Connected);
        List<IntegrationToken> otherTokens = await h
            .Db.IntegrationTokens.IgnoreQueryFilters()
            .Where(t => t.ConnectionId == otherConn)
            .ToListAsync();
        otherTokens.Should().NotBeEmpty().And.OnlyContain(t => t.DeletedAt == null);

        // The subject profile is anonymized; the indexed normalized username must not leak the login.
        User anonymized = await h.Db.Users.SingleAsync(u => u.Id == SubjectUser);
        anonymized.Enabled.Should().BeFalse();
        anonymized.IsAnonymized.Should().BeTrue();
        anonymized.DisplayName.Should().Be("Deleted User");
        anonymized.UsernameNormalized.Should().NotBe("subject");

        ComplianceAuditLog audit = await h.Db.ComplianceAuditLogs.SingleAsync(a =>
            a.RequestType == "erasure"
        );
        audit.Outcome.Should().Be("completed");
        audit.TablesAffected.Should().Contain("IntegrationConnections").And.Contain("Users");
        audit.RowsAffected.Should().BeGreaterThanOrEqualTo(2);
        audit.SubjectIdHash.Should().Be(HashOf(SubjectUser));
    }

    [Fact]
    public async Task Erasure_ScrubsTheSubjectsViewerData_AcrossChannels_AndLeavesOthers()
    {
        // Ported from GdprServiceTests: cross-channel hard delete, soft-deleted rows included.
        Harness h = Build();
        using GdprSqliteDatabase _ = h.Database;
        await SeedUsersAsync(h.Db);
        h.Db.ViewerData.AddRange(
            new NomNomzBot.Domain.ViewerData.Entities.ViewerDatum
            {
                BroadcasterId = SubjectChannel,
                ViewerUserId = SubjectUser,
                Key = "deaths",
                Value = "12",
            },
            new NomNomzBot.Domain.ViewerData.Entities.ViewerDatum
            {
                BroadcasterId = OtherChannel,
                ViewerUserId = SubjectUser,
                Key = "quest",
                Value = "done",
                DeletedAt = DateTime.UtcNow,
            },
            new NomNomzBot.Domain.ViewerData.Entities.ViewerDatum
            {
                BroadcasterId = SubjectChannel,
                ViewerUserId = OtherUser,
                Key = "deaths",
                Value = "3",
            }
        );
        await h.Db.SaveChangesAsync();

        Result<ErasureRequestDto> result = await h.Sut.RequestErasureAsync(
            SelfErasure(SubjectUser)
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        List<NomNomzBot.Domain.ViewerData.Entities.ViewerDatum> remaining = await h
            .Db.ViewerData.IgnoreQueryFilters()
            .ToListAsync();
        remaining.Should().ContainSingle().Which.ViewerUserId.Should().Be(OtherUser);

        ComplianceAuditLog audit = await h.Db.ComplianceAuditLogs.SingleAsync();
        audit.TablesAffected.Should().Contain("ViewerData");
        audit.RowsAffected.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Erasure_WithNoVaultConnections_StillSucceeds()
    {
        // Ported from GdprServiceTests: a bare subject with nothing but a profile still erases cleanly.
        Harness h = Build();
        using GdprSqliteDatabase _ = h.Database;
        await SeedUsersAsync(h.Db);

        Result<ErasureRequestDto> result = await h.Sut.RequestErasureAsync(
            SelfErasure(SubjectUser)
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        (await h.Db.Users.SingleAsync(u => u.Id == SubjectUser)).Enabled.Should().BeFalse();
        result.Value.Status.Should().Be("completed");
    }

    [Fact]
    public async Task Erasure_CryptoShredsTheSubjectDek_WithdrawsConsent_AndCompletesTheLedger()
    {
        Harness h = Build();
        using GdprSqliteDatabase _ = h.Database;
        await SeedUsersAsync(h.Db);

        // Mint a REAL subject DEK under the subject's hash and FK it from the user row.
        string subjectHash = HashOf(SubjectUser);
        Result<Guid> keyId = await h.SubjectKeys.GetOrCreateSubjectKeyAsync(
            SubjectUser,
            subjectHash
        );
        keyId.IsSuccess.Should().BeTrue(keyId.ErrorMessage);
        User user = await h.Db.Users.SingleAsync(u => u.Id == SubjectUser);
        user.SubjectKeyId = keyId.Value;
        h.Db.ConsentRecords.Add(
            new ConsentRecord
            {
                BroadcasterId = SubjectChannel,
                SubjectUserId = SubjectUser,
                SubjectIdHash = subjectHash,
                ConsentType = "leaderboard_opt_in",
                Status = "granted",
                LawfulBasis = "consent",
                GrantedAt = DateTime.UtcNow,
            }
        );
        await h.Db.SaveChangesAsync();

        Result<ErasureRequestDto> result = await h.Sut.RequestErasureAsync(
            SelfErasure(SubjectUser)
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        ErasureRequestDto dto = result.Value;
        dto.Status.Should().Be("completed");
        dto.CryptoShredApplied.Should().BeTrue();
        dto.AnonymizationApplied.Should().BeTrue();
        dto.CompletedAt.Should().NotBeNull();

        // The DEK is destroyed in place: status flipped, wrapped material GONE, linked to this request.
        CryptoKey shredded = await h
            .Db.CryptoKeys.AsNoTracking()
            .SingleAsync(k => k.Id == keyId.Value);
        shredded.Status.Should().Be("destroyed");
        shredded.WrappedKeyMaterial.Should().BeNull();
        shredded.KekReference.Should().BeNull();
        shredded.DestroyedAt.Should().NotBeNull();
        shredded.ErasureRequestId.Should().Be(dto.Id);

        // The consent was withdrawn through the ledger.
        ConsentRecord consent = await h.Db.ConsentRecords.AsNoTracking().SingleAsync();
        consent.Status.Should().Be("withdrawn");
        consent.WithdrawnAt.Should().NotBeNull();

        // Request + audit ledger: report json persisted, keys counted, completed outcome.
        ErasureRequest request = await h
            .Db.ErasureRequests.AsNoTracking()
            .SingleAsync(r => r.Id == dto.Id);
        request.ReportJson.Should().NotBeNullOrEmpty();
        request.ReportJson.Should().Contain("CryptoKeys");
        ComplianceAuditLog audit = await h.Db.ComplianceAuditLogs.SingleAsync(a =>
            a.ErasureRequestId == dto.Id
        );
        audit.Outcome.Should().Be("completed");
        audit.KeysShredded.Should().BeGreaterThan(0);
        audit.RequestedBy.Should().Be("self_service");

        // The lifecycle events fired: requested then completed, hashed subject only.
        h.Bus.Published.OfType<SubjectErasureRequestedEvent>()
            .Should()
            .ContainSingle(e => e.ErasureRequestId == dto.Id && e.SubjectIdHash == subjectHash);
        h.Bus.Published.OfType<SubjectErasureCompletedEvent>()
            .Should()
            .ContainSingle(e => e.ErasureRequestId == dto.Id && e.KeysShredded > 0);
    }

    [Fact]
    public async Task Erasure_MidPipelineFailure_RollsBack_ButTheRequestRowSurvivesAsFailed()
    {
        // A DEK store that refuses to shred forces the failure AFTER the destructive steps ran in-tx.
        Harness h = Build(subjectKeysOverride: new FailingSubjectKeyService());
        using GdprSqliteDatabase _ = h.Database;
        await SeedUsersAsync(h.Db);
        h.Db.CryptoKeys.Add(
            new CryptoKey
            {
                Id = Guid.Parse("0192a000-0000-7000-8000-00000000d001"),
                KeyScope = "subject",
                SubjectIdHash = HashOf(SubjectUser),
                WrappedKeyMaterial = "wrapped",
                Provider = "local_aes",
                Algorithm = "AES-256-GCM",
                Status = "active",
            }
        );
        h.Db.ChatMessages.Add(
            new NomNomzBot.Domain.Chat.Entities.ChatMessage
            {
                Id = "msg-1",
                BroadcasterId = SubjectChannel,
                UserId = SubjectUser.ToString(),
                Username = "subject",
                DisplayName = "Subject",
                UserType = "viewer",
                Message = "hello",
            }
        );
        await h.Db.SaveChangesAsync();

        Result<ErasureRequestDto> result = await h.Sut.RequestErasureAsync(
            SelfErasure(SubjectUser)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("ERASURE_FAILED");

        // Everything destructive rolled back: the profile is untouched and the chat message survives.
        User untouched = await h.Db.Users.AsNoTracking().SingleAsync(u => u.Id == SubjectUser);
        untouched.IsAnonymized.Should().BeFalse();
        untouched.Username.Should().Be("subject");
        (await h.Db.ChatMessages.AsNoTracking().CountAsync()).Should().Be(1);

        // But the request row SURVIVED the rollback (two-phase write) and records the failure.
        ErasureRequest request = await h.Db.ErasureRequests.AsNoTracking().SingleAsync();
        request.Status.Should().Be("failed");
        request.FailureReason.Should().Contain("shred refused");
        ComplianceAuditLog audit = await h.Db.ComplianceAuditLogs.SingleAsync();
        audit.Outcome.Should().Be("failed");
        h.Bus.Published.OfType<SubjectErasureFailedEvent>().Should().ContainSingle();
    }

    // ── Export ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Export_ReturnsTheSubjectsDataDocument_AndExcludesOtherUsers()
    {
        Harness h = Build();
        using GdprSqliteDatabase _ = h.Database;
        await SeedUsersAsync(h.Db);
        h.Db.ChatMessages.AddRange(
            new NomNomzBot.Domain.Chat.Entities.ChatMessage
            {
                Id = "msg-subject",
                BroadcasterId = SubjectChannel,
                UserId = SubjectUser.ToString(),
                Username = "subject",
                DisplayName = "Subject",
                UserType = "viewer",
                Message = "subject-says-hi",
            },
            new NomNomzBot.Domain.Chat.Entities.ChatMessage
            {
                Id = "msg-other",
                BroadcasterId = SubjectChannel,
                UserId = OtherUser.ToString(),
                Username = "other",
                DisplayName = "Other",
                UserType = "viewer",
                Message = "other-says-secret",
            }
        );
        h.Db.ConsentRecords.Add(
            new ConsentRecord
            {
                BroadcasterId = null,
                SubjectUserId = SubjectUser,
                SubjectIdHash = HashOf(SubjectUser),
                ConsentType = "tos_privacy",
                Status = "granted",
                LawfulBasis = "contract",
                GrantedAt = DateTime.UtcNow,
            }
        );
        await h.Db.SaveChangesAsync();

        Result<DataExportDto> result = await h.Sut.RequestExportAsync(
            new RequestExportRequest(SubjectUser, null, "self_service")
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        DataExportDto export = result.Value;
        export.ExportFormat.Should().Be("json");
        export.SizeBytes.Should().BeGreaterThan(0);
        export.RowsAffected.Should().BeGreaterThanOrEqualTo(3); // profile + message + consent

        // The document carries the subject's real data — and NOTHING of the other user's.
        export.Document.Should().Contain("subject-says-hi");
        export.Document.Should().Contain("\"Username\": \"subject\"");
        export.Document.Should().Contain("tos_privacy");
        export.Document.Should().NotContain("other-says-secret");

        ErasureRequest request = await h
            .Db.ErasureRequests.AsNoTracking()
            .SingleAsync(r => r.Id == export.ErasureRequestId);
        request.RequestType.Should().Be("export");
        request.Status.Should().Be("completed");
        request.ExportFormat.Should().Be("json");
        request.ExportLocation.Should().Be(export.ExportLocation);

        ComplianceAuditLog audit = await h.Db.ComplianceAuditLogs.SingleAsync();
        audit.RequestType.Should().Be("export");
        audit.Outcome.Should().Be("completed");
        h.Bus.Published.OfType<SubjectDataExportedEvent>().Should().ContainSingle();
    }

    // ── Opt-out ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OptOut_WithdrawsMarketingAndLeaderboardConsents_AndFlagsAnalyticsOptOut()
    {
        Harness h = Build();
        using GdprSqliteDatabase _ = h.Database;
        await SeedUsersAsync(h.Db);
        string subjectHash = HashOf(SubjectUser);
        h.Db.ConsentRecords.AddRange(
            new ConsentRecord
            {
                BroadcasterId = SubjectChannel,
                SubjectUserId = SubjectUser,
                SubjectIdHash = subjectHash,
                ConsentType = "marketing",
                Status = "granted",
                LawfulBasis = "consent",
                GrantedAt = DateTime.UtcNow,
            },
            new ConsentRecord
            {
                BroadcasterId = SubjectChannel,
                SubjectUserId = SubjectUser,
                SubjectIdHash = subjectHash,
                ConsentType = "tos_privacy", // NOT an opt-out type — must remain granted
                Status = "granted",
                LawfulBasis = "contract",
                GrantedAt = DateTime.UtcNow,
            }
        );
        h.Db.ViewerProfiles.AddRange(
            new NomNomzBot.Domain.Analytics.Entities.ViewerProfile
            {
                BroadcasterId = SubjectChannel,
                ViewerUserId = SubjectUser,
                ViewerTwitchUserId = "tw-subject",
            },
            new NomNomzBot.Domain.Analytics.Entities.ViewerProfile
            {
                BroadcasterId = OtherChannel,
                ViewerUserId = SubjectUser,
                ViewerTwitchUserId = "tw-subject",
            }
        );
        await h.Db.SaveChangesAsync();

        Result<ErasureRequestDto> result = await h.Sut.RequestOptOutAsync(
            new RequestOptOutRequest(SubjectUser, null, "self_service")
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.RequestType.Should().Be("opt_out");
        result.Value.Status.Should().Be("completed");
        result.Value.CryptoShredApplied.Should().BeFalse(); // opt-out never destroys keys

        ConsentRecord marketing = await h
            .Db.ConsentRecords.AsNoTracking()
            .SingleAsync(r => r.ConsentType == "marketing");
        marketing.Status.Should().Be("withdrawn");
        ConsentRecord tos = await h
            .Db.ConsentRecords.AsNoTracking()
            .SingleAsync(r => r.ConsentType == "tos_privacy");
        tos.Status.Should().Be("granted");

        // The processing opt-out flag lands on EVERY channel's viewer profile (deployment-wide opt-out).
        List<NomNomzBot.Domain.Analytics.Entities.ViewerProfile> profiles = await h
            .Db.ViewerProfiles.AsNoTracking()
            .Where(p => p.ViewerUserId == SubjectUser)
            .ToListAsync();
        profiles.Should().HaveCount(2).And.OnlyContain(p => p.IsAnalyticsOptedOut);

        ComplianceAuditLog audit = await h.Db.ComplianceAuditLogs.SingleAsync();
        audit.RequestType.Should().Be("consent_change");
        audit.Outcome.Should().Be("completed");
        audit.KeysShredded.Should().Be(0);
    }

    // ── Reads: self-scoping ────────────────────────────────────────────────────

    [Fact]
    public async Task ListRequests_ScopedToASubject_ReturnsOnlyTheirOwnRequests()
    {
        Harness h = Build();
        using GdprSqliteDatabase _ = h.Database;
        await SeedUsersAsync(h.Db);
        (
            await h.Sut.RequestExportAsync(
                new RequestExportRequest(SubjectUser, null, "self_service")
            )
        )
            .IsSuccess.Should()
            .BeTrue();
        (await h.Sut.RequestExportAsync(new RequestExportRequest(OtherUser, null, "self_service")))
            .IsSuccess.Should()
            .BeTrue();

        Result<PagedList<ErasureRequestDto>> own = await h.Sut.ListRequestsAsync(
            new PaginationParams(),
            subjectUserId: SubjectUser,
            broadcasterId: null
        );
        Result<PagedList<ErasureRequestDto>> all = await h.Sut.ListRequestsAsync(
            new PaginationParams(),
            subjectUserId: null,
            broadcasterId: null
        );

        own.IsSuccess.Should().BeTrue();
        own.Value.Items.Should().ContainSingle().Which.SubjectUserId.Should().Be(SubjectUser);
        all.Value.Items.Should().HaveCount(2); // the compliance plane sees every subject
    }

    [Fact]
    public async Task Erasure_ForAnUnknownSubject_FailsNotFound_AndWritesNoLedger()
    {
        Harness h = Build();
        using GdprSqliteDatabase _ = h.Database;

        Result<ErasureRequestDto> result = await h.Sut.RequestErasureAsync(
            SelfErasure(Guid.Parse("0192a000-0000-7000-8000-00000000dead"))
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
        (await h.Db.ErasureRequests.CountAsync()).Should().Be(0);
        (await h.Db.ComplianceAuditLogs.CountAsync()).Should().Be(0);
    }

    /// <summary>A DEK service whose shred always fails — forces the mid-pipeline failure path.</summary>
    private sealed class FailingSubjectKeyService : ISubjectKeyService
    {
        public Task<Result<Guid>> GetOrCreateSubjectKeyAsync(
            Guid subjectUserId,
            string subjectIdHash,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success(Guid.NewGuid()));

        public Task<Result<CipherPayload>> ProtectAsync(
            Guid cryptoKeyId,
            string plaintext,
            CipherAad aad,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Failure<CipherPayload>("unsupported", "KEY_NOT_FOUND"));

        public Task<Result<string>> UnprotectAsync(
            Guid cryptoKeyId,
            CipherPayload payload,
            CipherAad aad,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Failure<string>("unsupported", "KEY_NOT_FOUND"));

        public Task<Result> DestroyKeyAsync(
            Guid cryptoKeyId,
            Guid erasureRequestId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Failure("the key vault shred refused", "KEY_VAULT_DOWN"));
    }
}
