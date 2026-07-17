// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Gdpr;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Gdpr;

/// <summary>
/// Proves the consent ledger's latest-wins single-row semantics over a real store: grant persists the full
/// row shape, withdraw flips exactly that row (stamping WithdrawnAt), a re-grant reactivates the SAME row
/// (no duplicates for the unique ledger key), the HasActiveConsent gate tracks the transitions, and every
/// change emits a ConsentChangedEvent carrying only the hashed subject.
/// </summary>
public sealed class ConsentServiceTests
{
    private static readonly Guid Subject = Guid.Parse("0192a000-0000-7000-8000-00000000b001");
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000b0c1");

    private sealed record Harness(
        ConsentService Sut,
        GdprTestDbContext Db,
        RecordingEventBus Bus,
        GdprSqliteDatabase Database
    );

    private static async Task<Harness> BuildAsync()
    {
        GdprSqliteDatabase database = GdprSqliteDatabase.Open();
        GdprTestDbContext db = database.NewContext();
        db.Users.Add(
            new User
            {
                Id = Subject,
                TwitchUserId = "tw-subject",
                Username = "subject",
                UsernameNormalized = "subject",
                DisplayName = "Subject",
            }
        );
        await db.SaveChangesAsync();
        RecordingEventBus bus = new();
        ConsentService sut = new(db, bus, TimeProvider.System, NullLogger<ConsentService>.Instance);
        return new Harness(sut, db, bus, database);
    }

    private static GrantConsentRequest Grant(
        string consentType = "leaderboard_opt_in",
        string lawfulBasis = "consent",
        Guid? broadcasterId = null
    ) =>
        new(
            Subject,
            broadcasterId ?? Channel,
            consentType,
            lawfulBasis,
            ConsentVersion: "v1",
            Source: "dashboard",
            ProofOfConsentIp: null
        );

    [Fact]
    public async Task Grant_PersistsTheFullRowShape_AndTheGateFlipsTrue()
    {
        Harness h = await BuildAsync();
        using GdprSqliteDatabase _ = h.Database;

        Result<ConsentRecordDto> result = await h.Sut.GrantAsync(Grant());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        ConsentRecord row = await h.Db.ConsentRecords.AsNoTracking().SingleAsync();
        row.SubjectUserId.Should().Be(Subject);
        row.BroadcasterId.Should().Be(Channel);
        row.ConsentType.Should().Be("leaderboard_opt_in");
        row.Status.Should().Be("granted");
        row.LawfulBasis.Should().Be("consent");
        row.ConsentVersion.Should().Be("v1");
        row.Source.Should().Be("dashboard");
        row.SubjectIdHash.Should().HaveLength(64); // the deterministic 64-hex hash, never the raw id
        row.WithdrawnAt.Should().BeNull();
        row.IpAddressCipher.Should().BeNull(); // deliberately unsealed (entity doc-comment governs)

        Result<bool> active = await h.Sut.HasActiveConsentAsync(
            Subject,
            Channel,
            "leaderboard_opt_in"
        );
        active.Value.Should().BeTrue();
        h.Bus.Published.OfType<ConsentChangedEvent>()
            .Should()
            .ContainSingle(e => e.Status == "granted" && e.ConsentRecordId == row.Id);
    }

    [Fact]
    public async Task Withdraw_FlipsTheRow_TheGateReadsFalse_AndRegrantReactivatesTheSameRow()
    {
        Harness h = await BuildAsync();
        using GdprSqliteDatabase _ = h.Database;
        Guid rowId = (await h.Sut.GrantAsync(Grant())).Value.Id;

        Result withdrawn = await h.Sut.WithdrawAsync(Subject, Channel, "leaderboard_opt_in");

        withdrawn.IsSuccess.Should().BeTrue(withdrawn.ErrorMessage);
        ConsentRecord row = await h.Db.ConsentRecords.AsNoTracking().SingleAsync();
        row.Status.Should().Be("withdrawn");
        row.WithdrawnAt.Should().NotBeNull();
        (await h.Sut.HasActiveConsentAsync(Subject, Channel, "leaderboard_opt_in"))
            .Value.Should()
            .BeFalse();

        // Latest-wins: a re-grant reactivates the SAME ledger row — never a duplicate.
        Result<ConsentRecordDto> regrant = await h.Sut.GrantAsync(Grant());
        regrant.IsSuccess.Should().BeTrue(regrant.ErrorMessage);
        regrant.Value.Id.Should().Be(rowId);
        (await h.Db.ConsentRecords.CountAsync()).Should().Be(1);
        ConsentRecord reactivated = await h.Db.ConsentRecords.AsNoTracking().SingleAsync();
        reactivated.Status.Should().Be("granted");
        reactivated.WithdrawnAt.Should().BeNull();
        (await h.Sut.HasActiveConsentAsync(Subject, Channel, "leaderboard_opt_in"))
            .Value.Should()
            .BeTrue();
    }

    [Fact]
    public async Task Withdraw_WithoutAnActiveRow_FailsNotFound()
    {
        Harness h = await BuildAsync();
        using GdprSqliteDatabase _ = h.Database;

        Result result = await h.Sut.WithdrawAsync(Subject, Channel, "marketing");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
        h.Bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_RejectsUnknownConsentTypeAndLawfulBasis()
    {
        Harness h = await BuildAsync();
        using GdprSqliteDatabase _ = h.Database;

        Result<ConsentRecordDto> badType = await h.Sut.GrantAsync(Grant(consentType: "spy_on_me"));
        Result<ConsentRecordDto> badBasis = await h.Sut.GrantAsync(Grant(lawfulBasis: "vibes"));

        badType.ErrorCode.Should().Be("VALIDATION_FAILED");
        badBasis.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await h.Db.ConsentRecords.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ListForSubject_ScopesToChannelPlusPlatformWide_OrEverythingWithoutAContext()
    {
        Harness h = await BuildAsync();
        using GdprSqliteDatabase _ = h.Database;
        Guid otherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000b0c2");
        (await h.Sut.GrantAsync(Grant())).IsSuccess.Should().BeTrue();
        (await h.Sut.GrantAsync(Grant(consentType: "marketing", broadcasterId: otherChannel)))
            .IsSuccess.Should()
            .BeTrue();
        // Platform-wide ToS row (null broadcaster).
        (
            await h.Sut.GrantAsync(
                new GrantConsentRequest(Subject, null, "tos_privacy", "contract", null, null, null)
            )
        )
            .IsSuccess.Should()
            .BeTrue();

        Result<IReadOnlyList<ConsentRecordDto>> everything = await h.Sut.ListForSubjectAsync(
            Subject,
            broadcasterId: null
        );
        Result<IReadOnlyList<ConsentRecordDto>> channelView = await h.Sut.ListForSubjectAsync(
            Subject,
            Channel
        );

        everything.Value.Should().HaveCount(3);
        channelView.Value.Should().HaveCount(2); // the channel's row + the platform-wide row
        channelView.Value.Select(c => c.ConsentType).Should().NotContain("marketing");
    }
}
