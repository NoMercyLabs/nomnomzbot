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
using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Platform.Services;
using PlatformConfiguration = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Infrastructure.Platform.Configuration;

/// <summary>The support contact lives in the global configuration row <c>platform:support_contact_url</c>.</summary>
public sealed class SupportContactService : ISupportContactService
{
    public const string Key = "platform:support_contact_url";
    public const string FallbackSetting = "Platform:SupportContactUrl";

    private readonly IApplicationDbContext _db;
    private readonly IConfiguration _configuration;

    public SupportContactService(IApplicationDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<string?> GetContactUrlAsync(CancellationToken ct = default)
    {
        PlatformConfiguration? row = await GlobalRowAsync(ct);
        if (!string.IsNullOrWhiteSpace(row?.Value))
            return row.Value;

        string? fallback = _configuration[FallbackSetting];
        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }

    public async Task<Result<string?>> SetContactUrlAsync(
        string? url,
        CancellationToken ct = default
    )
    {
        string? trimmed = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        if (trimmed is not null && !IsWebAddress(trimmed))
            return Result.Failure<string?>(
                "The support contact must be an absolute http or https address.",
                "VALIDATION_FAILED"
            );

        PlatformConfiguration? row = await GlobalRowAsync(ct);
        if (row is null)
        {
            row = new() { BroadcasterId = null, Key = Key };
            _db.Configurations.Add(row);
        }

        row.Value = trimmed;
        await _db.SaveChangesAsync(ct);
        return Result.Success(await GetContactUrlAsync(ct));
    }

    private Task<PlatformConfiguration?> GlobalRowAsync(CancellationToken ct) =>
        _db.Configurations.FirstOrDefaultAsync(c => c.BroadcasterId == null && c.Key == Key, ct);

    private static bool IsWebAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
