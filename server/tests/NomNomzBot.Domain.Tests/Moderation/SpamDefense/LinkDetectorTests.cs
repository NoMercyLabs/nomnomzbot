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

using System.Text.RegularExpressions;
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

    // A pattern that backtracks without end on this input, with the shortest timeout .NET allows, forces a
    // genuine RegexMatchTimeoutException regardless of machine speed.
    private static readonly Regex TimingOutPattern = new(
        @"(?<scheme>(a+)+$)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(1)
    );

    [Fact]
    public void AMatchTimeout_ReportsTheSchemeUrlsOfTheMessage_InsteadOfThrowing()
    {
        string message = new string('a', 40) + "! see https://shop.example.com/x";

        IReadOnlyList<DetectedLink> links = LinkDetector.Find(message, TimingOutPattern);

        DetectedLink link = links.Should().ContainSingle().Subject;
        link.Host.Should().Be("shop.example.com");
        link.Text.Should().Be("https://shop.example.com/x");
    }

    [Fact]
    public void AMatchTimeout_WithNoSchemeUrl_ReportsTheMessageAsAnUnreadableLink()
    {
        string message = new string('a', 40) + "! shop.example.com";

        IReadOnlyList<DetectedLink> links = LinkDetector.Find(message, TimingOutPattern);

        DetectedLink link = links.Should().ContainSingle().Subject;
        link.Host.Should().BeEmpty();
        link.Text.Should().Contain("shop.example.com");
    }

    [Theory]
    [InlineData("i use asp.net for that")]
    [InlineData("node.js is fine")]
    [InlineData("vue.js or next.js?")]
    [InlineData("nuxt.js and three.js")]
    [InlineData("socket.io works")]
    [InlineData("d3.js charts, I like asp.net.")]
    public void ATechName_IsNotALink(string message)
    {
        LinkDetector.Find(message).Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://asp.net/core", "asp.net")]
    [InlineData("asp.net/core", "asp.net")]
    [InlineData("see socket.io/docs", "socket.io")]
    public void ATechNameWithASchemeOrAPath_IsStillALink(string message, string expectedHost)
    {
        LinkDetector.Find(message).Select(l => l.Host).Should().Equal(expectedHost);
    }
}
