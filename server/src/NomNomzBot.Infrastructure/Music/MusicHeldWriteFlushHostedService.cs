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

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>Sends the favorite and banger writes that were still held when the bot last stopped.</summary>
public sealed class MusicHeldWriteFlushHostedService(
    IMusicWriteQueue queue,
    ILogger<MusicHeldWriteFlushHostedService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            int sent = await queue.FlushAllAsync(stoppingToken);
            if (sent > 0)
                logger.LogInformation("Sent {Count} held music write(s) after restart", sent);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not send the held music writes after restart");
        }
    }
}
