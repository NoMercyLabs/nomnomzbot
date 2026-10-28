// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
