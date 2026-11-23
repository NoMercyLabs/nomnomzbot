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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Obs;

public sealed class ObsOverlayInstallService(IObsControlService obs, IWidgetService widgets)
    : IObsOverlayInstallService
{
    private const string SourcePrefix = "NomNomzBot - ";
    private const string BrowserSourceKind = "browser_source";

    public async Task<Result<ObsOverlayInstallResult>> InstallAsync(
        Guid broadcasterId,
        string sceneName,
        IReadOnlyList<string> overlayKeys,
        string overlayOrigin,
        CancellationToken ct = default
    )
    {
        Result<IReadOnlyList<ObsSceneDto>> scenes = await obs.GetScenesAsync(broadcasterId, ct);
        if (scenes.IsFailure)
            return Result.Failure<ObsOverlayInstallResult>(scenes.ErrorMessage!, scenes.ErrorCode!);
        if (!scenes.Value.Any(s => s.Name == sceneName))
            return Result.Failure<ObsOverlayInstallResult>(
                $"OBS has no scene named \"{sceneName}\".",
                "NOT_FOUND"
            );

        if (!scenes.Value.Any(s => s.Name == IObsOverlayInstallService.NestedSceneName))
        {
            Result created = await obs.CreateSceneAsync(
                broadcasterId,
                IObsOverlayInstallService.NestedSceneName,
                ct
            );
            if (created.IsFailure)
                return Result.Failure<ObsOverlayInstallResult>(
                    created.ErrorMessage!,
                    created.ErrorCode!
                );
        }

        List<ObsOverlayInstallEntry> entries = [];
        foreach (string key in overlayKeys.Distinct())
            entries.Add(await InstallOneAsync(broadcasterId, key, overlayOrigin, ct));

        Result<bool> added = await EnsureNestedSceneInAsync(broadcasterId, sceneName, ct);
        if (added.IsFailure)
            return Result.Failure<ObsOverlayInstallResult>(added.ErrorMessage!, added.ErrorCode!);

        return Result.Success(
            new ObsOverlayInstallResult(
                IObsOverlayInstallService.NestedSceneName,
                added.Value,
                entries
            )
        );
    }

    private async Task<ObsOverlayInstallEntry> InstallOneAsync(
        Guid broadcasterId,
        string key,
        string overlayOrigin,
        CancellationToken ct
    )
    {
        string canonical = WidgetKeyAliases.Canonical(key)!;
        if (!FirstPartyWidgetCatalogue.SystemSurfaceNaturalKeys.Contains(canonical))
            return Failed(key, key, "This is not an overlay NomNomzBot can install.");

        Result<WidgetDetail> widget = await widgets.EnsureSystemWidgetAsync(
            broadcasterId.ToString(),
            canonical,
            ct
        );
        if (widget.IsFailure)
            return Failed(key, key, widget.ErrorMessage!);

        string sourceName = SourcePrefix + widget.Value.Name;
        if (!TryBuildUrl(widget.Value.OverlayUrl, overlayOrigin, out string url))
            return Failed(key, sourceName, "The overlay has no address yet.");

        ObsBrowserSource setup =
            widget.Value.ObsSetup
            ?? FirstPartyWidgetCatalogue.Find(canonical)?.ObsSetup
            ?? new(1920, 1080, false, false);

        Result<ObsBrowserSourceOutcome> result = await obs.UpsertBrowserSourceAsync(
            broadcasterId,
            new(
                IObsOverlayInstallService.NestedSceneName,
                sourceName,
                url,
                setup.Width,
                setup.Height,
                setup.ControlAudioViaObs,
                setup.ShutdownWhenHidden
            ),
            ct
        );
        if (result.IsFailure)
            return Failed(key, sourceName, result.ErrorMessage!);

        return new(
            key,
            sourceName,
            result.Value == ObsBrowserSourceOutcome.Created
                ? ObsOverlayInstallOutcome.Created
                : ObsOverlayInstallOutcome.Updated,
            null
        );
    }

    // Puts the nested scene into the picked scene once; a second run finds it there and adds nothing.
    private async Task<Result<bool>> EnsureNestedSceneInAsync(
        Guid broadcasterId,
        string sceneName,
        CancellationToken ct
    )
    {
        Result<IReadOnlyList<ObsSceneItemDto>> items = await obs.GetSceneItemListAsync(
            broadcasterId,
            sceneName,
            ct
        );
        if (items.IsFailure)
            return Result.Failure<bool>(items.ErrorMessage!, items.ErrorCode!);
        if (items.Value.Any(i => i.SourceName == IObsOverlayInstallService.NestedSceneName))
            return Result.Success(false);

        Result added = await obs.AddSceneItemAsync(
            broadcasterId,
            sceneName,
            IObsOverlayInstallService.NestedSceneName,
            ct
        );
        return added.IsFailure
            ? Result.Failure<bool>(added.ErrorMessage!, added.ErrorCode!)
            : Result.Success(true);
    }

    // The widget's own address carries its secret token; only its path and query are kept, on the origin the
    // streamer's OBS can reach. The result goes to OBS and nowhere else.
    private static bool TryBuildUrl(string? widgetUrl, string origin, out string url)
    {
        url = "";
        if (
            string.IsNullOrWhiteSpace(widgetUrl)
            || !Uri.TryCreate(widgetUrl, UriKind.Absolute, out Uri? parsed)
        )
            return false;

        url = origin.TrimEnd('/') + parsed.PathAndQuery;
        return true;
    }

    private static ObsOverlayInstallEntry Failed(string key, string sourceName, string error) =>
        new(key, sourceName, ObsOverlayInstallOutcome.Failed, error);
}
