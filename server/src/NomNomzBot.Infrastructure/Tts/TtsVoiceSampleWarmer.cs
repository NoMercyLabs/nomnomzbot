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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// Makes the introduction clip of every free voice once, a little after start, so pressing play on a voice in the
/// dashboard plays at once. A voice from a paid provider is made on its first press, never in bulk, so the warmer
/// spends no one's provider quota. One clip every few seconds keeps it quiet, and a run of failures ends the pass.
/// </summary>
public sealed class TtsVoiceSampleWarmer(
    IServiceScopeFactory scopes,
    ITtsVoiceSampleStore samples,
    ILogger<TtsVoiceSampleWarmer> logger
) : BackgroundService
{
    internal const string FreeProvider = "edge";
    internal const int MaxFailuresInARow = 5;

    private static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, stoppingToken);
            int made = await WarmAsync(Pause, stoppingToken);
            logger.LogInformation("TTS voice samples: {Made} made", made);
        }
        catch (OperationCanceledException)
        {
            // The host is stopping.
        }
    }

    /// <summary>Makes the clip of each free voice that has none yet. Returns how many it made.</summary>
    internal async Task<int> WarmAsync(TimeSpan pause, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        ITtsConfigService config = scope.ServiceProvider.GetRequiredService<ITtsConfigService>();

        List<string> voiceIds = await db
            .TtsVoices.AsNoTracking()
            .Where(v => v.Provider == FreeProvider)
            .OrderBy(v => v.Id)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);

        int made = 0;
        int failuresInARow = 0;
        foreach (string voiceId in voiceIds)
        {
            if (await samples.GetAsync(voiceId, cancellationToken) is not null)
                continue;

            // The preview path makes the clip and stores it, so the warmer and a press share one recipe.
            Result<TtsTestResultDto> result = await config.TestVoiceAsync(
                Guid.Empty,
                new() { VoiceId = voiceId },
                cancellationToken
            );
            if (result.IsSuccess)
            {
                made++;
                failuresInARow = 0;
            }
            else if (++failuresInARow >= MaxFailuresInARow)
            {
                logger.LogWarning(
                    "TTS voice samples: {Failures} failures in a row, so the pass ends",
                    failuresInARow
                );
                break;
            }

            await Task.Delay(pause, cancellationToken);
        }

        return made;
    }
}
