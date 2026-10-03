// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.Playwright;
using NomNomzBot.E2E.Tests.Harness;

namespace NomNomzBot.E2E.Tests.Editor;

/// <summary>
/// A first-party widget is the code streamers open and copy, so it must pass the editor's own type check with
/// the SDK types the dashboard hands the editor for that widget. The source comes from the working tree (the
/// code under review); the types come from the instance for the channel's installed copy of the widget, or a
/// temporary copy this test installs and removes.
/// Converting a widget to the typed SDK adds one row here.
/// </summary>
public sealed class FirstPartyWidgetTypeCheckingTests : EditorPageTest
{
    // Natural key (the .vue file name) and the catalogue's gallery name (FirstPartyWidgetCatalogue.cs).
    [E2ETheory]
    [InlineData("tts_audio", "Audio Source")]
    [InlineData("tts_caption", "TTS Caption")]
    [InlineData("countdown_timer", "Countdown / Timer")]
    public async Task A_converted_widget_has_no_type_problems(string naturalKey, string galleryName)
    {
        string? installedId = await InstalledWidgetIdAsync(galleryName);
        string widgetId = installedId ?? await InstallAsync(galleryName);
        try
        {
            string sdkTypes = await WidgetTypesAsync(widgetId);
            string source = await File.ReadAllTextAsync(WidgetSourcePath(naturalKey));

            await OpenAsync("vue", "App.vue", source, sdkTypes);

            Assert.Equal(0, await WorkerDiagnosticCountAsync("App.vue.__script.ts"));
            Assert.Empty(await DiagnosticLinesAsync("App.vue"));
        }
        finally
        {
            // Only a copy this test installed is removed; the channel's own widgets are never touched.
            if (installedId is null)
                await DeleteAsync(widgetId);
        }
    }

    // A channel without the widget gets a temporary copy, so the check never depends on what is installed.
    private async Task<string> InstallAsync(string galleryName)
    {
        IAPIResponse gallery = await Page.APIRequest.GetAsync(
            $"{E2ESettings.BaseUrl}/api/v1/widget-gallery?take=100&search={Uri.EscapeDataString(galleryName)}",
            new() { Headers = AuthHeaders() }
        );
        Assert.Equal(200, gallery.Status);
        string? galleryItemId = (await gallery.JsonAsync())!
            .Value.GetProperty("data")
            .EnumerateArray()
            .Where(item => item.GetProperty("name").GetString() == galleryName)
            .Select(item => item.GetProperty("id").GetString())
            .FirstOrDefault();
        Assert.True(galleryItemId is not null, $"The gallery has no item named '{galleryName}'.");

        IAPIResponse installed = await Page.APIRequest.PostAsync(
            $"{E2ESettings.BaseUrl}/api/v1/channels/{TokenTenant()}/widgets/install/{galleryItemId}",
            new() { Headers = AuthHeaders() }
        );
        Assert.Equal(201, installed.Status);
        return (await installed.JsonAsync())!
            .Value.GetProperty("data")
            .GetProperty("id")
            .GetString()!;
    }

    private async Task DeleteAsync(string widgetId)
    {
        IAPIResponse deleted = await Page.APIRequest.DeleteAsync(
            $"{E2ESettings.BaseUrl}/api/v1/channels/{TokenTenant()}/widgets/{widgetId}",
            new() { Headers = AuthHeaders() }
        );
        Assert.Equal(204, deleted.Status);
    }

    // Matched on the gallery item's name, which the code owns: a streamer may rename their installed copy.
    private async Task<string?> InstalledWidgetIdAsync(string galleryName)
    {
        IAPIResponse list = await Page.APIRequest.GetAsync(
            $"{E2ESettings.BaseUrl}/api/v1/channels/{TokenTenant()}/widgets?take=100",
            new() { Headers = AuthHeaders() }
        );
        Assert.Equal(200, list.Status);

        foreach (
            JsonElement widget in (await list.JsonAsync())!
                .Value.GetProperty("data")
                .EnumerateArray()
        )
        {
            if (
                widget.TryGetProperty("galleryItemId", out JsonElement galleryItem)
                && galleryItem.GetString() is { } galleryItemId
                && await GalleryNameAsync(galleryItemId) == galleryName
            )
                return widget.GetProperty("id").GetString()!;
        }

        return null;
    }

    private async Task<string?> GalleryNameAsync(string galleryItemId)
    {
        IAPIResponse item = await Page.APIRequest.GetAsync(
            $"{E2ESettings.BaseUrl}/api/v1/widget-gallery/{galleryItemId}",
            new() { Headers = AuthHeaders() }
        );
        return item.Status == 200
            ? (await item.JsonAsync())!.Value.GetProperty("data").GetProperty("name").GetString()
            : null;
    }

    private async Task<string> WidgetTypesAsync(string widgetId)
    {
        IAPIResponse types = await Page.APIRequest.GetAsync(
            $"{E2ESettings.BaseUrl}/api/v1/sdk/types.d.ts?context=widget&widget={Uri.EscapeDataString(widgetId)}",
            new() { Headers = AuthHeaders() }
        );
        Assert.Equal(200, types.Status);
        return await types.TextAsync();
    }

    private static string WidgetSourcePath(string naturalKey)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (
            directory is not null
            && !Directory.Exists(Path.Combine(directory.FullName, "server", "src"))
        )
            directory = directory.Parent;

        return directory is null
            ? throw new DirectoryNotFoundException(
                "No folder above the test assembly holds server/src."
            )
            : Path.Combine(
                directory.FullName,
                "server",
                "src",
                "NomNomzBot.Infrastructure",
                "Content",
                "Widgets",
                "Assets",
                $"{naturalKey}.vue"
            );
    }
}
