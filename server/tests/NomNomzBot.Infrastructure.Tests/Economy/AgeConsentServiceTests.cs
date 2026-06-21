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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Domain.Economy.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// Proves the 18+ gambling gate (economy.md §3.6): a recent non-personnel account is fail-closed; an old
/// account auto-passes by the account-age inference and a Twitch-staff account by the personnel inference (both
/// recorded only in the K.8 cache, NEVER in the consent ledger); an explicit grant writes the authoritative
/// ConsentRecord + opens the gate; and a revoke withdraws it + closes the gate.
/// </summary>
public sealed class AgeConsentServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000a1");
    private static readonly DateTimeOffset Now = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);
    private static int _seq;

    private static (AgeConsentService Sut, AuthDbContext Db, RecordingEventBus Bus) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RecordingEventBus bus = new();
        return (new AgeConsentService(db, bus, new FakeTimeProvider(Now)), db, bus);
    }

    private static Guid SeedUser(AuthDbContext db, DateTime accountCreated, string type = "")
    {
        string id = $"tw{_seq++}";
        User user = new()
        {
            TwitchUserId = id,
            Username = id,
            UsernameNormalized = id,
            DisplayName = id,
            AccountCreatedAt = accountCreated,
            Type = type,
        };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    [Fact]
    public async Task A_recent_non_personnel_account_is_fail_closed()
    {
        (AgeConsentService sut, AuthDbContext db, _) = Build();
        Guid viewer = SeedUser(db, Now.UtcDateTime.AddYears(-1));

        (await sut.HasGrantedAsync(Channel, viewer)).Value.Should().BeFalse();
    }

    [Fact]
    public async Task An_old_account_auto_passes_and_never_touches_the_consent_ledger()
    {
        (AgeConsentService sut, AuthDbContext db, _) = Build();
        Guid viewer = SeedUser(db, Now.UtcDateTime.AddYears(-10));

        (await sut.HasGrantedAsync(Channel, viewer)).Value.Should().BeTrue();
        db.ViewerAgeConsents.Single(c => c.ViewerUserId == viewer)
            .ConfirmationMethod.Should()
            .Be("inferred_account_age");
        db.ConsentRecords.Should().BeEmpty(); // an inference is never written as consent
    }

    [Fact]
    public async Task Twitch_staff_auto_passes_by_the_personnel_inference()
    {
        (AgeConsentService sut, AuthDbContext db, _) = Build();
        Guid viewer = SeedUser(db, Now.UtcDateTime.AddYears(-1), type: "staff");

        (await sut.HasGrantedAsync(Channel, viewer)).Value.Should().BeTrue();
        db.ViewerAgeConsents.Single(c => c.ViewerUserId == viewer)
            .ConfirmationMethod.Should()
            .Be("inferred_twitch_personnel");
    }

    [Fact]
    public async Task Grant_records_consent_and_opens_the_gate()
    {
        (AgeConsentService sut, AuthDbContext db, RecordingEventBus bus) = Build();
        Guid viewer = SeedUser(db, Now.UtcDateTime.AddYears(-1)); // no inference basis

        Result<AgeConsentDto> grant = await sut.GrantAsync(
            Channel,
            new GrantAgeConsentRequest(viewer, "self_confirm", null, "v1")
        );

        grant.Value.Granted.Should().BeTrue();
        grant.Value.ConfirmationMethod.Should().Be("self_confirm");
        db.ConsentRecords.Single(c => c.SubjectUserId == viewer).Status.Should().Be("granted");
        bus.Published.OfType<AgeConsentGrantedEvent>().Should().ContainSingle();
        (await sut.HasGrantedAsync(Channel, viewer)).Value.Should().BeTrue();
    }

    [Fact]
    public async Task Revoke_withdraws_consent_and_closes_the_gate()
    {
        (AgeConsentService sut, AuthDbContext db, RecordingEventBus bus) = Build();
        Guid viewer = SeedUser(db, Now.UtcDateTime.AddYears(-1));
        await sut.GrantAsync(
            Channel,
            new GrantAgeConsentRequest(viewer, "self_confirm", null, null)
        );

        Result<AgeConsentDto> revoke = await sut.RevokeAsync(Channel, viewer);

        revoke.Value.Granted.Should().BeFalse();
        db.ConsentRecords.Single(c => c.SubjectUserId == viewer).Status.Should().Be("withdrawn");
        bus.Published.OfType<AgeConsentRevokedEvent>().Should().ContainSingle();
        (await sut.HasGrantedAsync(Channel, viewer)).Value.Should().BeFalse(); // recent account, no inference
    }
}
