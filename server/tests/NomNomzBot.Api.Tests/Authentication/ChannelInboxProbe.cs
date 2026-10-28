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
