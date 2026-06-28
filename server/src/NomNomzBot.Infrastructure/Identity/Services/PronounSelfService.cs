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
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Identity.Services;

/// <summary>
/// Self-service pronoun management for the authenticated viewer. Reads and writes the user's own
/// pronoun selection; respects <see cref="User.PronounManualOverride"/> to distinguish explicit
/// choices from automatically-resolved ones.
/// </summary>
public sealed class PronounSelfService : IPronounSelfService
{
    private readonly AppDbContext _db;

    public PronounSelfService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<UserPronounDto?> GetAsync(Guid userId, CancellationToken ct = default)
    {
        User? user = await _db
            .Users.Include(u => u.Pronoun)
            .Include(u => u.AltPronoun)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        return user is null ? null : ToDto(user);
    }

    public async Task<UserPronounDto?> SetAsync(
        Guid userId,
        SetPronounRequest request,
        CancellationToken ct = default
    )
    {
        User? user = await _db
            .Users.Include(u => u.Pronoun)
            .Include(u => u.AltPronoun)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
            return null;

        // `0` = explicit clear; `null` = leave unchanged.
        if (request.PronounId.HasValue)
            user.PronounId = request.PronounId.Value == 0 ? null : request.PronounId;
        if (request.AltPronounId.HasValue)
            user.AltPronounId = request.AltPronounId.Value == 0 ? null : request.AltPronounId;
        if (request.ManualOverride.HasValue)
            user.PronounManualOverride = request.ManualOverride.Value;

        await _db.SaveChangesAsync(ct);

        // Reload navigations so the DTO reflects the newly-set rows.
        await _db.Entry(user).Reference(u => u.Pronoun).LoadAsync(ct);
        await _db.Entry(user).Reference(u => u.AltPronoun).LoadAsync(ct);

        return ToDto(user);
    }

    private static UserPronounDto ToDto(User user)
    {
        string? primaryName = user.Pronoun?.Name;
        string? altName = user.AltPronoun?.Name;
        string? badge = BuildBadge(user.Pronoun, user.AltPronoun);

        return new UserPronounDto(
            PronounId: user.PronounId,
            PronounName: primaryName,
            PronounBadge: badge,
            AltPronounId: user.AltPronounId,
            AltPronounName: altName,
            ManualOverride: user.PronounManualOverride
        );
    }

    private static string? BuildBadge(Pronoun? primary, Pronoun? alt)
    {
        if (primary is null)
            return null;

        // With alt: "she/they" → "she/they" badge from primary subject + alt subject.
        if (alt is not null)
            return $"{primary.Subject}/{alt.Subject}";

        // Without alt: use the pronoun's own display name (e.g. "they/them").
        return primary.Name;
    }
}
