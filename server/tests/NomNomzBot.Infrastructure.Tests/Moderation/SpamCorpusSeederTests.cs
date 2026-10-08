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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Content.Moderation;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// The curated spam corpus seed (spam-defense.md §4.1, S-SPAM-SEED-CORPUS) against a real database: the
/// embedded file reaches <c>SpamSignatures</c> as Curated rows, a re-run changes nothing, a withdrawn row is
/// never resurrected, a Local row is upgraded rather than duplicated, and the seeded rows then drive the real
/// matcher on obfuscated copies of a corpus phrase.
/// </summary>
public class SpamCorpusSeederTests : IDisposable
{
    private const int CorpusSkeletonCount = 119;
    private const int CorpusDomainCount = 16;
    private const int RequiredCorroborations = 3;

    private static readonly Guid Channel = Guid.Parse("0199c000-0000-7000-8000-0000000000e1");
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FakeTimeProvider _time = new(Now);

    public SpamCorpusSeederTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using AppDbContext db = NewDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    /// <summary>Run the seeder exactly as the SeedRunner does: track changes, then one SaveChanges.</summary>
    private async Task RunSeederAsync()
    {
        using AppDbContext db = NewDbContext();
        await new SpamCorpusSeeder(db, _time).SeedAsync();
        await db.SaveChangesAsync();
    }

    private async Task<List<SpamSignature>> AllSignaturesAsync()
    {
        using AppDbContext db = NewDbContext();
        return await db.SpamSignatures.IgnoreQueryFilters().AsNoTracking().ToListAsync();
    }

    private async Task AddSignatureAsync(SpamSignature signature)
    {
        using AppDbContext db = NewDbContext();
        db.SpamSignatures.Add(signature);
        await db.SaveChangesAsync();
    }

    private static SpamSignature Existing(
        string value,
        SignatureSource source,
        bool quarantined = false
    ) =>
        new()
        {
            Kind = SignatureKind.Skeleton,
            Value = value,
            Source = source,
            IsQuarantined = quarantined,
            Corroborations = 1,
            FirstSeenAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
            LastConfirmedAt = new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc),
        };

    [Fact]
    public void TheEmbeddedCorpus_LoadsEveryCuratedSkeletonAndDomain()
    {
        SpamSeedCorpus corpus = SpamSeedCorpus.Load();

        corpus.Skeletons.Should().HaveCount(CorpusSkeletonCount).And.OnlyHaveUniqueItems();
        corpus.Domains.Should().HaveCount(CorpusDomainCount).And.OnlyHaveUniqueItems();
        corpus.Skeletons.Should().Contain("viewersonthestream");
        corpus.Domains.Should().Contain("boostmap.ru").And.Contain("streamfollowers.online");
        corpus
            .Skeletons.Should()
            .OnlyContain(s => s.All(char.IsAsciiLetterOrDigit), "a skeleton is L0-normalized text");
    }

    [Fact]
    public async Task Seeding_WritesEveryEntryAsACuratedRowThatMayActImmediately()
    {
        await RunSeederAsync();

        List<SpamSignature> rows = await AllSignaturesAsync();

        rows.Where(r => r.Kind == SignatureKind.Skeleton).Should().HaveCount(CorpusSkeletonCount);
        rows.Where(r => r.Kind == SignatureKind.Domain).Should().HaveCount(CorpusDomainCount);
        rows.Should()
            .OnlyContain(r =>
                r.Source == SignatureSource.Curated
                && !r.IsQuarantined
                && r.Corroborations == 1
                && r.WithdrawnAt == null
                && r.DeletedAt == null
                && r.FirstSeenAt == Now.UtcDateTime
                && r.LastConfirmedAt == Now.UtcDateTime
            );
        rows.Should().OnlyContain(r => r.CanAct(RequiredCorroborations));
    }

    [Fact]
    public async Task SeedingTwice_AddsNoRowAndChangesNoField()
    {
        await RunSeederAsync();
        List<SpamSignature> first = await AllSignaturesAsync();

        _time.Advance(TimeSpan.FromDays(3));
        await RunSeederAsync();
        List<SpamSignature> second = await AllSignaturesAsync();

        second.Should().HaveCount(CorpusSkeletonCount + CorpusDomainCount);
        second
            .Should()
            .BeEquivalentTo(first, "a re-run of the seeder must leave every row exactly as it was");
    }

    [Fact]
    public async Task AWithdrawnRow_IsNeverResurrected()
    {
        SpamSignature withdrawn = Existing("addviewers", SignatureSource.Local);
        withdrawn.WithdrawnAt = new DateTime(2026, 9, 5, 8, 0, 0, DateTimeKind.Utc);
        await AddSignatureAsync(withdrawn);

        await RunSeederAsync();

        List<SpamSignature> rows = await AllSignaturesAsync();
        SpamSignature stored = rows.Single(r => r.Value == "addviewers");
        stored.Id.Should().Be(withdrawn.Id, "the moderator's verdict row stays the only row");
        stored.WithdrawnAt.Should().Be(withdrawn.WithdrawnAt);
        stored.Source.Should().Be(SignatureSource.Local, "it is not re-marked Curated");
        stored.Corroborations.Should().Be(1);
        stored.LastConfirmedAt.Should().Be(withdrawn.LastConfirmedAt);
        rows.Should()
            .HaveCount(
                CorpusSkeletonCount + CorpusDomainCount,
                "every other entry is still seeded and nothing is duplicated"
            );
    }

    [Fact]
    public async Task ASoftDeletedRow_IsNotBroughtBack()
    {
        SpamSignature deleted = Existing("addmeoninsta", SignatureSource.Curated);
        deleted.DeletedAt = new DateTime(2026, 9, 6, 8, 0, 0, DateTimeKind.Utc);
        await AddSignatureAsync(deleted);

        await RunSeederAsync();

        List<SpamSignature> rows = await AllSignaturesAsync();
        SpamSignature stored = rows.Single(r => r.Value == "addmeoninsta");
        stored.DeletedAt.Should().Be(deleted.DeletedAt);
        stored.LastConfirmedAt.Should().Be(deleted.LastConfirmedAt);
        rows.Should().HaveCount(CorpusSkeletonCount + CorpusDomainCount);
    }

    [Fact]
    public async Task ALocalOrQuarantinedRow_IsUpgradedToCurated_NotDuplicated()
    {
        SpamSignature local = Existing("addmeondiscord", SignatureSource.Local);
        SpamSignature network = Existing("addmeupondiscord", SignatureSource.Network, true);
        await AddSignatureAsync(local);
        await AddSignatureAsync(network);

        await RunSeederAsync();

        List<SpamSignature> rows = await AllSignaturesAsync();
        rows.Should().HaveCount(CorpusSkeletonCount + CorpusDomainCount);

        foreach (SpamSignature original in new[] { local, network })
        {
            SpamSignature upgraded = rows.Single(r => r.Value == original.Value);
            upgraded.Id.Should().Be(original.Id, "the existing row is upgraded in place");
            upgraded.Source.Should().Be(SignatureSource.Curated);
            upgraded.IsQuarantined.Should().BeFalse();
            upgraded.Corroborations.Should().Be(2, "the curator's confirmation adds to the count");
            upgraded.FirstSeenAt.Should().Be(original.FirstSeenAt);
            upgraded.LastConfirmedAt.Should().Be(Now.UtcDateTime);
            upgraded.CanAct(RequiredCorroborations).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData("VI EWERS ON THE STREAM")]
    [InlineData("ｖｉｅｗｅｒｓ on the stream")]
    [InlineData("v13w3r5 0n th3 5tr34m")]
    public async Task ASeededPhrase_IsCaughtByTheRealMatcher_InPlainFullwidthAndLeetForm(
        string message
    )
    {
        await RunSeederAsync();

        ContentSignal[] signals = await EvaluateAsync(message);

        signals.Should().Contain(ContentSignal.CorpusMatch);
    }

    [Fact]
    public async Task WithoutTheSeed_TheSamePhrasesAreNotACorpusMatch()
    {
        foreach (
            string message in new[]
            {
                "VI EWERS ON THE STREAM",
                "ｖｉｅｗｅｒｓ on the stream",
                "v13w3r5 0n th3 5tr34m",
            }
        )
        {
            ContentSignal[] signals = await EvaluateAsync(message);

            signals.Should().NotContain(ContentSignal.CorpusMatch);
        }
    }

    private async Task<ContentSignal[]> EvaluateAsync(string message)
    {
        using AppDbContext db = NewDbContext();
        SpamDefenseService service = new(
            db,
            _time,
            Substitute.For<IModerationService>(),
            new CurrentTenantService(),
            Substitute.For<ITwitchUsersApi>(),
            Substitute.For<IFollowStateService>()
        );

        SpamEvaluationResult? result = await service.EvaluateAsync(
            new SpamEvaluationRequest(
                Channel,
                AuthEnums.Platform.Twitch,
                MessageId: Guid.NewGuid().ToString(),
                PlatformUserId: "viewer-1",
                DisplayName: "Viewer",
                Message: message,
                IsBroadcaster: false,
                IsModerator: false,
                IsVip: false,
                IsSubscriber: false
            )
        );

        result.Should().NotBeNull();
        return [.. result.Signals];
    }
}
