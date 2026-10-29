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

using System.Web;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Stoney 2026-10-29: "easier to change these values that make the free edge tts work since microsoft rotates
/// them". The Chromium version and the trusted client token come from <c>Tts:Edge</c> settings, so a rotation is
/// a setting and a restart, not a code change and a release. One Chromium version drives every place that must
/// agree (Sec-MS-GEC-Version and the User-Agent), and a blank or malformed setting keeps the shipped value.
/// </summary>
public sealed class EdgeClientIdentityTests
{
    [Fact]
    public void With_no_settings_the_shipped_values_are_used()
    {
        EdgeClientIdentity identity = EdgeClientIdentity.Resolve(new(), out List<string> rejected);

        identity.ChromiumVersion.Should().Be(EdgeTtsOptions.DefaultChromiumVersion);
        identity.TrustedClientToken.Should().Be(EdgeTtsOptions.DefaultTrustedClientToken);
        rejected.Should().BeEmpty();
    }

    [Fact]
    public void One_chromium_version_drives_the_gec_version_and_the_user_agent()
    {
        EdgeClientIdentity identity = EdgeClientIdentity.Resolve(
            new() { ChromiumVersion = "150.0.3700.12" },
            out _
        );

        identity.SecMsGecVersion.Should().Be("2-150.0.3700.12");
        identity.UserAgent.Should().Contain("Chrome/150.0.0.0").And.Contain("Edg/150.0.0.0");
        identity.UserAgent.Should().NotContain("143");
    }

    [Fact]
    public void A_new_trusted_client_token_is_used_as_given()
    {
        EdgeClientIdentity identity = EdgeClientIdentity.Resolve(
            new() { TrustedClientToken = "0123456789ABCDEF0123456789abcdef" },
            out List<string> rejected
        );

        identity.TrustedClientToken.Should().Be("0123456789ABCDEF0123456789abcdef");
        rejected.Should().BeEmpty();
    }

    // The compose file passes an empty string for a variable that is not set.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_setting_keeps_the_shipped_value(string blank)
    {
        EdgeClientIdentity identity = EdgeClientIdentity.Resolve(
            new() { ChromiumVersion = blank, TrustedClientToken = blank },
            out List<string> rejected
        );

        identity.ChromiumVersion.Should().Be(EdgeTtsOptions.DefaultChromiumVersion);
        identity.TrustedClientToken.Should().Be(EdgeTtsOptions.DefaultTrustedClientToken);
        rejected.Should().BeEmpty();
    }

    // A typo must not silence every voice on the platform: the shipped value stays, and the key is named.
    [Theory]
    [InlineData("latest", "6A5AA1D4EAFF4E9FB37E23D68491D6F4", "ChromiumVersion")]
    [InlineData("150", "6A5AA1D4EAFF4E9FB37E23D68491D6F4", "ChromiumVersion")]
    [InlineData("150.0.3700.12", "not-a-token", "TrustedClientToken")]
    [InlineData("150.0.3700.12", "6A5AA1D4EAFF4E9FB37E23D68491D6F", "TrustedClientToken")]
    public void A_malformed_setting_keeps_the_shipped_value_and_is_named(
        string version,
        string token,
        string rejectedKey
    )
    {
        EdgeClientIdentity identity = EdgeClientIdentity.Resolve(
            new() { ChromiumVersion = version, TrustedClientToken = token },
            out List<string> rejected
        );

        rejected.Should().Equal(rejectedKey);
        if (rejectedKey == "ChromiumVersion")
            identity.ChromiumVersion.Should().Be(EdgeTtsOptions.DefaultChromiumVersion);
        else
            identity.TrustedClientToken.Should().Be(EdgeTtsOptions.DefaultTrustedClientToken);
    }

    [Fact]
    public async Task The_handshake_carries_the_configured_token_and_version_and_signs_with_that_token()
    {
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-10-29T18:00:00Z"));
        List<Uri> urls = [];
        EdgeTtsProvider provider = new(
            time,
            Substitute.For<IHttpClientFactory>(),
            NullLogger<EdgeTtsProvider>.Instance,
            Options.Create(
                new EdgeTtsOptions
                {
                    ChromiumVersion = "150.0.3700.12",
                    TrustedClientToken = "0123456789ABCDEF0123456789ABCDEF",
                }
            ),
            (_, url, _) =>
            {
                urls.Add(url);
                return Task.CompletedTask;
            }
        );

        using EdgeTtsProvider.EdgeConnection? connection = await provider.ConnectWithRetryAsync(
            CancellationToken.None
        );

        System.Collections.Specialized.NameValueCollection query = HttpUtility.ParseQueryString(
            urls.Single().Query
        );
        query["TrustedClientToken"].Should().Be("0123456789ABCDEF0123456789ABCDEF");
        query["Sec-MS-GEC-Version"].Should().Be("2-150.0.3700.12");
        query["Sec-MS-GEC"]
            .Should()
            .Be(EdgeSecMsGecToken.Generate(time.GetUtcNow(), "0123456789ABCDEF0123456789ABCDEF"));
    }
}
