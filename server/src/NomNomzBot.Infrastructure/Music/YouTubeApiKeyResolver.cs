// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Contracts.Music;

namespace NomNomzBot.Infrastructure.Music;

public sealed class YouTubeApiKeyResolver(
    ISystemCredentialsProvider credentials,
    IConfiguration configuration
) : IYouTubeApiKeyResolver
{
    public async Task<string?> GetAsync(CancellationToken cancellationToken = default)
    {
        string? stored = await credentials.GetValueAsync("youtube", "api_key", cancellationToken);
        if (!string.IsNullOrWhiteSpace(stored))
            return stored;

        string? configured = configuration["YouTube:ApiKey"];
        return string.IsNullOrWhiteSpace(configured) ? null : configured;
    }
}
