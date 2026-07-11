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
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Giveaways.Dtos;
using NomNomzBot.Application.Giveaways.Services;
using NomNomzBot.Domain.Giveaways.Entities;

namespace NomNomzBot.Infrastructure.Giveaways;

/// <summary>
/// <see cref="IGiveawayCodePoolService"/> (giveaways.md §3.2, D6). Codes are AEAD-sealed via
/// <see cref="ITokenProtector"/> on intake under the tenant-bound <see cref="GiveawayCodeProtection"/>
/// context (crypto-shreddable with the tenant's DEK); every read is MASKED (label + status); the only
/// plaintext read is the broadcaster-gated reveal of a winner's assigned code after a failed whisper.
/// </summary>
public sealed class GiveawayCodePoolService : IGiveawayCodePoolService
{
    private readonly IApplicationDbContext _db;
    private readonly ITokenProtector _protector;
    private readonly TimeProvider _clock;

    public GiveawayCodePoolService(
        IApplicationDbContext db,
        ITokenProtector protector,
        TimeProvider clock
    )
    {
        _db = db;
        _protector = protector;
        _clock = clock;
    }

    public async Task<Result<CodePoolDto>> CreatePoolAsync(
        Guid broadcasterId,
        CreateCodePoolRequest request,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result.Failure<CodePoolDto>("A pool name is required.", "VALIDATION_FAILED");

        GiveawayCodePool pool = new()
        {
            BroadcasterId = broadcasterId,
            Name = request.Name.Trim(),
            Description = request.Description,
        };
        await _db.GiveawayCodePools.AddAsync(pool, ct);
        await _db.SaveChangesAsync(ct);
        return Result.Success(new CodePoolDto(pool.Id, pool.Name, pool.Description, 0, 0, 0));
    }

    public async Task<Result<CodePoolDto>> AddCodesAsync(
        Guid broadcasterId,
        Guid poolId,
        AddCodesRequest request,
        CancellationToken ct = default
    )
    {
        GiveawayCodePool? pool = await FindPoolAsync(broadcasterId, poolId, ct);
        if (pool is null)
            return Result.Failure<CodePoolDto>("Code pool not found.", "NOT_FOUND");
        if (request.Codes.Count == 0)
            return Result.Failure<CodePoolDto>("No codes supplied.", "VALIDATION_FAILED");
        if (request.Codes.Any(c => string.IsNullOrWhiteSpace(c.Code)))
            return Result.Failure<CodePoolDto>("Empty codes are not allowed.", "VALIDATION_FAILED");

        foreach (CodeInput input in request.Codes)
        {
            string plaintext = input.Code.Trim();
            string cipher = await _protector.ProtectAsync(
                plaintext,
                GiveawayCodeProtection.Context(broadcasterId),
                ct
            );
            await _db.GiveawayCodes.AddAsync(
                new GiveawayCode
                {
                    BroadcasterId = broadcasterId,
                    CodePoolId = poolId,
                    CodeCipher = cipher,
                    // The masked tail keeps a pool auditable without ever echoing the secret.
                    Label = input.Label ?? $"…{plaintext[Math.Max(0, plaintext.Length - 4)..]}",
                    Status = GiveawayCodeStatus.Available,
                },
                ct
            );
        }
        await _db.SaveChangesAsync(ct);

        return Result.Success(await ToPoolDtoAsync(pool, ct));
    }

    public async Task<Result<PagedList<CodePoolDto>>> ListPoolsAsync(
        Guid broadcasterId,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<GiveawayCodePool> query = _db
            .GiveawayCodePools.AsNoTracking()
            .Where(p => p.BroadcasterId == broadcasterId)
            .OrderByDescending(p => p.Id);

        int total = await query.CountAsync(ct);
        List<GiveawayCodePool> pools = await query
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        List<CodePoolDto> dtos = [];
        foreach (GiveawayCodePool pool in pools)
            dtos.Add(await ToPoolDtoAsync(pool, ct));

        return Result.Success(
            new PagedList<CodePoolDto>(dtos, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<CodePoolDetailDto>> GetPoolAsync(
        Guid broadcasterId,
        Guid poolId,
        CancellationToken ct = default
    )
    {
        GiveawayCodePool? pool = await FindPoolAsync(broadcasterId, poolId, ct);
        if (pool is null)
            return Result.Failure<CodePoolDetailDto>("Code pool not found.", "NOT_FOUND");

        // MASKED read (D6): id + label + status only — CodeCipher never leaves the row, plaintext never
        // exists here at all.
        List<MaskedCodeDto> codes = await _db
            .GiveawayCodes.AsNoTracking()
            .Where(c => c.CodePoolId == poolId)
            .OrderBy(c => c.Id)
            .Select(c => new MaskedCodeDto(c.Id, c.Label, c.Status, c.AssignedAt))
            .ToListAsync(ct);

        return Result.Success(new CodePoolDetailDto(pool.Id, pool.Name, pool.Description, codes));
    }

    public async Task<Result> DeletePoolAsync(
        Guid broadcasterId,
        Guid poolId,
        CancellationToken ct = default
    )
    {
        GiveawayCodePool? pool = await FindPoolAsync(broadcasterId, poolId, ct);
        if (pool is null)
            return Result.Failure("Code pool not found.", "NOT_FOUND");

        bool inUse = await _db.Giveaways.AnyAsync(
            g =>
                g.PrizeCodePoolId == poolId
                && (g.Status == GiveawayStatus.Open || g.Status == GiveawayStatus.Closed),
            ct
        );
        if (inUse)
            return Result.Failure(
                "The pool backs an active giveaway — draw or archive it first.",
                "VALIDATION_FAILED"
            );

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        pool.DeletedAt = now;
        List<GiveawayCode> codes = await _db
            .GiveawayCodes.Where(c => c.CodePoolId == poolId)
            .ToListAsync(ct);
        foreach (GiveawayCode code in codes)
            code.DeletedAt = now;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<string>> RevealAssignedCodeAsync(
        Guid broadcasterId,
        Guid winnerId,
        CancellationToken ct = default
    )
    {
        GiveawayWinner? winner = await _db
            .GiveawayWinners.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == winnerId && w.BroadcasterId == broadcasterId, ct);
        if (winner?.AssignedCodeId is not { } codeId)
            return Result.Failure<string>("This winner has no assigned code.", "NOT_FOUND");

        GiveawayCode? code = await _db
            .GiveawayCodes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == codeId && c.BroadcasterId == broadcasterId, ct);
        if (code is null)
            return Result.Failure<string>("The assigned code no longer exists.", "NOT_FOUND");

        string? plaintext = await _protector.TryUnprotectAsync(
            code.CodeCipher,
            GiveawayCodeProtection.Context(broadcasterId),
            ct
        );
        return plaintext is null
            ? Result.Failure<string>("The code could not be decrypted.", "SERVICE_UNAVAILABLE")
            : Result.Success(plaintext);
    }

    private Task<GiveawayCodePool?> FindPoolAsync(
        Guid broadcasterId,
        Guid poolId,
        CancellationToken ct
    ) =>
        _db.GiveawayCodePools.FirstOrDefaultAsync(
            p => p.Id == poolId && p.BroadcasterId == broadcasterId,
            ct
        );

    private async Task<CodePoolDto> ToPoolDtoAsync(GiveawayCodePool pool, CancellationToken ct)
    {
        var counts = await _db
            .GiveawayCodes.AsNoTracking()
            .Where(c => c.CodePoolId == pool.Id)
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        int total = counts.Sum(c => c.Count);
        int available = counts
            .Where(c => c.Status == GiveawayCodeStatus.Available)
            .Sum(c => c.Count);
        int assigned = counts
            .Where(c =>
                c.Status == GiveawayCodeStatus.Assigned || c.Status == GiveawayCodeStatus.Delivered
            )
            .Sum(c => c.Count);
        return new CodePoolDto(pool.Id, pool.Name, pool.Description, total, available, assigned);
    }
}
