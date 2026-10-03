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
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// The SDK editor hover text is the C# <c>/// &lt;summary&gt;</c> of an event or field. The reader turns that XML
/// into one plain line that is safe inside a JSDoc comment.
/// </summary>
public sealed class XmlDocSummaryReaderTests
{
    private const string Xml = """
        <doc><members>
        <member name="T:A.B.Thing">
          <summary>
            Published when a <c>viewer</c> follows; see <see cref="T:A.B.Other"/>
            and <see cref="P:A.B.Thing.Name"/> and <paramref name="x"/>, never <see langword="null"/>.
          </summary>
        </member>
        <member name="P:A.B.Thing.Name"><summary>Ends early */ here.</summary></member>
        <member name="P:A.B.Thing.NoSummary"><remarks>Only remarks.</remarks></member>
        </members></doc>
        """;

    [Fact]
    public void A_summary_with_tags_and_a_newline_comes_out_as_one_plain_line()
    {
        XmlDocSummaryReader reader = XmlDocSummaryReader.Parse(Xml);

        reader
            .Summary("T:A.B.Thing")
            .Should()
            .Be("Published when a viewer follows; see Other and Name and x, never null.");
    }

    [Fact]
    public void A_comment_terminator_in_the_text_is_escaped()
    {
        XmlDocSummaryReader reader = XmlDocSummaryReader.Parse(Xml);

        reader.Summary("P:A.B.Thing.Name").Should().Be("Ends early *\\/ here.");
    }

    [Fact]
    public void A_member_without_a_summary_or_not_in_the_file_returns_nothing()
    {
        XmlDocSummaryReader reader = XmlDocSummaryReader.Parse(Xml);

        reader.Summary("P:A.B.Thing.NoSummary").Should().BeNull();
        reader.Summary("P:A.B.Thing.Missing").Should().BeNull();
    }

    [Fact]
    public void The_domain_assembly_summary_of_a_real_event_field_is_read_from_its_xml_file()
    {
        XmlDocSummaryReader? reader = XmlDocSummaryReader.ForAssembly(
            typeof(NomNomzBot.Domain.Community.Events.FollowEvent).Assembly,
            null
        );

        reader.Should().NotBeNull("NomNomzBot.Domain.xml must sit next to the dll");
        reader
            .PropertySummary(
                typeof(NomNomzBot.Domain.Community.Events.FollowEvent).GetProperty("UserLogin")!
            )
            .Should()
            .Be("The login name of the viewer (lowercase).");
    }
}
