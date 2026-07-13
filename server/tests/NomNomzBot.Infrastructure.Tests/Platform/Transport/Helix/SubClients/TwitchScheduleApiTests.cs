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
/// Behavioural tests for the Schedule sub-client: each method resolves the tenant Guid to the Twitch id,
/// gates mutations on <c>channel:manage:schedule</c>, and builds the exact Helix request (verb / path /
/// auth / query / body). The capturing transport lets us assert the request shape, the short-circuit paths
/// and the mapping of the single nested schedule object — all with no HTTP.
/// </summary>
public class TwitchScheduleApiTests
{
    private static readonly Guid Tenant = Guid.Parse("0195e0d2-2222-7222-8222-000000000002");
    private const string TwitchId = "44322889";

    private static TwitchScheduleApi Build(
        CapturingHelixTransport transport,
        params string[] scopes
    ) =>
        new(
            transport,
            new StubIdentityResolver(Tenant, TwitchId),
            new StubScopeTokenResolver(scopes)
        );

    private static TwitchSchedule Schedule(params TwitchScheduleSegment[] segments) =>
        new(segments, TwitchId, "Name", "login", null);

    private static TwitchScheduleSegment Segment() =>
        new(
            "seg-1",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddHours(2),
            "Coding stream",
            null,
            new TwitchScheduleCategory("509658", "Software & Game Development"),
            true
        );

    [Fact]
    public async Task GetSchedule_ResolvesTenant_BuildsAppTokenRequest_MapsNestedSchedule()
    {
        CapturingHelixTransport transport = new()
        {
            SingleResult = Schedule(Segment()) with
            {
                Vacation = new TwitchScheduleVacation(
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch.AddDays(7)
                ),
            },
        };
        TwitchScheduleApi api = Build(transport);

        Result<TwitchSchedule> result = await api.GetScheduleAsync(
            Tenant,
            new TwitchPageRequest(After: "abc", PageSize: 25)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.BroadcasterId.Should().Be(TwitchId);
        result.Value.Vacation.Should().NotBeNull();
        TwitchScheduleSegment segment = result.Value.Segments.Should().ContainSingle().Subject;
        segment.Title.Should().Be("Coding stream");
        segment.IsRecurring.Should().BeTrue();
        segment.Category!.Name.Should().Be("Software & Game Development");

        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("schedule");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "first" && q.Value == "25");
        transport.LastRequest.Query.Should().Contain(q => q.Key == "after" && q.Value == "abc");
    }

    [Fact]
    public async Task GetSchedule_ClampsPageSizeToTwitchMaxOf25()
    {
        CapturingHelixTransport transport = new() { SingleResult = Schedule(Segment()) };
        TwitchScheduleApi api = Build(transport);

        // The controller default (100) exceeds Twitch's schedule cap of 25 — Twitch rejects that with 400 (the
        // bug that made the page 502). The sub-client must clamp `first` to 25 so the request succeeds.
        Result<TwitchSchedule> result = await api.GetScheduleAsync(
            Tenant,
            new TwitchPageRequest(PageSize: 100)
        );

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Query.Should().Contain(q => q.Key == "first" && q.Value == "25");
    }

    [Fact]
    public async Task GetSchedule_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchScheduleApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver()
        );

        Result<TwitchSchedule> result = await api.GetScheduleAsync(Tenant, new TwitchPageRequest());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetICalendar_ResolvesTenant_BuildsAppTokenRawGet_ReturnsICalTextVerbatim()
    {
        const string ical = """
            BEGIN:VCALENDAR
            PRODID:-//twitch.tv//StreamSchedule//1.0
            VERSION:2.0
            BEGIN:VEVENT
            SUMMARY:TwitchDev Monthly Update // July 1, 2021
            END:VEVENT
            END:VCALENDAR
            """;
        CapturingHelixTransport transport = new() { RawResult = ical };
        TwitchScheduleApi api = Build(transport); // no scope required

        Result<string> result = await api.GetICalendarAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(ical);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Get);
        transport.LastRequest.Path.Should().Be("schedule/icalendar");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.App);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task GetICalendar_UnknownTenant_ReturnsNotFound_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchScheduleApi api = new(
            transport,
            new StubIdentityResolver(Guid.NewGuid(), "other"),
            new StubScopeTokenResolver()
        );

        Result<string> result = await api.GetICalendarAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.NotFound);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateScheduleSettings_MissingScope_ShortCircuits_WithoutCallingTransport()
    {
        CapturingHelixTransport transport = new();
        TwitchScheduleApi api = Build(transport); // no scopes granted

        Result result = await api.UpdateScheduleSettingsAsync(
            Tenant,
            isVacationEnabled: true,
            vacationStartTime: null,
            vacationEndTime: null,
            timezone: null
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateScheduleSettings_WithScope_BuildsUserTokenPatch_WithVacationQuery()
    {
        CapturingHelixTransport transport = new();
        TwitchScheduleApi api = Build(transport, TwitchScopes.ChannelManageSchedule);

        Result result = await api.UpdateScheduleSettingsAsync(
            Tenant,
            isVacationEnabled: true,
            vacationStartTime: new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            vacationEndTime: new DateTimeOffset(2026, 7, 8, 9, 0, 0, TimeSpan.Zero),
            timezone: "America/New_York"
        );

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        transport.LastRequest.Path.Should().Be("schedule/settings");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "is_vacation_enabled" && q.Value == "true");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "vacation_start_time" && q.Value == "2026-07-01T09:00:00Z");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "vacation_end_time" && q.Value == "2026-07-08T09:00:00Z");
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "timezone" && q.Value == "America/New_York");
    }

    [Fact]
    public async Task UpdateScheduleSettings_OmitsUnsetOptionalQueryParams()
    {
        CapturingHelixTransport transport = new();
        TwitchScheduleApi api = Build(transport, TwitchScopes.ChannelManageSchedule);

        Result result = await api.UpdateScheduleSettingsAsync(
            Tenant,
            isVacationEnabled: false,
            vacationStartTime: null,
            vacationEndTime: null,
            timezone: null
        );

        result.IsSuccess.Should().BeTrue();
        transport
            .LastRequest!.Query.Should()
            .Contain(q => q.Key == "is_vacation_enabled" && q.Value == "false");
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "vacation_start_time");
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "vacation_end_time");
        transport.LastRequest.Query.Should().NotContain(q => q.Key == "timezone");
    }

    [Fact]
    public async Task CreateSegment_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchScheduleApi api = Build(transport);

        Result<TwitchSchedule> result = await api.CreateSegmentAsync(
            Tenant,
            new CreateScheduleSegmentRequest(DateTimeOffset.UnixEpoch, "America/New_York", "240")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CreateSegment_WithScope_BuildsUserTokenPost_WithBody_MapsSchedule()
    {
        CapturingHelixTransport transport = new() { SingleResult = Schedule(Segment()) };
        TwitchScheduleApi api = Build(transport, TwitchScopes.ChannelManageSchedule);
        CreateScheduleSegmentRequest request = new(
            DateTimeOffset.UnixEpoch,
            "America/New_York",
            "240",
            IsRecurring: true,
            CategoryId: "509658",
            Title: "Coding stream"
        );

        Result<TwitchSchedule> result = await api.CreateSegmentAsync(Tenant, request);

        result.IsSuccess.Should().BeTrue();
        result.Value.Segments.Should().ContainSingle().Which.Title.Should().Be("Coding stream");
        transport.LastRequest!.Method.Should().Be(HttpMethod.Post);
        transport.LastRequest.Path.Should().Be("schedule/segment");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport.LastRequest.Body.Should().BeSameAs(request);
        transport
            .LastRequest.Query.Should()
            .ContainSingle(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
    }

    [Fact]
    public async Task UpdateSegment_WithScope_BuildsUserTokenPatch_WithIdQuery_AndBody()
    {
        CapturingHelixTransport transport = new() { SingleResult = Schedule(Segment()) };
        TwitchScheduleApi api = Build(transport, TwitchScopes.ChannelManageSchedule);
        UpdateScheduleSegmentRequest request = new(Title: "Renamed", IsCanceled: true);

        Result<TwitchSchedule> result = await api.UpdateSegmentAsync(Tenant, "seg-1", request);

        result.IsSuccess.Should().BeTrue();
        result.Value.BroadcasterId.Should().Be(TwitchId);
        transport.LastRequest!.Method.Should().Be(HttpMethod.Patch);
        transport.LastRequest.Path.Should().Be("schedule/segment");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Body.Should().BeSameAs(request);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "id" && q.Value == "seg-1");
    }

    [Fact]
    public async Task UpdateSegment_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchScheduleApi api = Build(transport);

        Result<TwitchSchedule> result = await api.UpdateSegmentAsync(
            Tenant,
            "seg-1",
            new UpdateScheduleSegmentRequest(Title: "Renamed")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task DeleteSegment_MissingScope_ShortCircuits()
    {
        CapturingHelixTransport transport = new();
        TwitchScheduleApi api = Build(transport);

        Result result = await api.DeleteSegmentAsync(Tenant, "seg-1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
        transport.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task DeleteSegment_WithScope_BuildsUserTokenDelete_WithIdQuery()
    {
        CapturingHelixTransport transport = new();
        TwitchScheduleApi api = Build(transport, TwitchScopes.ChannelManageSchedule);

        Result result = await api.DeleteSegmentAsync(Tenant, "seg-1");

        result.IsSuccess.Should().BeTrue();
        transport.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        transport.LastRequest.Path.Should().Be("schedule/segment");
        transport.LastRequest.Auth.Should().Be(TwitchHelixAuth.User);
        transport.LastRequest.Priority.Should().Be(TwitchCallPriority.UserInteractive);
        transport
            .LastRequest.Query.Should()
            .Contain(q => q.Key == "broadcaster_id" && q.Value == TwitchId);
        transport.LastRequest.Query.Should().Contain(q => q.Key == "id" && q.Value == "seg-1");
    }
}
