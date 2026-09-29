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
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.BackgroundServices;

/// <summary>
/// Tells live chats the bot is going away when it stops with NO successor (a plain restart, a crash-restart,
/// a manual stop). A blue/green handover posts nothing: the incoming instance holds the standby claim, the
/// probe sees it, and chat never notices the swap. The line is spoken in the channel's personality through
/// the same composer the built-in commands use.
/// <para>
/// Registered AFTER the EventSub host, so (hosted services stop in reverse order) this runs while the
/// standby still holds its claim. Sends run in parallel under a hard budget, so a hung Helix call can never
/// hold shutdown open.
/// </para>
/// </summary>
public sealed class BotShutdownAnnouncementService(
    IServiceScopeFactory scopeFactory,
    IActiveInstanceGate instanceGate,
    ILogger<BotShutdownAnnouncementService> logger
) : IHostedService
{
    /// <summary>The whole announcement's budget — well inside the host shutdown timeout (30s default).</summary>
    internal static readonly TimeSpan SendBudget = TimeSpan.FromSeconds(5);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        budget.CancelAfter(SendBudget);

        try
        {
            if (await instanceGate.HasWaitingSuccessorAsync(budget.Token))
                return;

            List<(Guid Id, string Personality)> channels = await LoadLiveChannelsAsync(
                budget.Token
            );
            List<Task> sends =
            [
                .. channels.Select(c => AnnounceAsync(c.Id, c.Personality, budget.Token)),
            ];
            await Task.WhenAll(sends).WaitAsync(budget.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Shutdown announcement did not finish within {Budget}s; continuing shutdown.",
                SendBudget.TotalSeconds
            );
        }
        catch (Exception ex)
        {
            // Never let an announcement failure abort the rest of host teardown.
            logger.LogWarning(ex, "Shutdown announcement failed; continuing shutdown.");
        }
    }

    /// <summary>Channels the bot serves that are live right now — nobody reads an offline chat.</summary>
    private async Task<List<(Guid Id, string Personality)>> LoadLiveChannelsAsync(
        CancellationToken ct
    )
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var rows = await db
            .Channels.AsNoTracking()
            .Where(c =>
                c.Enabled && c.IsOnboarded && c.IsLive && c.Status == AuthEnums.ChannelStatus.Active
            )
            .Select(c => new { c.Id, c.Personality })
            .ToListAsync(ct);

        return [.. rows.Select(r => (r.Id, r.Personality))];
    }

    private async Task AnnounceAsync(Guid broadcasterId, string personality, CancellationToken ct)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IBuiltinResponseComposer composer =
                scope.ServiceProvider.GetRequiredService<IBuiltinResponseComposer>();
            IChatProvider chat = scope.ServiceProvider.GetRequiredService<IChatProvider>();

            string message = await composer.ComposeAsync(
                new BuiltinResponseRequest
                {
                    BroadcasterId = broadcasterId,
                    Personality = personality,
                    BuiltinKey = BuiltinResponseSlots.BotStatus.Key,
                    Slot = BuiltinResponseSlots.BotStatus.GoingOffline,
                    NeutralFallback =
                        ToneTemplateCatalog.ShippedTemplate(
                            BuiltinResponseSlots.BotStatus.Key,
                            BuiltinResponseSlots.BotStatus.GoingOffline
                        ) ?? string.Empty,
                },
                ct
            );
            if (message.Length == 0)
                return;

            if (!await chat.SendMessageAsync(broadcasterId, message, ct))
                logger.LogWarning(
                    "Shutdown announcement was not accepted for channel {BroadcasterId}.",
                    broadcasterId
                );
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "Shutdown announcement failed for channel {BroadcasterId}.",
                broadcasterId
            );
        }
    }
}
