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

using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// Manages per-channel event response configurations.
/// </summary>
public interface IEventResponseService
{
    Task<Result<PagedList<EventResponseListItem>>> ListAsync(
        string broadcasterId,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    );
    Task<Result<EventResponseDto>> GetByEventTypeAsync(
        string broadcasterId,
        string eventType,
        CancellationToken cancellationToken = default
    );
    Task<Result<EventResponseDto>> UpsertAsync(
        string broadcasterId,
        string eventType,
        UpdateEventResponseDto request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Resets one event-response row back to its seeded default (disabled, chat_message, no message/pipeline).
    /// Never removes the row — rows are a fixed, seeded catalogue keyed by event type
    /// (S-EVENTRESPONSE-NO-CREATE), not user-created/deletable. A row bound to a pipeline, command or
    /// action is only reset when <paramref name="confirmed"/> is true; otherwise the call fails with
    /// <c>CONFIRMATION_REQUIRED</c>, names the binding that would be lost, and changes nothing.
    /// </summary>
    Task<Result> ResetToDefaultAsync(
        string broadcasterId,
        string eventType,
        bool confirmed = false,
        CancellationToken cancellationToken = default
    );
}
