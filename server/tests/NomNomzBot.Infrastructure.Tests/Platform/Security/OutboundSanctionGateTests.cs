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
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Infrastructure.Tests.Platform.Security;

/// <summary>
/// The guarantee, at the only place that can hold it: every outbound Twitch call funnels through the
/// transport's core send, so a write that nothing sanctioned never leaves the process.
///
/// <para>
/// This is here because reading the code could not settle the question. On 2026-09-04 a background handler
/// granted Twitch moderator on channels nobody asked, and a hand audit afterwards missed a second background
/// writer on the very first spot-check. These tests assert the behaviour a future handler cannot quietly
/// opt out of.
/// </para>
/// </summary>
public sealed class OutboundSanctionGateTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000ee01");

    [Theory]
    [InlineData("POST")]
    [InlineData("PATCH")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task An_outbound_write_that_nothing_sanctioned_never_reaches_twitch(string verb)
    {
        (ITwitchHelixTransport transport, CountingHandler http, _) = Build();

        Result sent = await transport.SendAsync(
            new(new HttpMethod(verb), "moderation/moderators", TwitchHelixAuth.User, Channel)
        );

        sent.IsFailure.Should().BeTrue();
        sent.ErrorCode.Should().Be("UNSANCTIONED_WRITE");
        http.Calls.Should().Be(0, "an unsanctioned change must not leave the process at all");
    }

    [Fact]
    public async Task A_write_a_user_action_sanctioned_is_allowed_through()
    {
        // The dashboard path: an endpoint that already proved the caller may do this opens the scope.
        (
            ITwitchHelixTransport transport,
            CountingHandler http,
            IOutboundSanctionAccessor sanctions
        ) = Build();

        using (
            sanctions.Begin(
                OutboundSanction.UserAction("moderation:moderator:write", Guid.NewGuid())
            )
        )
        {
            await transport.SendAsync(
                new(HttpMethod.Post, "moderation/moderators", TwitchHelixAuth.User, Channel)
            );
        }

        http.Calls.Should().Be(1);
    }

    [Fact]
    public async Task A_read_needs_no_sanction_because_it_changes_nothing_of_anybody_elses()
    {
        // Internal processing — dashboards, projections, imports — must stay unaffected, or the gate would
        // be turned off within a week for being in the way.
        (ITwitchHelixTransport transport, CountingHandler http, _) = Build();

        await transport.GetRawAsync(
            new(HttpMethod.Get, "moderation/moderators", TwitchHelixAuth.User, Channel)
        );

        http.Calls.Should().Be(1);
    }

    [Fact]
    public async Task The_sanction_lapses_when_its_scope_closes()
    {
        // A scope that leaked would let one sanctioned click authorise everything the process did afterwards.
        (
            ITwitchHelixTransport transport,
            CountingHandler http,
            IOutboundSanctionAccessor sanctions
        ) = Build();

        using (sanctions.Begin(OutboundSanction.UserAction("moderation:ban", null))) { }

        Result after = await transport.SendAsync(
            new(HttpMethod.Post, "moderation/bans", TwitchHelixAuth.User, Channel)
        );

        after.ErrorCode.Should().Be("UNSANCTIONED_WRITE");
        http.Calls.Should().Be(0);
    }

    [Fact]
    public void A_configured_basis_names_the_setting_that_authorised_it()
    {
        // A broadcaster asking "why did the bot time out my viewer" needs an answer that leads back to a
        // setting they saved, not merely "a rule fired".
        OutboundSanction sanction = OutboundSanction.ChannelConfiguration(
            "chat_filter:link-blocker"
        );

        sanction.Basis.Should().Be(OutboundSanctionBasis.ChannelConfiguration);
        sanction.Detail.Should().Be("chat_filter:link-blocker");
        sanction.ActorUserId.Should().BeNull("no person is present when stored configuration acts");
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static (
        ITwitchHelixTransport Transport,
        CountingHandler Http,
        IOutboundSanctionAccessor Sanctions
    ) Build()
    {
        CountingHandler handler = new();
        OutboundSanctionAccessor sanctions = new();
        NomNomzBot.Infrastructure.Platform.Transport.Helix.TwitchHelixTransport transport = new(
            new SingleClientFactory(new HttpClient(handler)),
            new NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix.FakeTwitchTokenResolver(),
            NomNomzBot.Infrastructure.Tests.Music.NullSystemCredentialsProvider.Instance,
            new NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix.CapturingEventBus(),
            sanctions,
            Microsoft
                .Extensions
                .Logging
                .Abstractions
                .NullLogger<NomNomzBot.Infrastructure.Platform.Transport.Helix.TwitchHelixTransport>
                .Instance
        );
        return (transport, handler, sanctions);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            return Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"data":[]}""",
                        System.Text.Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }
}
