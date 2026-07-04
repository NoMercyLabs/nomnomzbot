// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Transport.Helix.SubClients;

/// <summary>
/// The Helix "Schedule" sub-client (twitch-helix.md §3.2). Pure Helix I/O: it resolves the tenant
/// <see cref="Guid"/> to a Twitch channel id, pre-checks the required scope, builds a
/// <see cref="TwitchHelixRequest"/>, and maps the response through <see cref="ITwitchHelixTransport"/>.
/// It deliberately holds no database or event-bus dependency — mirroring Twitch state into local tables
/// and raising domain events is a separate responsibility owned by the consuming services, which keeps
/// every sub-client thin, uniform, and testable purely at the HTTP seam.
/// </summary>
public sealed class TwitchScheduleApi(
    ITwitchHelixTransport transport,
    ITwitchIdentityResolver identity,
    ITwitchTokenResolver tokens
) : ITwitchScheduleApi
{
    public async Task<Result<TwitchSchedule>> GetScheduleAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    )
    {
        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchSchedule>(default!);

        List<KeyValuePair<string, string>> query =
        [
            new("broadcaster_id", channel.Value),
            new("first", page.PageSize.ToString()),
        ];
        if (page.After is not null)
            query.Add(new("after", page.After));

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "schedule",
            TwitchHelixAuth.App,
            broadcasterId,
            Query: query
        );

        return await transport.GetSingleAsync<TwitchSchedule>(request, ct);
    }

    public async Task<Result<string>> GetICalendarAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        TwitchHelixRequest request = new(
            HttpMethod.Get,
            "schedule/icalendar",
            TwitchHelixAuth.App,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value)]
        );

        return await transport.GetRawAsync(request, ct);
    }

    public async Task<Result> UpdateScheduleSettingsAsync(
        Guid broadcasterId,
        bool? isVacationEnabled,
        DateTimeOffset? vacationStartTime,
        DateTimeOffset? vacationEndTime,
        string? timezone,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageSchedule,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        List<KeyValuePair<string, string>> query = [new("broadcaster_id", channel.Value)];
        if (isVacationEnabled is { } enabled)
            query.Add(new("is_vacation_enabled", enabled ? "true" : "false"));
        if (vacationStartTime is { } start)
            query.Add(new("vacation_start_time", FormatTimestamp(start)));
        if (vacationEndTime is { } end)
            query.Add(new("vacation_end_time", FormatTimestamp(end)));
        if (timezone is not null)
            query.Add(new("timezone", timezone));

        TwitchHelixRequest request = new(
            HttpMethod.Patch,
            "schedule/settings",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: query,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    public async Task<Result<TwitchSchedule>> CreateSegmentAsync(
        Guid broadcasterId,
        CreateScheduleSegmentRequest request,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageSchedule,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchSchedule>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchSchedule>(default!);

        TwitchHelixRequest helixRequest = new(
            HttpMethod.Post,
            "schedule/segment",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value)],
            Body: request,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchSchedule>(helixRequest, ct);
    }

    public async Task<Result<TwitchSchedule>> UpdateSegmentAsync(
        Guid broadcasterId,
        string segmentId,
        UpdateScheduleSegmentRequest request,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageSchedule,
            ct
        );
        if (scope.IsFailure)
            return scope.WithValue<TwitchSchedule>(default!);

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel.WithValue<TwitchSchedule>(default!);

        TwitchHelixRequest helixRequest = new(
            HttpMethod.Patch,
            "schedule/segment",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("id", segmentId)],
            Body: request,
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendWithResultAsync<TwitchSchedule>(helixRequest, ct);
    }

    public async Task<Result> DeleteSegmentAsync(
        Guid broadcasterId,
        string segmentId,
        CancellationToken ct = default
    )
    {
        Result scope = await RequireScopeAsync(
            broadcasterId,
            TwitchScopes.ChannelManageSchedule,
            ct
        );
        if (scope.IsFailure)
            return scope;

        Result<string> channel = await ResolveAsync(broadcasterId, ct);
        if (channel.IsFailure)
            return channel;

        TwitchHelixRequest request = new(
            HttpMethod.Delete,
            "schedule/segment",
            TwitchHelixAuth.User,
            broadcasterId,
            Query: [new("broadcaster_id", channel.Value), new("id", segmentId)],
            Priority: TwitchCallPriority.UserInteractive
        );

        return await transport.SendAsync(request, ct);
    }

    /// <summary>Formats a timestamp as the RFC3339 UTC string Twitch expects in schedule query params.</summary>
    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>Resolves the tenant Guid to its Twitch channel id, or <c>not_found</c> when unknown locally.</summary>
    private async Task<Result<string>> ResolveAsync(Guid broadcasterId, CancellationToken ct)
    {
        string? channelId = await identity.GetTwitchChannelIdAsync(broadcasterId, ct);
        return channelId is null
            ? Result.Failure<string>("Channel is not known locally.", TwitchErrorCodes.NotFound)
            : Result.Success(channelId);
    }

    /// <summary>Pre-checks a required user-token scope, short-circuiting with <c>missing_scope</c> when absent.</summary>
    private async Task<Result> RequireScopeAsync(
        Guid broadcasterId,
        string scope,
        CancellationToken ct
    )
    {
        bool granted = await tokens.HasScopeAsync(broadcasterId, scope, ct);
        return granted
            ? Result.Success()
            : Result.Failure($"Missing required scope '{scope}'.", TwitchErrorCodes.MissingScope);
    }
}
