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
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Bot;

/// <summary>Reads the bot's name from the enabled <c>twitch_bot</c> service row, the way <c>{botname}</c> does.</summary>
internal sealed class DbBotIdentityDirectory : IBotIdentityDirectory
{
    public const string FallbackName = "NomNomzBot";

    public async Task<string> NameAsync(
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        try
        {
            IApplicationDbContext db = services.GetRequiredService<IApplicationDbContext>();
            Service? service = await db
                .Services.AsNoTracking()
                .Where(s => s.Name == "twitch_bot" && s.Enabled)
                .FirstOrDefaultAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(service?.UserName) ? FallbackName : service.UserName;
        }
        catch (Exception)
        {
            return FallbackName;
        }
    }
}
