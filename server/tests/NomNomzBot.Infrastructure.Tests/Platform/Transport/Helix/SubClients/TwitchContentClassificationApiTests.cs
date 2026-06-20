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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Transport.Helix.SubClients;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix.SubClients.Fakes;

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix.SubClients;

/// <summary>
/// Behavioural tests for the Content Classification Labels sub-client: the method builds the exact Helix
/// request (verb / path / App auth / optional locale query) and maps the list response. No tenant resolution
/// or scope gating is involved — this is a public App-token read — so the capturing transport alone proves the
/// request shape and the DTO mapping with no HTTP.
/// </summary>
public class TwitchContentClassificationApiTests
{
    [Fact]
    public async Task GetContentClassificationLabels_WithLocale_BuildsAppTokenQuery_MapsDtos()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchContentClassificationLabel>
            {
                new(
                    "DrugsIntoxication",
                    "Drugs, Intoxication, or Excessive Tobacco Use",
                    "Excessive tobacco glorification or promotion, any marijuana consumption/use, legal drug and alcohol induced intoxication, discussions of illegal drugs."
                ),
            },
        };
        TwitchContentClassificationApi api = new(transport);

        Result<IReadOnlyList<TwitchContentClassificationLabel>> result =
            await api.GetContentClassificationLabelsAsync("en-US");

        result.IsSuccess.Should().BeTrue();
        TwitchContentClassificationLabel label = result.Value.Should().ContainSingle().Subject;
        label.Id.Should().Be("DrugsIntoxication");
        label.Name.Should().Be("Drugs, Intoxication, or Excessive Tobacco Use");
        label
            .Description.Should()
            .Be(
                "Excessive tobacco glorification or promotion, any marijuana consumption/use, legal drug and alcohol induced intoxication, discussions of illegal drugs."
            );

        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("content_classification_labels");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport.LastRequest.BroadcasterId.Should().BeNull();
        transport.LastRequest.Query.Should().Contain(q => q.Key == "locale" && q.Value == "en-US");
    }

    [Fact]
    public async Task GetContentClassificationLabels_NullLocale_OmitsLocale()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchContentClassificationLabel>(),
        };
        TwitchContentClassificationApi api = new(transport);

        await api.GetContentClassificationLabelsAsync(null);

        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("content_classification_labels");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "locale");
    }
}
