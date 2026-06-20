// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Platform.Transport.Helix;

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

/// <summary>
/// Proves the auth handler attaches the app's <c>Client-Id</c> and the <em>resolved Twitch access token</em>
/// as the bearer — and that the bearer is the token from the request options, never a tenant Guid.
/// </summary>
public class TwitchAuthHeaderHandlerTests
{
    private const string TwitchAccessToken = "twitch-user-access-token-abc123";
    private static readonly Guid TenantGuid = Guid.Parse("0195e0d2-7777-7777-7777-000000000001");

    private static (HttpClient Client, RecordingHelixHandler Wire) BuildClient()
    {
        RecordingHelixHandler wire = new([() => new HttpResponseMessage(HttpStatusCode.OK)]);
        TwitchAuthHeaderHandler auth = new(
            Options.Create(new TwitchOptions { ClientId = "client-id-xyz" })
        )
        {
            InnerHandler = wire,
        };
        return (new HttpClient(auth), wire);
    }

    [Fact]
    public async Task SendAsync_AttachesClientIdAndResolvedBearer()
    {
        (HttpClient client, RecordingHelixHandler wire) = BuildClient();

        using HttpRequestMessage request = new(
            HttpMethod.Get,
            "https://api.twitch.tv/helix/users?id=42"
        );
        request.Options.Set(HelixRequestOptions.AccessToken, TwitchAccessToken);

        await client.SendAsync(request);

        RecordedRequest sent = wire.Requests.Should().ContainSingle().Subject;
        sent.ClientId.Should().Be("client-id-xyz");
        sent.AuthorizationParameter.Should().Be(TwitchAccessToken);
    }

    [Fact]
    public async Task SendAsync_NeverSendsTenantGuidAsBearer()
    {
        (HttpClient client, RecordingHelixHandler wire) = BuildClient();

        // The token the transport resolved is a Twitch string token; the tenant Guid stays internal.
        using HttpRequestMessage request = new(
            HttpMethod.Get,
            "https://api.twitch.tv/helix/channels?broadcaster_id=99"
        );
        request.Options.Set(HelixRequestOptions.AccessToken, TwitchAccessToken);

        await client.SendAsync(request);

        RecordedRequest sent = wire.Requests.Should().ContainSingle().Subject;
        sent.AuthorizationParameter.Should().NotBe(TenantGuid.ToString());
        sent.AuthorizationParameter.Should().Be(TwitchAccessToken);
        // Defensive: the bearer must not parse as a Guid at all.
        Guid.TryParse(sent.AuthorizationParameter, out _).Should().BeFalse();
    }
}
