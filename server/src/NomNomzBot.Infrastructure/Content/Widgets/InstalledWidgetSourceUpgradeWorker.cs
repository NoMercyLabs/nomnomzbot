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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Content.Widgets;

/// <summary>
/// Brings every installed first-party widget that the channel never edited up to the catalogue's current source.
/// The gallery seeder only bumps <see cref="WidgetGalleryItem.SourceRevision"/>; an installed widget keeps the copy
/// it was installed with until this worker rebuilds it. A widget with its own saved version
/// (<see cref="Widget.IsSourceCustomized"/>) is left for the streamer's explicit reset.
/// It runs in the background after the host has started, never in the seed phase: a rebuild takes about 4 s per
/// widget, and doing it before the host listens kept /health/ready down for minutes on every deploy. Until a rebuild
/// finishes, the widget keeps serving its last good build (the active version only moves after a successful build,
/// and a failed build leaves the install revision alone so the next start retries).
/// </summary>
public sealed class InstalledWidgetSourceUpgradeWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<InstalledWidgetSourceUpgradeWorker> logger,
    IHostApplicationLifetime? lifetime = null
) : BackgroundService
{
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromMinutes(30);

    internal const string LeaseResourceName = "installed-widget-source-upgrade";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await WaitForApplicationStartedAsync(stoppingToken);
            await UpgradeAllAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping; the next start picks up what is left.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Installed widget source upgrade stopped before it finished.");
        }
    }

    private async Task WaitForApplicationStartedAsync(CancellationToken stoppingToken)
    {
        if (lifetime is null)
        {
            await Task.Yield(); // no host lifetime (bare provider): still hand control back to StartAsync first.
            return;
        }

        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using CancellationTokenRegistration startedRegistration =
            lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(stoppingToken);
    }

    private async Task UpgradeAllAsync(CancellationToken ct)
    {
        using IServiceScope leaseScope = scopeFactory.CreateScope();
        IRunOnceGuard guard = leaseScope.ServiceProvider.GetRequiredService<IRunOnceGuard>();
        await using IAsyncDisposable? lease = await guard.TryAcquireAsync(
            LeaseResourceName,
            LeaseTtl,
            ct
        );
        if (lease is null)
            return; // another instance is upgrading this startup.

        List<Widget> stale = await FindStaleUntouchedInstallsAsync(ct);
        Dictionary<Guid, List<string>> failuresByChannel = [];
        foreach (Widget widget in stale)
        {
            string? failure = await UpgradeOneAsync(widget, ct);
            if (failure is not null)
                AddFailure(failuresByChannel, widget.BroadcasterId, failure);
        }

        foreach ((Guid channelId, List<string> failures) in failuresByChannel)
            logger.LogWarning(
                "Widget source upgrade failed for {Count} widget(s) of channel {ChannelId}; they keep their last good build and retry next start. First error: {Error}",
                failures.Count,
                channelId,
                failures[0]
            );
    }

    private async Task<List<Widget>> FindStaleUntouchedInstallsAsync(CancellationToken ct)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        Dictionary<Guid, int> revisionByItemId = await db
            .WidgetGalleryItems.IgnoreQueryFilters()
            .Where(item => item.TrustTier == "first_party" && item.DeletedAt == null)
            .ToDictionaryAsync(item => item.Id, item => item.SourceRevision, ct);
        List<Guid> itemIds = [.. revisionByItemId.Keys];

        List<Widget> installs = await db
            .Widgets.IgnoreQueryFilters()
            .Where(widget =>
                widget.DeletedAt == null
                && widget.GalleryItemId != null
                && itemIds.Contains(widget.GalleryItemId.Value)
            )
            .ToListAsync(ct);
        installs =
        [
            .. installs.Where(widget =>
                (widget.InstalledSourceRevision ?? 0)
                < revisionByItemId[widget.GalleryItemId!.Value]
            ),
        ];

        List<Guid> staleIds = [.. installs.Select(widget => widget.Id)];
        Dictionary<Guid, int> latestVersions = await db
            .WidgetVersions.IgnoreQueryFilters()
            // A failed build never went live, so it is no edit of the channel's.
            .Where(version =>
                staleIds.Contains(version.WidgetId) && version.BuildStatus == "success"
            )
            .GroupBy(version => version.WidgetId)
            .Select(group => new { WidgetId = group.Key, Latest = group.Max(v => v.VersionNumber) })
            .ToDictionaryAsync(row => row.WidgetId, row => row.Latest, ct);

        return
        [
            .. installs.Where(widget =>
                !widget.IsSourceCustomized(
                    latestVersions.TryGetValue(widget.Id, out int number) ? number : null
                )
            ),
        ];
    }

    private async Task<string?> UpgradeOneAsync(Widget widget, CancellationToken ct)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            IWidgetService widgets = scope.ServiceProvider.GetRequiredService<IWidgetService>();
            Result<WidgetDetail> result = await widgets.UpdateFromGalleryAsync(
                widget.BroadcasterId.ToString(),
                widget.Id.ToString(),
                ct
            );
            return result.IsSuccess ? null : $"{widget.Id}: {result.ErrorMessage}";
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return $"{widget.Id}: {ex.Message}";
        }
    }

    private static void AddFailure(
        Dictionary<Guid, List<string>> failuresByChannel,
        Guid channelId,
        string failure
    )
    {
        if (!failuresByChannel.TryGetValue(channelId, out List<string>? failures))
            failuresByChannel[channelId] = failures = [];
        failures.Add(failure);
    }
}
