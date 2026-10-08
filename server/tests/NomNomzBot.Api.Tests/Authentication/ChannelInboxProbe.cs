// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;

namespace NomNomzBot.Api.Tests.Authentication;

/// <summary>
/// An inbox source whose one item names the channel it was asked about, so an inbox response shows exactly
/// which channel's inbox the caller reached.
/// </summary>
internal sealed class ChannelInboxProbe : IActionRequiredSource
{
    private static readonly DateTime DetectedAt = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    public string SourceKey => "probe";

    public IReadOnlyCollection<string> KeyPrefixes => ["probe:"];

    public IReadOnlyCollection<string> InvalidatingEventTypes => [];

    public Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            Result.Success<List<ActionRequiredItemDto>>([
                new(
                    $"probe:{channelId}",
                    "probe",
                    "info",
                    "probe_title",
                    "probe_message",
                    new() { ["channel"] = channelId.ToString() },
                    DetectedAt,
                    "dashboard",
                    null,
                    null,
                    1,
                    []
                ),
            ])
        );

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));
}
