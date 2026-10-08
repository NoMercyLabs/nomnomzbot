// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Domain.Tests.Moderation.SpamDefense;

/// <summary>
/// The links rule used to read only <c>http(s)://</c>, so <c>example.com/x</c> walked straight through. These
/// tests pin both halves: a bare domain is a link (also when disguised with fullwidth or homoglyph letters),
/// and ordinary words with a dot are not.
/// </summary>
public sealed class LinkDetectorTests
{
    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("check example.com now", "example.com")]
    [InlineData("example.com/x", "example.com")]
    [InlineData("sub.example.co.uk", "sub.example.co.uk")]
    [InlineData("www.example.com", "www.example.com")]
    [InlineData("EXAMPLE.COM", "example.com")]
    [InlineData("example.com:8080/path?q=1", "example.com")]
    [InlineData("https://example.com/x", "example.com")]
    [InlineData("http://user@example.com/x", "example.com")]
    [InlineData("https://free-nitro.example", "free-nitro.example")]
    [InlineData("go to example.com.", "example.com")]
    public void FindsALinkAndItsHost(string message, string expectedHost)
    {
        IReadOnlyList<DetectedLink> links = LinkDetector.Find(message);

        links.Should().ContainSingle().Which.Host.Should().Be(expectedHost);
    }

    [Theory]
    [InlineData("ｅｘａｍｐｌｅ．ｃｏｍ")] // fullwidth example.com
    [InlineData("еxample.com")] // Cyrillic е
    [InlineData("exam​ple.com")] // zero-width space inside the name
    [InlineData("example.cоm")] // Cyrillic о in the TLD
    public void FoldsDisguisedLettersBeforeLookingForTheDomain(string message)
    {
        IReadOnlyList<DetectedLink> links = LinkDetector.Find(message);

        links.Should().ContainSingle().Which.Host.Should().Be("example.com");
    }

    [Theory]
    [InlineData("end.of sentence")]
    [InlineData("I waited. Then left")]
    [InlineData("update to v1.2 today")]
    [InlineData("it costs 3.5 coins")]
    [InlineData("version 10.0.1 is out")]
    [InlineData("wait...what")]
    [InlineData("e.g. this")]
    [InlineData("well.. ok")]
    [InlineData("mail me at someone@example.com")]
    [InlineData("see readme.txt")]
    [InlineData("")]
    public void DoesNotTreatOrdinaryTextAsALink(string message)
    {
        LinkDetector.Find(message).Should().BeEmpty();
    }

    [Fact]
    public void ReturnsEveryLinkInTheMessage()
    {
        IReadOnlyList<DetectedLink> links = LinkDetector.Find(
            "a.example.com and https://b.example.net/x and plain.org/y"
        );

        links.Select(l => l.Host).Should().Equal("a.example.com", "b.example.net", "plain.org");
    }

    [Fact]
    public void ANullMessageHasNoLinks()
    {
        LinkDetector.Find(null).Should().BeEmpty();
    }
}
