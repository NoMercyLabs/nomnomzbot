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
using NomNomzBot.Domain.Automation.Events;
using NomNomzBot.Infrastructure.AutomationApi;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.AutomationApi;

/// <summary>
/// Proves the automation token management plane (automation-api.md §3): a create mints a one-time
/// secret and persists ONLY its SHA-256 hash + display prefix (the secret is unrecoverable from any
/// later read); names are unique per channel; unknown scopes are rejected before anything persists;
/// a rotate replaces the hash so the old secret stops matching and audits as a rotation; a revoke
/// tombstones the row, audits exactly once, and is idempotent; a revoked token cannot be rotated back
/// to life.
/// </summary>
public sealed class AutomationApiTokenServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f001");
    private static readonly Guid Creator = Guid.Parse("0192a000-0000-7000-8000-00000000f002");
    private static readonly DateTime T0 = new(2026, 7, 17, 9, 0, 0, DateTimeKind.Utc);

    private static (
        AutomationApiTokenService Sut,
        AutomationTestDbContext Db,
        RecordingEventBus Bus
    ) Build()
    {
        AutomationTestDbContext db = AutomationTestDbContext.New();
        RecordingEventBus bus = new();
        AutomationApiTokenService sut = new(
            db,
            bus,
            new FakeTimeProvider(new DateTimeOffset(T0)),
            new Infrastructure.AutomationApi.Events.AutomationEventRegistry([
                new Infrastructure.AutomationApi.Events.SupporterReceivedEventDescriptor(),
            ])
        );
        return (sut, db, bus);
    }

    private static CreateAutomationTokenRequest Request(
        string name = "deck",
        IReadOnlyList<string>? scopes = null
    ) => new() { Name = name, Scopes = scopes ?? ["invoke", "read"] };

    [Fact]
    public async Task Create_returns_the_secret_once_and_persists_only_its_hash()
    {
        (AutomationApiTokenService sut, AutomationTestDbContext db, RecordingEventBus bus) =
            Build();

        Result<IssuedAutomationTokenDto> result = await sut.CreateAsync(
            Channel,
            Creator,
            Request(),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        string secret = result.Value.Secret;
        secret.Should().StartWith("nnzb_ak_");
        secret.Length.Should().Be("nnzb_ak_".Length + 64, "32 CSPRNG bytes as hex");

        AutomationApiToken row = await db.AutomationApiTokens.SingleAsync();
        row.TokenHash.Should().Be(AutomationApiTokenService.HashSecret(secret));
        row.TokenHash.Should().NotBe(secret, "only the hash is stored");
        row.TokenPrefix.Should().Be(secret[..12]);
        row.CreatedByUserId.Should().Be(Creator);
        row.ScopesJson.Should().Contain("invoke").And.Contain("read");

        // Nothing retrievable later carries the secret — the list DTO has only the display prefix.
        Result<PagedList<AutomationTokenDto>> list = await sut.ListAsync(
            Channel,
            new PaginationParams(1, 25, null, null)
        );
        list.Value.Items.Single().TokenPrefix.Should().Be(secret[..12]);

        AutomationTokenIssuedEvent issued = bus
            .Published.OfType<AutomationTokenIssuedEvent>()
            .Single();
        issued.WasRotation.Should().BeFalse();
        issued.TokenId.Should().Be(row.Id);
        issued.BroadcasterId.Should().Be(Channel);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_name_and_an_unknown_scope()
    {
        (AutomationApiTokenService sut, AutomationTestDbContext db, _) = Build();
        await sut.CreateAsync(Channel, Creator, Request("deck"), CancellationToken.None);

        Result<IssuedAutomationTokenDto> dup = await sut.CreateAsync(
            Channel,
            Creator,
            Request("deck"),
            CancellationToken.None
        );
        dup.IsFailure.Should().BeTrue();
        dup.ErrorCode.Should().Be("ALREADY_EXISTS");

        Result<IssuedAutomationTokenDto> badScope = await sut.CreateAsync(
            Channel,
            Creator,
            Request("other", scopes: ["invoke", "admin"]),
            CancellationToken.None
        );
        badScope.IsFailure.Should().BeTrue();
        badScope.ErrorCode.Should().Be("VALIDATION_FAILED");

        (await db.AutomationApiTokens.CountAsync())
            .Should()
            .Be(1, "neither reject persisted a row");
    }

    [Fact]
    public async Task Rotate_replaces_the_hash_so_the_old_secret_stops_matching()
    {
        (AutomationApiTokenService sut, AutomationTestDbContext db, RecordingEventBus bus) =
            Build();
        Result<IssuedAutomationTokenDto> created = await sut.CreateAsync(
            Channel,
            Creator,
            Request(),
            CancellationToken.None
        );
        string oldSecret = created.Value.Secret;

        Result<IssuedAutomationTokenDto> rotated = await sut.RotateAsync(
            Channel,
            created.Value.Token.Id,
            Creator,
            CancellationToken.None
        );

        rotated.IsSuccess.Should().BeTrue(rotated.ErrorMessage);
        rotated.Value.Secret.Should().NotBe(oldSecret);

        AutomationApiToken row = await db.AutomationApiTokens.SingleAsync();
        row.TokenHash.Should().Be(AutomationApiTokenService.HashSecret(rotated.Value.Secret));
        row.TokenHash.Should()
            .NotBe(
                AutomationApiTokenService.HashSecret(oldSecret),
                "the old secret no longer authenticates"
            );

        bus.Published.OfType<AutomationTokenIssuedEvent>()
            .Should()
            .HaveCount(2)
            .And.ContainSingle(e => e.WasRotation);
    }

    [Fact]
    public async Task Revoke_tombstones_audits_once_and_is_idempotent()
    {
        (AutomationApiTokenService sut, AutomationTestDbContext db, RecordingEventBus bus) =
            Build();
        Result<IssuedAutomationTokenDto> created = await sut.CreateAsync(
            Channel,
            Creator,
            Request(),
            CancellationToken.None
        );
        Guid tokenId = created.Value.Token.Id;

        Result<bool> first = await sut.RevokeAsync(
            Channel,
            tokenId,
            Creator,
            CancellationToken.None
        );
        Result<bool> second = await sut.RevokeAsync(
            Channel,
            tokenId,
            Creator,
            CancellationToken.None
        );

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue("revoking an already-revoked token is idempotent");
        (await db.AutomationApiTokens.SingleAsync()).RevokedAt.Should().Be(T0);
        bus.Published.OfType<AutomationTokenRevokedEvent>()
            .Should()
            .HaveCount(1, "the tombstone audits exactly once");

        // A revoked credential never comes back through rotate.
        Result<IssuedAutomationTokenDto> rotate = await sut.RotateAsync(
            Channel,
            tokenId,
            Creator,
            CancellationToken.None
        );
        rotate.IsFailure.Should().BeTrue();
        rotate.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Tokens_are_channel_scoped_reads_and_writes()
    {
        (AutomationApiTokenService sut, _, _) = Build();
        Guid otherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000f0ff");
        Result<IssuedAutomationTokenDto> created = await sut.CreateAsync(
            Channel,
            Creator,
            Request(),
            CancellationToken.None
        );

        // Another channel neither lists it nor may revoke it.
        Result<PagedList<AutomationTokenDto>> otherList = await sut.ListAsync(
            otherChannel,
            new PaginationParams(1, 25, null, null)
        );
        otherList.Value.TotalCount.Should().Be(0);
        Result<bool> foreignRevoke = await sut.RevokeAsync(
            otherChannel,
            created.Value.Token.Id,
            Creator,
            CancellationToken.None
        );
        foreignRevoke.IsFailure.Should().BeTrue();
        foreignRevoke.ErrorCode.Should().Be("NOT_FOUND");
    }
}
