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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Automation.Entities;
using NomNomzBot.Infrastructure.AutomationApi;

namespace NomNomzBot.Infrastructure.Tests.AutomationApi;

/// <summary>
/// Proves the data-plane credential check (automation-api.md §3): a valid secret resolves to the
/// principal with the token's channel, scopes, and allowlist, and stamps <c>LastUsedAt</c>; an
/// unknown, expired, revoked, or soft-deleted secret is rejected with ONE uniform failure that never
/// says why; the <c>LastUsedAt</c> stamp is throttled so rapid calls don't write on every request.
/// </summary>
public sealed class AutomationTokenAuthenticatorTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f101");
    private static readonly DateTime T0 = new(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);
    private const string Secret = "nnzb_ak_deadbeef";

    private static (
        AutomationTokenAuthenticator Sut,
        AutomationTestDbContext Db,
        FakeTimeProvider Clock
    ) Build()
    {
        AutomationTestDbContext db = AutomationTestDbContext.New();
        FakeTimeProvider clock = new(new DateTimeOffset(T0));
        return (new AutomationTokenAuthenticator(db, clock), db, clock);
    }

    private static AutomationApiToken Seed(
        AutomationTestDbContext db,
        DateTime? expiresAt = null,
        DateTime? revokedAt = null,
        DateTime? deletedAt = null
    )
    {
        AutomationApiToken token = new()
        {
            BroadcasterId = Channel,
            Name = "deck",
            TokenHash = AutomationApiTokenService.HashSecret(Secret),
            TokenPrefix = Secret[..12],
            ScopesJson = """["invoke","events"]""",
            AllowedPipelineIdsJson = null,
            ExpiresAt = expiresAt,
            RevokedAt = revokedAt,
            DeletedAt = deletedAt,
            CreatedByUserId = Guid.NewGuid(),
        };
        db.AutomationApiTokens.Add(token);
        db.SaveChanges();
        return token;
    }

    [Fact]
    public async Task A_valid_secret_resolves_the_principal_and_stamps_LastUsedAt()
    {
        (AutomationTokenAuthenticator sut, AutomationTestDbContext db, _) = Build();
        AutomationApiToken token = Seed(db);

        Result<AutomationPrincipal> result = await sut.AuthenticateAsync(Secret);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.BroadcasterId.Should().Be(Channel);
        result.Value.TokenId.Should().Be(token.Id);
        result.Value.TokenName.Should().Be("deck");
        result.Value.Scopes.Should().BeEquivalentTo(["invoke", "events"]);
        result.Value.AllowedPipelineIds.Should().BeNull();
        (await db.AutomationApiTokens.SingleAsync()).LastUsedAt.Should().Be(T0);
    }

    [Fact]
    public async Task The_LastUsedAt_stamp_is_throttled_to_one_write_per_minute()
    {
        (AutomationTokenAuthenticator sut, AutomationTestDbContext db, FakeTimeProvider clock) =
            Build();
        Seed(db);

        await sut.AuthenticateAsync(Secret); // stamps T0
        clock.Advance(TimeSpan.FromSeconds(30));
        await sut.AuthenticateAsync(Secret); // inside the window — no new write
        (await db.AutomationApiTokens.SingleAsync()).LastUsedAt.Should().Be(T0);

        clock.Advance(TimeSpan.FromSeconds(31));
        await sut.AuthenticateAsync(Secret); // window elapsed — stamps again
        (await db.AutomationApiTokens.SingleAsync()).LastUsedAt.Should().Be(T0.AddSeconds(61));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("deleted")]
    [InlineData("unknown")]
    public async Task A_dead_or_unknown_secret_is_rejected_uniformly(string kind)
    {
        (AutomationTokenAuthenticator sut, AutomationTestDbContext db, _) = Build();
        switch (kind)
        {
            case "expired":
                Seed(db, expiresAt: T0.AddMinutes(-1));
                break;
            case "revoked":
                Seed(db, revokedAt: T0.AddMinutes(-1));
                break;
            case "deleted":
                Seed(db, deletedAt: T0.AddMinutes(-1));
                break;
            case "unknown":
                break; // nothing seeded
        }

        Result<AutomationPrincipal> result = await sut.AuthenticateAsync(Secret);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("UNAUTHENTICATED");
        result
            .ErrorMessage.Should()
            .Be("Invalid automation token.", "the reason a credential failed is never disclosed");
    }
}
