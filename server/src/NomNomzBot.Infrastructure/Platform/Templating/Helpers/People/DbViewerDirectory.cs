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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

/// <summary>
/// The user table behind <see cref="IViewerDirectory"/>. The trigger user comes from the seeds
/// (<c>user</c>, <c>user.name</c>, <c>user.id</c>, <c>user.provider</c>); the target is the typed first argument,
/// mapped to a login by the shared mention resolver. A person without a row still gets a reference, with no key.
/// </summary>
internal sealed class DbViewerDirectory : IViewerDirectory
{
    private const string DefaultProvider = "twitch";

    public Task<ViewerRef?> ResolveAsync(
        IServiceProvider services,
        ViewerSide side,
        IReadOnlyDictionary<string, string> seeds,
        CancellationToken cancellationToken
    ) =>
        side == ViewerSide.User
            ? ResolveUserAsync(services, seeds, cancellationToken)
            : ResolveTargetAsync(services, seeds, cancellationToken);

    private static async Task<ViewerRef?> ResolveUserAsync(
        IServiceProvider services,
        IReadOnlyDictionary<string, string> seeds,
        CancellationToken ct
    )
    {
        string id = Seed(seeds, "user.id");
        string login = Seed(seeds, "user.name");
        if (id.Length == 0 && login.Length == 0)
            return null;

        string provider = Seed(seeds, "user.provider");
        string display = Seed(seeds, "user");
        IApplicationDbContext db = services.GetRequiredService<IApplicationDbContext>();
        User? row = await FindUserRowAsync(db, id, provider, ct);
        return new ViewerRef(
            row?.Id,
            row?.TwitchUserId ?? (id.Length > 0 && !Guid.TryParse(id, out _) ? id : null),
            login.Length > 0 ? login : row?.Username ?? string.Empty,
            display.Length > 0 ? display : row?.DisplayName ?? login,
            provider.Length > 0 ? provider : DefaultProvider
        );
    }

    private static async Task<ViewerRef?> ResolveTargetAsync(
        IServiceProvider services,
        IReadOnlyDictionary<string, string> seeds,
        CancellationToken ct
    )
    {
        string typed = Seed(seeds, "target").TrimStart('@');
        if (typed.Length == 0)
            return null;

        IMentionTargetResolver resolver = services.GetRequiredService<IMentionTargetResolver>();
        string login = (await resolver.ResolveLoginAsync(typed, ct)).ToLowerInvariant();
        IApplicationDbContext db = services.GetRequiredService<IApplicationDbContext>();
        User? row = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == login, ct);
        string provider = Seed(seeds, "user.provider");
        return new ViewerRef(
            row?.Id,
            row?.TwitchUserId,
            login,
            row?.DisplayName ?? typed,
            provider.Length > 0 ? provider : DefaultProvider
        );
    }

    /// <summary>Redemption paths seed the user key itself; chat paths seed the provider's id.</summary>
    private static async Task<User?> FindUserRowAsync(
        IApplicationDbContext db,
        string externalId,
        string provider,
        CancellationToken ct
    )
    {
        if (externalId.Length == 0)
            return null;

        if (Guid.TryParse(externalId, out Guid direct))
            return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == direct, ct);

        if (provider.Length > 0)
        {
            Guid viaIdentity = await db
                .UserIdentities.AsNoTracking()
                .Where(i => i.Provider == provider && i.ProviderUserId == externalId)
                .Select(i => i.UserId)
                .FirstOrDefaultAsync(ct);
            if (viaIdentity != Guid.Empty)
                return await db
                    .Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == viaIdentity, ct);
        }

        return await db
            .Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.TwitchUserId == externalId, ct);
    }

    private static string Seed(IReadOnlyDictionary<string, string> seeds, string key) =>
        seeds.TryGetValue(key, out string? value) ? value.Trim() : string.Empty;
}
