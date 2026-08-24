// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Xml.Linq;
using FluentAssertions;
using NomNomzBot.Infrastructure.Tts;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves a caller-supplied <c>voiceId</c> cannot break out of the SSML <c>&lt;voice name='...'&gt;</c>
/// attribute (S041/F14): a value crafted to close the attribute and inject a sibling <c>&lt;speak&gt;</c>
/// element must appear only as escaped text, and the document must still parse as well-formed XML with the
/// original single-voice structure intact.
/// </summary>
public sealed class TtsSsmlEscapingTests
{
    private const string InjectionAttempt = "en-US-Voice'/><speak>injected</speak>";
    private const string AmpersandAndAngleBrackets = "voice & <bad>";

    [Fact]
    public void Azure_BuildSsml_escapes_an_attribute_breakout_voiceId()
    {
        string ssml = AzureTtsProvider.BuildSsml("hello", InjectionAttempt);

        ssml.Should().NotContain("'/><speak>");

        XDocument doc = XDocument.Parse(ssml);
        XElement voiceElement = doc.Root!.Elements().Single();
        voiceElement.Name.LocalName.Should().Be("voice");
        voiceElement.Attribute("name")!.Value.Should().Be(InjectionAttempt);
        doc.Descendants().Count(e => e.Name.LocalName == "speak").Should().Be(1);
    }

    [Fact]
    public void Azure_BuildSsml_escapes_ampersand_and_angle_brackets_in_voiceId()
    {
        string ssml = AzureTtsProvider.BuildSsml("hello", AmpersandAndAngleBrackets);

        ssml.Should().Contain("&amp;").And.NotContain("<bad>");

        XDocument doc = XDocument.Parse(ssml);
        doc.Root!.Elements()
            .Single()
            .Attribute("name")!
            .Value.Should()
            .Be(AmpersandAndAngleBrackets);
    }

    [Fact]
    public void Edge_BuildSsml_escapes_an_attribute_breakout_voiceId()
    {
        string ssml = EdgeTtsProvider.BuildSsml("hello", InjectionAttempt);

        ssml.Should().NotContain("'/><speak>");

        XDocument doc = XDocument.Parse(ssml);
        XElement voiceElement = doc.Root!.Elements().Single();
        voiceElement.Name.LocalName.Should().Be("voice");
        voiceElement.Attribute("name")!.Value.Should().Be(InjectionAttempt);
        doc.Descendants().Count(e => e.Name.LocalName == "speak").Should().Be(1);
    }

    [Fact]
    public void Edge_BuildSsml_escapes_ampersand_and_angle_brackets_in_voiceId()
    {
        string ssml = EdgeTtsProvider.BuildSsml("hello", AmpersandAndAngleBrackets);

        ssml.Should().Contain("&amp;").And.NotContain("<bad>");

        XDocument doc = XDocument.Parse(ssml);
        doc.Root!.Elements()
            .Single()
            .Attribute("name")!
            .Value.Should()
            .Be(AmpersandAndAngleBrackets);
    }
}
