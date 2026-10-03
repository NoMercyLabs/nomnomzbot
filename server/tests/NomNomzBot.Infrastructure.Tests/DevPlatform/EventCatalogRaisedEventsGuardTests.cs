// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// The Event Catalog offers every <c>IDomainEvent</c> that is not <see cref="EventVisibility.Internal"/> as a
/// script trigger, so an offered event nothing ever raises is a trigger that can never fire (the raid earning
/// once sat on an event the Twitch raid translator never published). This guard reads the real source under
/// <c>server/src</c> (outside the Domain project) and fails when an offered event has no construction site.
/// An event whose feature is not built yet is server plumbing: mark it Internal, never leave it offered.
/// </summary>
public sealed class EventCatalogRaisedEventsGuardTests
{
    [Fact]
    public void Every_offered_event_is_raised_somewhere()
    {
        string source = ReadAllNonDomainSource();
        List<string> offered = [.. OfferedEventNames().Distinct(StringComparer.Ordinal)];
        offered.Should().NotBeEmpty("the scan is broken if the catalog is empty");

        List<string> unraised = [.. offered.Where(name => !IsConstructed(source, name))];

        unraised
            .Should()
            .BeEmpty(
                "every event a script can pick as a trigger must be raised somewhere in server/src; "
                    + "a trigger nothing raises can never fire"
            );
    }

    // An Internal event is server plumbing that no SDK context offers, so it is no trigger to guard.
    private static IEnumerable<string> OfferedEventNames() =>
        new EventCatalog()
            .Descriptors.Where(d => d.Visibility != EventVisibility.Internal)
            .Select(d => d.ClrType.Name);

    // Counts `new Name`, a target-typed `Name x = new(...)`, and `Name M(...) => new(...)`.
    private static bool IsConstructed(string source, string eventName)
    {
        string name = Regex.Escape(eventName);
        return Regex.IsMatch(source, $@"\bnew\s+{name}\b")
            || Regex.IsMatch(source, $@"\b{name}\??\s+\w+\s*=\s*new\s*\(")
            || Regex.IsMatch(source, $@"\b{name}\??\s+\w+\s*\([^)]*\)\s*=>\s*new\s*\(");
    }

    private static string ReadAllNonDomainSource([CallerFilePath] string thisFilePath = "")
    {
        string serverRoot = Path.GetFullPath(
            Path.Combine(Path.GetDirectoryName(thisFilePath)!, "..", "..", "..")
        );
        string srcRoot = Path.Combine(serverRoot, "src");
        Directory.Exists(srcRoot).Should().BeTrue($"expected the real source at {srcRoot}");

        string domainRoot =
            Path.Combine(srcRoot, "NomNomzBot.Domain") + Path.DirectorySeparatorChar;
        string binRoot = Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar;
        string objRoot = Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;

        List<string> files =
        [
            .. Directory
                .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
                .Where(f =>
                    !f.StartsWith(domainRoot, StringComparison.OrdinalIgnoreCase)
                    && !f.Contains(binRoot, StringComparison.Ordinal)
                    && !f.Contains(objRoot, StringComparison.Ordinal)
                ),
        ];
        files.Should().NotBeEmpty("the scan is broken if it reads no source");

        return string.Join(
            "\n",
            files.SelectMany(File.ReadLines).Where(l => !l.TrimStart().StartsWith("//"))
        );
    }
}
