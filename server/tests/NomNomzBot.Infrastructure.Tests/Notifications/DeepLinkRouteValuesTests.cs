// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// Every deep link an attention item can carry names a page of the dashboard by its page address.
/// The producers hold the literals, so this test reads their source and checks each literal.
/// </summary>
public sealed class DeepLinkRouteValuesTests
{
    private static readonly string[] PageAddresses =
    [
        "home",
        "chat/one-channel",
        "chat/all-my-channels",
        "commands",
        "triggers-timers/chat-triggers",
        "triggers-timers/voice-triggers",
        "triggers-timers/timers",
        "event-responses/responses",
        "event-responses/alerts",
        "library/quotes",
        "library/random-responses",
        "library/sounds",
        "library/files",
        "pipelines",
        "overlays",
        "scripts",
        "people/overview",
        "people/queue",
        "people/viewers",
        "people/history",
        "people/rules",
        "people/roles",
        "channel-points",
        "economy",
        "games",
        "giveaways",
        "music/now-playing",
        "music/song-requests",
        "tts",
        "media-share",
        "stream/schedule",
        "analytics",
        "obs",
        "vtube-studio",
        "discord",
        "webhooks",
        "custom-events",
        "supporters",
        "federation",
        "api-devices",
        "gallery/bundles",
        "integrations",
        "features",
        "settings",
        "my-data",
        "admin",
    ];

    [Fact]
    public void EveryDeepLinkLiteralInTheProducers_IsANewPageAddress()
    {
        string notificationsDir = Path.Combine(
            RepoRoot(),
            "server",
            "src",
            "NomNomzBot.Infrastructure",
            "Notifications"
        );

        List<string> found = [];
        foreach (
            string file in Directory.GetFiles(notificationsDir, "*.cs", SearchOption.AllDirectories)
        )
        {
            string text = File.ReadAllText(file);
            foreach (Match assignment in Regex.Matches(text, @"DeepLinkRoute:\s*([^,]*),"))
            {
                foreach (Match literal in Regex.Matches(assignment.Groups[1].Value, "\"([^\"]*)\""))
                    found.Add(literal.Groups[1].Value);
            }
        }

        found.Should().NotBeEmpty("the producers must be found for this check to mean anything");
        found.Distinct().Should().BeSubsetOf(PageAddresses);
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "server", "src")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root from {AppContext.BaseDirectory}."
        );
    }
}
