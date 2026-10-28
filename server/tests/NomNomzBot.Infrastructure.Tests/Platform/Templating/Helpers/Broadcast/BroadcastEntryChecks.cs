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

using FluentAssertions;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Tests.Localization;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Broadcast;

/// <summary>The checks every broadcast provider shares: both help strings, the Dutch one different, and a sample.</summary>
internal static class BroadcastEntryChecks
{
    public static List<TemplateHelperEntry> EntriesOf(
        ITemplateHelperRegistry registry,
        IReadOnlyCollection<string> namespaces
    ) =>
        [
            .. registry.All.Where(e =>
                registry.ProviderFor(e.Key) is { } provider
                && namespaces.Contains(provider.Namespace)
            ),
        ];

    public static void AssertAll(
        ITemplateHelperRegistry registry,
        IReadOnlyCollection<string> namespaces,
        int minimumEntries
    )
    {
        DashboardStringsXmlCatalog strings = new();
        List<TemplateHelperEntry> entries = EntriesOf(registry, namespaces);

        entries.Should().HaveCountGreaterThanOrEqualTo(minimumEntries);
        foreach (TemplateHelperEntry entry in entries)
        {
            strings
                .TryGetEnglish(entry.Description.Key, out string english)
                .Should()
                .BeTrue(entry.Key);
            strings.TryGetDutch(entry.Description.Key, out string dutch).Should().BeTrue(entry.Key);
            english.Should().NotBeNullOrWhiteSpace(entry.Key);
            dutch.Should().NotBe(english, entry.Key);
            entry.Sample.Should().NotBeNullOrEmpty(entry.Key);
        }
    }
}
