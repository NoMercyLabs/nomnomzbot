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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Billing;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Billing;

/// <summary>
/// S-ADMIN-4d — the priced-unit model. Proves a unit price is authored and PERSISTS with an explicit
/// currency, integer minor units, and the unit it prices (never a float), that authoring lands an
/// <see cref="IamAuditLog"/> row naming the acting operator, and that authoring the same unit key twice
/// reprices it (upsert) instead of creating a duplicate row.
/// </summary>
public sealed class PricedUnitAdminServiceTests
{
    private static readonly Guid Actor = Guid.Parse("0199b000-0000-7000-8000-0000000a0d01");

    private static (PricedUnitAdminService Sut, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        return (new PricedUnitAdminService(db, TimeProvider.System), db);
    }

    [Fact]
    public async Task AuthorPricedUnit_persists_an_integer_priced_unit_with_currency_and_audits_the_operator()
    {
        (PricedUnitAdminService sut, AuthDbContext db) = Build();

        Result<PricedUnitDto> result = await sut.AuthorPricedUnitAsync(
            new AuthorPricedUnitRequest("sandbox_exec_ms", "usd", 5, 1_000),
            Actor
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.UnitKey.Should().Be("sandbox_exec_ms");
        result.Value.Currency.Should().Be("usd");
        result.Value.PriceMinorUnitsPerBatch.Should().Be(5);
        result.Value.BatchSize.Should().Be(1_000);

        // Actually persisted — not merely returned by the call.
        PricedUnit persisted = await db.PricedUnits.SingleAsync(u =>
            u.UnitKey == "sandbox_exec_ms"
        );
        persisted.Currency.Should().Be("usd");
        persisted.PriceMinorUnitsPerBatch.Should().Be(5);
        persisted.BatchSize.Should().Be(1_000);

        IamAuditLog audit = db.IamAuditLogs.Single(a => a.Permission == "priced_unit:author");
        audit.PrincipalId.Should().Be(Actor);
        audit.TargetResource.Should().Be("sandbox_exec_ms");
        audit.Justification.Should().Contain(Actor.ToString());
    }

    [Fact]
    public async Task AuthorPricedUnit_reprices_an_existing_unit_key_instead_of_creating_a_duplicate_row()
    {
        (PricedUnitAdminService sut, AuthDbContext db) = Build();

        await sut.AuthorPricedUnitAsync(
            new AuthorPricedUnitRequest("tts_characters", "usd", 10, 1_000),
            Actor
        );
        Result<PricedUnitDto> repriced = await sut.AuthorPricedUnitAsync(
            new AuthorPricedUnitRequest("tts_characters", "usd", 25, 1_000),
            Actor
        );

        repriced.IsSuccess.Should().BeTrue();
        repriced.Value.PriceMinorUnitsPerBatch.Should().Be(25);

        db.PricedUnits.Count(u => u.UnitKey == "tts_characters").Should().Be(1);
        (await db.PricedUnits.SingleAsync(u => u.UnitKey == "tts_characters"))
            .PriceMinorUnitsPerBatch.Should()
            .Be(25);
        db.IamAuditLogs.Count(a => a.Permission == "priced_unit:author").Should().Be(2);
    }

    [Fact]
    public async Task AuthorPricedUnit_rejects_a_negative_price()
    {
        (PricedUnitAdminService sut, AuthDbContext db) = Build();

        Result<PricedUnitDto> result = await sut.AuthorPricedUnitAsync(
            new AuthorPricedUnitRequest("sandbox_exec_ms", "usd", -1, 1),
            Actor
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        db.PricedUnits.Should().BeEmpty();
    }
}
