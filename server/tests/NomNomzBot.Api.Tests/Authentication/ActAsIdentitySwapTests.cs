// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Identifiers;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Infrastructure.Platform.Auth;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Authentication;

/// <summary>
/// The owner's rule for act-as: "I need to be 100% the user I am impersonating … the only trace of the admin's
/// account is the exit impersonation button." Proven on the real request path: an admin (Stoney_Eagle, owner of
/// his own channel, platform admin) acts as anda_six (owner of another channel, moderator of a third, no admin).
/// Every read the dashboard makes on boot and navigation answers the act-as token EXACTLY as it answers
/// anda_six's own login; a refresh keeps anda_six; Exit (and a logout pressed while acting) ends the support
/// session, kills the act-as token and gives the admin back — never touching anda_six's own sessions.
/// </summary>
public sealed class ActAsIdentitySwapTests : IAsyncLifetime
{
    private static readonly Guid Admin = Guid.Parse("0192b000-0000-7000-8000-000000000a01");
    private static readonly Guid Target = Guid.Parse("0192b000-0000-7000-8000-000000000b01");
    private static readonly Guid ThirdOwner = Guid.Parse("0192b000-0000-7000-8000-000000000c01");
    private static readonly Guid AdminChannel = Guid.Parse("0192b000-0000-7000-8000-000000000a02");
    private static readonly Guid TargetChannel = Guid.Parse("0192b000-0000-7000-8000-000000000b02");
    private static readonly Guid ModeratedChannel = Guid.Parse(
        "0192b000-0000-7000-8000-000000000c02"
    );
    private static readonly Guid SupportGrant = Guid.Parse("0192b000-0000-7000-8000-00000000d001");

    // A channel anda_six moderates on Twitch only: no ChannelModerators row, no membership row. Her own
    // channel list grants the Moderator row; act-as must answer the same without writing it.
    private static readonly Guid FourthOwner = Guid.Parse("0192b000-0000-7000-8000-000000000e01");
    private static readonly Guid TwitchOnlyChannel = Guid.Parse(
        "0192b000-0000-7000-8000-000000000e02"
    );

    private ActAsTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await ActAsTestHost.StartAsync(Admin);
        await _host.SeedAsync(SeedAsync);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Every_boot_read_under_act_as_equals_the_impersonated_users_own_login()
    {
        string targetOwn = (await _host.LoginAsync(Target, TargetChannel)).AccessToken;
        string adminOwn = (await _host.LoginAsync(Admin, AdminChannel)).AccessToken;
        string actAs = (await StartActAsAsync(adminOwn)).AccessToken;

        // Each boot read, what anda_six herself gets, and what the admin himself gets — the two differ on every
        // row, so matching anda_six's answer is only possible by BEING her, not by a shared default.
        (string Path, HttpStatusCode AsTarget, HttpStatusCode AsAdmin)[] bootReads =
        [
            ("/api/v1/auth/me", HttpStatusCode.OK, HttpStatusCode.OK),
            ("/api/v1/channels?page=1&pageSize=100", HttpStatusCode.OK, HttpStatusCode.OK),
            (
                $"/api/v1/channels/{TargetChannel}/roles/effective/me",
                HttpStatusCode.OK,
                HttpStatusCode.OK
            ),
            (
                $"/api/v1/channels/{ModeratedChannel}/roles/effective/me",
                HttpStatusCode.OK,
                HttpStatusCode.OK
            ),
            (
                $"/api/v1/channels/{AdminChannel}/roles/effective/me",
                HttpStatusCode.OK,
                HttpStatusCode.OK
            ),
            (
                $"/api/v1/channels/{TargetChannel}/notifications/action-required",
                HttpStatusCode.OK,
                HttpStatusCode.Forbidden
            ),
            (
                $"/api/v1/channels/{AdminChannel}/notifications/action-required",
                HttpStatusCode.Forbidden,
                HttpStatusCode.OK
            ),
            ("/api/v1/admin/tenants?page=1&take=25", HttpStatusCode.Forbidden, HttpStatusCode.OK),
        ];
        foreach (
            (string path, HttpStatusCode targetStatus, HttpStatusCode adminStatus) in bootReads
        )
        {
            (HttpStatusCode status, string body) asTarget = await ReadAsync(path, targetOwn);
            (HttpStatusCode status, string body) asAdmin = await ReadAsync(path, adminOwn);
            (HttpStatusCode status, string body) actingAs = await ReadAsync(path, actAs);

            asTarget.status.Should().Be(targetStatus, path);
            asAdmin.status.Should().Be(adminStatus, path);
            asAdmin.Should().NotBe(asTarget, $"{path} must tell the two users apart");
            actingAs
                .Should()
                .Be(asTarget, $"act-as must answer {path} exactly as the user's own login");
        }

        JsonNode? me = JsonNode.Parse((await ReadAsync("/api/v1/auth/me", actAs)).body)!["data"];
        me!["username"]!.GetValue<string>().Should().Be("anda_six");
        me["isAdmin"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task A_channel_she_moderates_only_on_Twitch_answers_as_her_own_login_without_a_write()
    {
        _host
            .Moderators.GetModeratedChannelsAsync(
                TargetChannel,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchModeratedChannel>(
                        [new(TwitchId(TwitchOnlyChannel), "fourth_owner", "fourth_owner")],
                        null,
                        1
                    )
                )
            );
        const string channels = "/api/v1/channels?page=1&pageSize=100";
        string effective = $"/api/v1/channels/{TwitchOnlyChannel}/roles/effective/me";
        string adminOwn = (await _host.LoginAsync(Admin, AdminChannel)).AccessToken;
        string actAs = (await StartActAsAsync(adminOwn)).AccessToken;

        (HttpStatusCode status, string body) actingList = await ReadAsync(channels, actAs);
        (HttpStatusCode status, string body) actingRole = await ReadAsync(effective, actAs);

        (await HasMembershipAsync(Target, TwitchOnlyChannel))
            .Should()
            .BeFalse("an act-as session writes no role row on the user's behalf");

        string targetOwn = (await _host.LoginAsync(Target, TargetChannel)).AccessToken;
        (HttpStatusCode status, string body) ownList = await ReadAsync(channels, targetOwn);
        (HttpStatusCode status, string body) ownRole = await ReadAsync(effective, targetOwn);

        (await HasMembershipAsync(Target, TwitchOnlyChannel))
            .Should()
            .BeTrue("her own login grants the Moderator row, so the comparison below is real");
        actingList.Should().Be(ownList, "act-as lists her channels exactly as her own login");
        actingRole.Should().Be(ownRole, "act-as resolves her role exactly as her own login");
        JsonNode.Parse(actingRole.body)!["data"]!["managementRole"]!
            .GetValue<string>()
            .Should()
            .Be("Moderator");
    }

    [Fact]
    public async Task The_dashboard_hub_joins_as_the_impersonated_user()
    {
        string targetOwn = (await _host.LoginAsync(Target, TargetChannel)).AccessToken;
        string adminOwn = (await _host.LoginAsync(Admin, AdminChannel)).AccessToken;
        string actAs = (await StartActAsAsync(adminOwn)).AccessToken;

        object[] join = [TargetChannel.ToString(), new[] { "chat", "activity" }];
        JsonNode? asTarget = await DashboardHubProbe.InvokeAsync(
            _host.Server,
            targetOwn,
            "JoinChannelClasses",
            join
        );
        JsonNode? actingAs = await DashboardHubProbe.InvokeAsync(
            _host.Server,
            actAs,
            "JoinChannelClasses",
            join
        );
        JsonNode? asAdmin = await DashboardHubProbe.InvokeAsync(
            _host.Server,
            adminOwn,
            "JoinChannelClasses",
            join
        );

        JsonNode.DeepEquals(actingAs, asTarget).Should().BeTrue();
        actingAs!["grantedClasses"]!.AsArray().Should().HaveCount(2, "anda_six owns this channel");
        asAdmin!["grantedClasses"]!
            .AsArray()
            .Should()
            .BeEmpty("the admin himself holds no role there");
    }

    [Fact]
    public async Task The_act_as_token_carries_the_operator_by_id_only()
    {
        string adminOwn = (await _host.LoginAsync(Admin, AdminChannel)).AccessToken;
        string actAs = (await StartActAsAsync(adminOwn)).AccessToken;

        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(actAs);
        jwt.Claims.Should()
            .ContainSingle(c => c.Type == JwtTokenService.ActorClaim)
            .Which.Value.Should()
            .Be(Admin.ToString());
        jwt.Claims.Should()
            .NotContain(c => c.Value.Contains("Stoney", StringComparison.OrdinalIgnoreCase));
        jwt.Claims.Select(c => c.Type).Should().NotContain("act_name");
    }

    [Fact]
    public async Task A_refresh_while_acting_stays_the_impersonated_user_and_leaves_the_admins_session_alone()
    {
        SessionTokensDto admin = await _host.LoginAsync(Admin, AdminChannel);
        HttpResponseMessage started = await StartActAsResponseAsync(admin.AccessToken);
        string actAsCookie = CookieValue(started, "nnz_act_as")!;
        SetCookieHeader(started, "nnz_act_as")
            .Should()
            .Contain("httponly")
            .And.Contain("samesite=strict");

        HttpResponseMessage refreshed = await PostAsync(
            "/api/v1/auth/refresh",
            bearer: null,
            cookies: $"nnz_refresh_token={admin.RawRefreshToken}; nnz_act_as={actAsCookie}"
        );

        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode data = JsonNode.Parse(await refreshed.Content.ReadAsStringAsync())!["data"]!;
        data["user"]!["username"]!.GetValue<string>().Should().Be("anda_six");
        Decode(data["impersonation"]!["sessionId"]!).Should().Be(SupportGrant);
        data["refreshToken"]
            .Should()
            .BeNull("the admin's own refresh token is never handed out while acting");
        CookieValue(refreshed, "nnz_refresh_token")
            .Should()
            .BeNull("the admin's refresh cookie is not rotated");
        string rotated = CookieValue(refreshed, "nnz_act_as")!;
        rotated.Should().Be(data["accessToken"]!.GetValue<string>());

        (await ReadAsync("/api/v1/auth/me", rotated))
            .body.Should()
            .Contain("\"username\":\"anda_six\"");
        (await _host.IsSessionLiveAsync(admin.RawRefreshToken)).Should().BeTrue();
    }

    [Fact]
    public async Task Exit_ends_the_support_session_kills_the_act_as_token_and_hands_back_the_admin()
    {
        SessionTokensDto admin = await _host.LoginAsync(Admin, AdminChannel);
        HttpResponseMessage started = await StartActAsResponseAsync(admin.AccessToken);
        string actAs = CookieValue(started, "nnz_act_as")!;

        HttpResponseMessage exited = await PostAsync(
            "/api/v1/auth/impersonation/exit",
            bearer: actAs,
            cookies: $"nnz_refresh_token={admin.RawRefreshToken}; nnz_act_as={actAs}"
        );

        exited.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode data = JsonNode.Parse(await exited.Content.ReadAsStringAsync())!["data"]!;
        data["impersonation"].Should().BeNull();
        string adminAgain = data["accessToken"]!.GetValue<string>();
        (await ReadAsync("/api/v1/auth/me", adminAgain))
            .body.Should()
            .Contain("\"username\":\"Stoney_Eagle\"");
        SetCookieHeader(exited, "nnz_act_as").Should().Contain("expires=thu, 01 jan 1970");
        string newAdminRefresh = CookieValue(exited, "nnz_refresh_token")!;

        (await ReadAsync("/api/v1/auth/me", actAs)).status.Should().Be(HttpStatusCode.Unauthorized);
        (await DashboardHubProbe.NegotiateAsync(_host.Server, actAs))
            .Should()
            .Be(HttpStatusCode.Unauthorized, "the hub refuses the dead act-as token too");
        (await _host.ReadAsync(db => db.IamRoleAssignments.SingleAsync(a => a.Id == SupportGrant)))
            .RevokedAt.Should()
            .NotBeNull();
        await _host
            .EventBus.Received(1)
            .PublishAsync(
                Arg.Is<ImpersonationEndedEvent>(e =>
                    e.AccessGrantId == SupportGrant
                    && e.OperatorPrincipalId == ActAsTestHost.AdminPrincipalId
                ),
                Arg.Any<CancellationToken>()
            );

        // A reload afterwards, with the stale act-as cookie still in the jar, comes back as the admin.
        HttpResponseMessage reload = await PostAsync(
            "/api/v1/auth/refresh",
            bearer: null,
            cookies: $"nnz_refresh_token={newAdminRefresh}; nnz_act_as={actAs}"
        );
        JsonNode reloaded = JsonNode.Parse(await reload.Content.ReadAsStringAsync())!["data"]!;
        reloaded["user"]!["username"]!.GetValue<string>().Should().Be("Stoney_Eagle");
        reloaded["impersonation"].Should().BeNull();
        SetCookieHeader(reload, "nnz_act_as").Should().Contain("expires=thu, 01 jan 1970");
    }

    [Fact]
    public async Task Ending_the_support_session_any_other_way_ends_act_as_too()
    {
        SessionTokensDto admin = await _host.LoginAsync(Admin, AdminChannel);
        string actAs = CookieValue(await StartActAsResponseAsync(admin.AccessToken), "nnz_act_as")!;

        // The admin closes the support session from his own admin view — not through Exit, so no `sid` is revoked.
        using HttpRequestMessage end = new(
            HttpMethod.Delete,
            $"/api/v1/admin/access/{SupportGrant}"
        );
        end.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        (await _host.Client.SendAsync(end)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReadAsync("/api/v1/auth/me", actAs)).status.Should().Be(HttpStatusCode.Unauthorized);
        HttpResponseMessage reload = await PostAsync(
            "/api/v1/auth/refresh",
            bearer: null,
            cookies: $"nnz_refresh_token={admin.RawRefreshToken}; nnz_act_as={actAs}"
        );
        JsonNode reloaded = JsonNode.Parse(await reload.Content.ReadAsStringAsync())!["data"]!;
        reloaded["user"]!["username"]!.GetValue<string>().Should().Be("Stoney_Eagle");
        reloaded["impersonation"].Should().BeNull();
    }

    [Theory]
    [InlineData("/api/v1/auth/logout")]
    [InlineData("/api/v1/auth/logout/all")]
    public async Task A_logout_while_acting_logs_the_admin_out_and_never_the_impersonated_user(
        string logout
    )
    {
        SessionTokensDto target = await _host.LoginAsync(Target, TargetChannel);
        SessionTokensDto admin = await _host.LoginAsync(Admin, AdminChannel);
        string actAs = CookieValue(await StartActAsResponseAsync(admin.AccessToken), "nnz_act_as")!;

        HttpResponseMessage response = await PostAsync(
            logout,
            bearer: actAs,
            cookies: $"nnz_refresh_token={admin.RawRefreshToken}; nnz_act_as={actAs}"
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.IsSessionLiveAsync(admin.RawRefreshToken)).Should().BeFalse();
        (await _host.IsSessionLiveAsync(target.RawRefreshToken))
            .Should()
            .BeTrue("anda_six's own login is not the admin's to end");
        (await _host.ReadAsync(db => db.IamRoleAssignments.SingleAsync(a => a.Id == SupportGrant)))
            .RevokedAt.Should()
            .NotBeNull();
        (await ReadAsync("/api/v1/auth/me", actAs)).status.Should().Be(HttpStatusCode.Unauthorized);
        SetCookieHeader(response, "nnz_act_as").Should().Contain("expires=thu, 01 jan 1970");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task<ImpersonationTokenDto> StartActAsAsync(string adminToken)
    {
        HttpResponseMessage response = await StartActAsResponseAsync(adminToken);
        JsonNode data = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["data"]!;
        return new(
            data["accessToken"]!.GetValue<string>(),
            data["expiresAt"]!.GetValue<DateTime>(),
            Decode(data["sessionId"]!),
            null!
        );
    }

    private async Task<HttpResponseMessage> StartActAsResponseAsync(string adminToken)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            $"/api/v1/admin/users/{Target}/impersonate"
        );
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        request.Content = JsonContent.Create(
            new ImpersonateUserRequest(SupportGrant, "reproducing anda_six's report")
        );
        HttpResponseMessage response = await _host.Client.SendAsync(request);
        response
            .StatusCode.Should()
            .Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return response;
    }

    private async Task<(HttpStatusCode status, string body)> ReadAsync(string path, string token)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpResponseMessage response = await _host.Client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<HttpResponseMessage> PostAsync(string path, string? bearer, string cookies)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, path);
        if (bearer is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.Add("Cookie", cookies);
        request.Headers.Add("Origin", "http://localhost");
        request.Content = JsonContent.Create(new { });
        return await _host.Client.SendAsync(request);
    }

    private static string? SetCookieHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values)
            ? values
                .FirstOrDefault(v => v.StartsWith(name + "=", StringComparison.Ordinal))
                ?.ToLowerInvariant()
            : null;

    private static string? CookieValue(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values))
            return null;
        string? header = values.FirstOrDefault(v =>
            v.StartsWith(name + "=", StringComparison.Ordinal)
        );
        string? value = header?[(name.Length + 1)..].Split(';')[0];
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private Task<bool> HasMembershipAsync(Guid userId, Guid channelId) =>
        _host.ReadAsync(db =>
            db.ChannelMemberships.IgnoreQueryFilters()
                .AnyAsync(m => m.UserId == userId && m.BroadcasterId == channelId)
        );

    private static string TwitchId(Guid channelId) => channelId.ToString("N")[^12..];

    private static Guid Decode(JsonNode id) =>
        GuidUlidCodec.TryDecode(id.GetValue<string>(), out Guid guid)
            ? guid
            : throw new FormatException(id.GetValue<string>());

    private static async Task SeedAsync(Infrastructure.Platform.Persistence.AppDbContext db)
    {
        DateTime now = DateTime.UtcNow;
        db.Users.AddRange(
            User(Admin, "Stoney_Eagle", isAdmin: true),
            User(Target, "anda_six", isAdmin: false),
            User(ThirdOwner, "third_owner", isAdmin: false),
            User(FourthOwner, "fourth_owner", isAdmin: false)
        );
        db.Channels.AddRange(
            Channel(AdminChannel, Admin, "stoney_eagle"),
            Channel(TargetChannel, Target, "anda_six"),
            Channel(ModeratedChannel, ThirdOwner, "third_owner"),
            Channel(TwitchOnlyChannel, FourthOwner, "fourth_owner")
        );
        db.ChannelModerators.Add(
            new()
            {
                ChannelId = ModeratedChannel,
                UserId = Target,
                GrantedAt = now,
            }
        );
        db.ChannelMemberships.Add(
            new()
            {
                BroadcasterId = ModeratedChannel,
                UserId = Target,
                ManagementRole = ManagementRole.Moderator,
                LevelValue = 10,
                Source = MembershipSource.TwitchBadge,
                GrantedAt = now,
            }
        );
        foreach (string key in new[] { "dashboard:read", "chat:read" })
            db.ActionDefinitions.Add(
                new()
                {
                    ActionKey = key,
                    Plane = AuthPlane.Management,
                    DefaultLevel = 10,
                    FloorLevel = 10,
                    FloorTier = DangerTier.Low,
                }
            );

        IamRole support = new() { Name = "platform-support", IsSystem = true };
        db.IamRoles.Add(support);
        db.IamPrincipals.Add(
            new()
            {
                Id = ActAsTestHost.AdminPrincipalId,
                PrincipalType = IamPrincipalType.Employee,
                UserId = Admin,
                Name = "Stoney_Eagle",
                IsActive = true,
            }
        );
        db.IamRoleAssignments.Add(
            new()
            {
                Id = SupportGrant,
                PrincipalId = ActAsTestHost.AdminPrincipalId,
                RoleId = support.Id,
                ScopeChannelId = TargetChannel,
                ExpiresAt = now.AddHours(4),
                Reason = "anda_six support ticket",
            }
        );
        await Task.CompletedTask;
    }

    private static User User(Guid id, string username, bool isAdmin) =>
        new()
        {
            Id = id,
            TwitchUserId = id.ToString("N")[^10..],
            Username = username,
            UsernameNormalized = username.ToLowerInvariant(),
            DisplayName = username,
            Color = isAdmin ? "#9146FF" : "#1E90FF",
            IsPlatformPrincipal = isAdmin,
        };

    private static Channel Channel(Guid id, Guid owner, string name) =>
        new()
        {
            Id = id,
            OwnerUserId = owner,
            Name = name,
            NameNormalized = name,
            TwitchChannelId = TwitchId(id),
            ExternalChannelId = id.ToString("N")[^12..],
            IsOnboarded = true,
        };
}
