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
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using FluentAssertions;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Api.Tests.Authentication;

/// <summary>
/// The owner's rule for "View as": the previewed role is a full identity swap. With the X-View-As header the
/// platform admin is not an admin, owns no channel, and the admin plane refuses; without it everything answers
/// as the admin himself.
/// </summary>
public sealed class ViewAsIdentitySwapTests : IAsyncLifetime
{
    private static readonly Guid Admin = Guid.Parse("0192b000-0000-7000-8000-000000000a01");
    private static readonly Guid AdminChannel = Guid.Parse("0192b000-0000-7000-8000-000000000a02");

    private ActAsTestHost _host = null!;
    private string _token = null!;

    public async Task InitializeAsync()
    {
        _host = await ActAsTestHost.StartAsync(Admin);
        await _host.SeedAsync(SeedAsync);
        _token = (await _host.LoginAsync(Admin, AdminChannel)).AccessToken;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Theory]
    [InlineData("viewer")]
    [InlineData("moderator")]
    public async Task The_current_user_is_not_an_admin_while_previewing(string role)
    {
        (HttpStatusCode own, string ownBody) = await ReadAsync("/api/v1/auth/me", null);
        (HttpStatusCode previewed, string previewBody) = await ReadAsync("/api/v1/auth/me", role);

        own.Should().Be(HttpStatusCode.OK);
        JsonNode.Parse(ownBody)!["data"]!["isAdmin"]!.GetValue<bool>().Should().BeTrue();
        previewed.Should().Be(HttpStatusCode.OK);
        JsonNode data = JsonNode.Parse(previewBody)!["data"]!;
        data["isAdmin"]!.GetValue<bool>().Should().BeFalse();
        data["username"]!.GetValue<string>().Should().Be("Stoney_Eagle");
    }

    [Theory]
    [InlineData("viewer", "viewer")]
    [InlineData("moderator", "moderator")]
    public async Task The_channel_list_shows_no_broadcaster_role_while_previewing(
        string preview,
        string expectedRole
    )
    {
        (_, string ownBody) = await ReadAsync("/api/v1/channels?page=1&pageSize=100", null);
        (_, string previewBody) = await ReadAsync("/api/v1/channels?page=1&pageSize=100", preview);

        RolesOf(ownBody).Should().Equal("broadcaster");
        RolesOf(previewBody).Should().Equal(expectedRole);
    }

    [Theory]
    [InlineData("viewer")]
    [InlineData("moderator")]
    public async Task The_admin_plane_refuses_while_previewing(string role)
    {
        const string path = "/api/v1/admin/tenants?page=1&take=25";

        (await ReadAsync(path, null)).Item1.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(path, role)).Item1.Should().Be(HttpStatusCode.Forbidden);
    }

    private static string[] RolesOf(string body) =>
        [.. JsonNode.Parse(body)!["data"]!.AsArray().Select(c => c!["role"]!.GetValue<string>())];

    private async Task<(HttpStatusCode, string)> ReadAsync(string path, string? viewAs)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (viewAs is not null)
            request.Headers.Add("X-View-As", viewAs);
        HttpResponseMessage response = await _host.Client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static Task SeedAsync(AppDbContext db)
    {
        db.Users.Add(
            new()
            {
                Id = Admin,
                TwitchUserId = Admin.ToString("N")[^10..],
                Username = "Stoney_Eagle",
                UsernameNormalized = "stoney_eagle",
                DisplayName = "Stoney_Eagle",
                IsPlatformPrincipal = true,
            }
        );
        db.Channels.Add(
            new()
            {
                Id = AdminChannel,
                OwnerUserId = Admin,
                Name = "stoney_eagle",
                NameNormalized = "stoney_eagle",
                TwitchChannelId = AdminChannel.ToString("N")[^12..],
                ExternalChannelId = AdminChannel.ToString("N")[^12..],
                IsOnboarded = true,
            }
        );
        return Task.CompletedTask;
    }
}
