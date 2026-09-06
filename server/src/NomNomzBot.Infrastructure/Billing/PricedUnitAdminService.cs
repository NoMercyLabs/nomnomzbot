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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Billing;

/// <summary>
/// Priced-unit authoring (S-ADMIN-4d) — the admin write surface that lets the owner give a real per-unit
/// price to a usage key <c>UsageRecord</c>/<c>TtsUsageRecord</c> already measures, so cost becomes
/// computable from data the owner actually supplied instead of a fabricated figure.
/// </summary>
public sealed class PricedUnitAdminService(IApplicationDbContext db, TimeProvider clock)
    : IPricedUnitAdminService
{
    public async Task<Result<IReadOnlyList<PricedUnitDto>>> ListPricedUnitsAsync(
        CancellationToken ct = default
    )
    {
        List<PricedUnit> units = await db
            .PricedUnits.Where(u => u.DeletedAt == null)
            .OrderBy(u => u.UnitKey)
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<PricedUnitDto>>([.. units.Select(ToDto)]);
    }

    public async Task<Result<PricedUnitDto>> AuthorPricedUnitAsync(
        AuthorPricedUnitRequest request,
        Guid? actorUserId,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.UnitKey))
            return Result.Failure<PricedUnitDto>("Unit key is required.", "VALIDATION_FAILED");
        if (string.IsNullOrWhiteSpace(request.Currency))
            return Result.Failure<PricedUnitDto>("Currency is required.", "VALIDATION_FAILED");
        if (request.PriceMinorUnitsPerBatch < 0)
            return Result.Failure<PricedUnitDto>("Price cannot be negative.", "VALIDATION_FAILED");
        if (request.BatchSize < 1)
            return Result.Failure<PricedUnitDto>(
                "Batch size must be at least 1.",
                "VALIDATION_FAILED"
            );

        PricedUnit? existing = await db.PricedUnits.FirstOrDefaultAsync(
            u => u.UnitKey == request.UnitKey && u.DeletedAt == null,
            ct
        );

        DateTime now = clock.GetUtcNow().UtcDateTime;
        string action;
        PricedUnit unit;
        if (existing is null)
        {
            action = "created";
            unit = new PricedUnit
            {
                UnitKey = request.UnitKey,
                Currency = request.Currency,
                PriceMinorUnitsPerBatch = request.PriceMinorUnitsPerBatch,
                BatchSize = request.BatchSize,
            };
            db.PricedUnits.Add(unit);
        }
        else
        {
            action =
                $"repriced from {existing.PriceMinorUnitsPerBatch}/{existing.BatchSize}{existing.Currency}";
            existing.Currency = request.Currency;
            existing.PriceMinorUnitsPerBatch = request.PriceMinorUnitsPerBatch;
            existing.BatchSize = request.BatchSize;
            unit = existing;
        }

        db.IamAuditLogs.Add(
            new IamAuditLog
            {
                PrincipalId = actorUserId ?? Guid.Empty,
                PrincipalType = IamPrincipalType.Employee,
                Permission = "priced_unit:author",
                TargetResource = request.UnitKey,
                Justification =
                    $"actor={actorUserId?.ToString() ?? "system"};unit='{request.UnitKey}';{action};"
                    + $"new={request.PriceMinorUnitsPerBatch}/{request.BatchSize}{request.Currency}",
                BreakGlass = false,
                Outcome = IamOutcome.Allowed,
                OccurredAt = now,
                AffectedTenantCount = 0,
            }
        );

        await db.SaveChangesAsync(ct);

        return Result.Success(ToDto(unit));
    }

    private static PricedUnitDto ToDto(PricedUnit unit) =>
        new(unit.Id, unit.UnitKey, unit.Currency, unit.PriceMinorUnitsPerBatch, unit.BatchSize);
}
