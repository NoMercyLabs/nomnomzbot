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
