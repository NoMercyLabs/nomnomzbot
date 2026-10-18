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
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Integrations.Services;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Integrations;

/// <summary>
/// Proves the credential state tells the dashboard whether a shared (platform) Spotify app exists, so the panel
/// only offers "use the shared app" when there is one. Spotify never falls back to a platform app
/// (<c>ChannelCredentialsResolver</c> carve-out), so the flag stays false even when platform credentials exist.
/// </summary>
public sealed class ChannelSpotifyCredentialsServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000a1");

    private static ChannelSpotifyCredentialsService Build(bool platformAppConfigured)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(db, out _);
        return new(db, protector, new FakePlatformCredentials(platformAppConfigured));
    }

    [Fact]
    public async Task Get_reports_no_shared_app_when_the_platform_has_no_spotify_app()
    {
        ChannelSpotifyCredentialsService service = Build(platformAppConfigured: false);

        Result<ChannelSpotifyCredentialsDto> result = await service.GetAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        result.Value!.HasSharedApp.Should().BeFalse();
    }

    [Fact]
    public async Task Get_reports_no_shared_app_even_when_platform_credentials_exist_because_spotify_never_falls_back()
    {
        ChannelSpotifyCredentialsService service = Build(platformAppConfigured: true);

        Result<ChannelSpotifyCredentialsDto> result = await service.GetAsync(Channel);

        result.Value!.HasSharedApp.Should().BeFalse();
    }

    [Fact]
    public async Task Set_and_clear_keep_the_flag_in_the_returned_state()
    {
        ChannelSpotifyCredentialsService service = Build(platformAppConfigured: false);

        Result<ChannelSpotifyCredentialsDto> saved = await service.SetAsync(
            Channel,
            new("my-id", "my-secret")
        );
        Result<ChannelSpotifyCredentialsDto> cleared = await service.ClearAsync(Channel);

        saved.Value!.Should().Be(new ChannelSpotifyCredentialsDto("my-id", true, false));
        cleared.Value!.Should().Be(new ChannelSpotifyCredentialsDto(null, false, false));
    }

    private sealed class FakePlatformCredentials(bool configured) : ISystemCredentialsProvider
    {
        public Task<SystemAppCredentials?> GetAsync(
            string provider,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<SystemAppCredentials?>(configured ? new("id", "secret") : null);

        public Task<string?> GetClientIdAsync(
            string provider,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<string?>(configured ? "id" : null);

        public Task<string?> GetValueAsync(
            string provider,
            string field,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<string?>(null);

        public Task<bool> IsAppDecisionRecordedAsync(
            string provider,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(true);
    }
}
