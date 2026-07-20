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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Application.Contracts.Kick;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Integrations.Services;
using NomNomzBot.Application.Marketplace.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Supporters.Services;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Identity.Enums;
using Xunit;

namespace NomNomzBot.Api.Tests.Requirements.Auth;

/// <summary>
/// REQUIREMENT (identity-auth §3.4 + integrations-oauth §3.2): every provider in
/// <c>AuthEnums.IntegrationProvider</c> must be genuinely CONNECTABLE — a registered connect path, the shared
/// crypto token vault (<see cref="IIntegrationTokenVault"/>), and a feature service that consumes the connection.
/// The spec's connect model is descriptor-driven: "a new provider plugs in as one descriptor... no new controller
/// or service" (<see cref="IOAuthProviderRegistry"/>). These tests DEMAND that coverage against the real
/// container; a provider with no connect descriptor, no vault, or no feature service is a real RED, not a snapshot.
/// </summary>
public sealed class IntegrationOAuthCoverageTests : IClassFixture<DiHostFixture>
{
    private readonly DiHostFixture _host;

    public IntegrationOAuthCoverageTests(DiHostFixture host) => _host = host;

    /// <summary>
    /// The OAuth-model integration providers — those whose connect is an OAuth 2.0 authorization grant, so they
    /// belong in the unified <see cref="IOAuthProviderRegistry"/>. Excludes twitch (the identity/login provider,
    /// connected via the bespoke <see cref="IAuthService"/> path), azure_tts / elevenlabs (API-key, not OAuth),
    /// and marketplace (internal bundle store) — those connect models are asserted separately, not as OAuth.
    /// </summary>
    public static readonly TheoryData<string> OAuthModelProviders = new()
    {
        AuthEnums.IntegrationProvider.Spotify,
        AuthEnums.IntegrationProvider.Discord,
        AuthEnums.IntegrationProvider.YouTube,
        AuthEnums.IntegrationProvider.Kick,
        AuthEnums.IntegrationProvider.Patreon,
        AuthEnums.IntegrationProvider.Shopify,
        AuthEnums.IntegrationProvider.Treatstream,
    };

    /// <summary>Every integration provider mapped to a feature-service contract that consumes its connection.</summary>
    public static readonly TheoryData<string, Type> ProviderFeatureServices = new()
    {
        { AuthEnums.IntegrationProvider.Twitch, typeof(ITwitchUsersApi) },
        { AuthEnums.IntegrationProvider.Spotify, typeof(IMusicService) },
        { AuthEnums.IntegrationProvider.Discord, typeof(IDiscordGuildService) },
        { AuthEnums.IntegrationProvider.YouTube, typeof(IYouTubeLiveChatClient) },
        { AuthEnums.IntegrationProvider.Kick, typeof(IKickApiClient) },
        { AuthEnums.IntegrationProvider.Patreon, typeof(ISupporterIngestService) },
        { AuthEnums.IntegrationProvider.Shopify, typeof(ISupporterIngestService) },
        { AuthEnums.IntegrationProvider.Treatstream, typeof(ISupporterIngestService) },
        { AuthEnums.IntegrationProvider.AzureTts, typeof(ITtsService) },
        { AuthEnums.IntegrationProvider.ElevenLabs, typeof(ITtsService) },
        { AuthEnums.IntegrationProvider.Marketplace, typeof(IMarketplaceService) },
    };

    /// <summary>
    /// The shared, crypto-shred-ready token vault every provider stores through (identity-auth §3.4). One seam
    /// for all providers — its absence would break token custody for the entire integration surface.
    /// </summary>
    [Fact]
    public void Integration_token_vault_is_registered()
    {
        IServiceProviderIsService inspector =
            _host.Services.GetRequiredService<IServiceProviderIsService>();

        inspector
            .IsService(typeof(IIntegrationTokenVault))
            .Should()
            .BeTrue(
                "every integration provider stores its tokens through the shared IIntegrationTokenVault"
            );
    }

    /// <summary>
    /// The registered OAuth connect descriptors must be real, not stubs: each carries authorize + token
    /// endpoints and at least one scope set (the connect flow is generic over this shape).
    /// </summary>
    [Fact]
    public void Registered_oauth_connect_descriptors_are_wellformed()
    {
        using IServiceScope scope = _host.Services.CreateScope();
        IOAuthProviderRegistry registry =
            scope.ServiceProvider.GetRequiredService<IOAuthProviderRegistry>();

        registry
            .KnownProviders.Should()
            .NotBeEmpty("the OAuth connect registry must expose its providers");

        foreach (string provider in registry.KnownProviders)
        {
            Result<OAuthProviderDescriptor> resolved = registry.Resolve(provider, Guid.Empty);
            resolved
                .IsSuccess.Should()
                .BeTrue($"'{provider}' is a KnownProvider so it must resolve");

            OAuthProviderDescriptor descriptor = resolved.Value;
            descriptor
                .AuthorizeEndpoint.Should()
                .NotBeNullOrWhiteSpace(
                    $"'{provider}' needs an authorize endpoint to start a connect"
                );
            descriptor
                .TokenEndpoint.Should()
                .NotBeNullOrWhiteSpace($"'{provider}' needs a token endpoint to exchange the code");
            descriptor
                .ScopeSets.Should()
                .NotBeEmpty($"'{provider}' must expose at least one requestable scope set");
        }
    }

    /// <summary>
    /// Every OAuth-model integration provider must resolve a descriptor from the unified registry (the spec's
    /// single connect home). RED surfaces a provider whose OAuth connect is NOT descriptor-driven — e.g. Discord,
    /// which today connects via a bespoke <c>DiscordOAuthController</c> + <c>IDiscordOAuthStateService</c> instead
    /// of the unified registry the architecture mandates.
    /// </summary>
    [Theory]
    [MemberData(nameof(OAuthModelProviders))]
    public void Every_oauth_integration_provider_resolves_a_generic_connect_descriptor(
        string provider
    )
    {
        using IServiceScope scope = _host.Services.CreateScope();
        IOAuthProviderRegistry registry =
            scope.ServiceProvider.GetRequiredService<IOAuthProviderRegistry>();

        Result<OAuthProviderDescriptor> resolved = registry.Resolve(provider, Guid.Empty);

        resolved
            .IsSuccess.Should()
            .BeTrue(
                $"'{provider}' connects via OAuth, so it must plug into the unified IOAuthProviderRegistry "
                    + "(integrations-oauth §3.2: one descriptor, no bespoke controller/service). "
                    + $"Known providers: [{string.Join(", ", registry.KnownProviders)}]"
            );
    }

    /// <summary>
    /// Every integration provider needs a feature service that actually uses the connection — a connected
    /// provider with no consuming service is dead weight. RED surfaces a provider in the enum with no wired
    /// feature service.
    /// </summary>
    [Theory]
    [MemberData(nameof(ProviderFeatureServices))]
    public void Every_integration_provider_has_a_registered_feature_service(
        string provider,
        Type featureService
    )
    {
        IServiceProviderIsService inspector =
            _host.Services.GetRequiredService<IServiceProviderIsService>();

        inspector
            .IsService(featureService)
            .Should()
            .BeTrue(
                $"integration provider '{provider}' needs its feature service {featureService.Name} registered "
                    + "so a connected integration is actually usable"
            );
    }
}
