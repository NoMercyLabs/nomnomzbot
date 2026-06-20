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
using NomNomzBot.Application.Contracts.EventStore;

namespace NomNomzBot.Infrastructure.EventStore;

/// <summary>
/// Builds the <c>(EventType, FromVersion)</c> chain map from the injected upcasters and applies it on read.
/// Brings a stored payload from its recorded version up to the current version one step at a time
/// (<c>v1→v2→v3</c>), so projections and replay only ever see the current shape. Pure / stateless after
/// construction — registered as a singleton. The current version of a type is one above its highest registered
/// <c>FromVersion</c>; a type with no upcaster is implicitly at version 1.
/// </summary>
public sealed class EventUpcasterRegistry : IEventUpcasterRegistry
{
    private readonly Dictionary<(string EventType, int FromVersion), IEventUpcaster> _chain;
    private readonly Dictionary<string, int> _currentVersions;

    public EventUpcasterRegistry(IEnumerable<IEventUpcaster> upcasters)
    {
        _chain = [];
        _currentVersions = [];

        foreach (IEventUpcaster upcaster in upcasters)
        {
            (string EventType, int FromVersion) key = (upcaster.EventType, upcaster.FromVersion);
            if (!_chain.TryAdd(key, upcaster))
                throw new InvalidOperationException(
                    $"Duplicate upcaster for event type '{upcaster.EventType}' from version "
                        + $"{upcaster.FromVersion}. Each (EventType, FromVersion) step must be unique."
                );

            // The current version is one past the highest FromVersion any upcaster transforms.
            int candidate = upcaster.FromVersion + 1;
            if (
                !_currentVersions.TryGetValue(upcaster.EventType, out int known)
                || candidate > known
            )
                _currentVersions[upcaster.EventType] = candidate;
        }
    }

    public Result<UpcastResult> UpcastToCurrent(
        string eventType,
        int fromVersion,
        string payloadJson
    )
    {
        int current = CurrentVersion(eventType);
        if (fromVersion >= current)
            return Result.Success(new UpcastResult(payloadJson, fromVersion, Changed: false));

        string payload = payloadJson;
        for (int version = fromVersion; version < current; version++)
        {
            if (!_chain.TryGetValue((eventType, version), out IEventUpcaster? upcaster))
                return Result.Failure<UpcastResult>(
                    $"No upcaster registered for event type '{eventType}' from version {version}; "
                        + $"the chain to version {current} is broken.",
                    "UPCASTER_CHAIN_BROKEN"
                );

            Result<string> step = upcaster.Upcast(payload);
            if (step.IsFailure)
                return Result.Failure<UpcastResult>(
                    step.ErrorMessage!,
                    step.ErrorCode,
                    step.ErrorDetail
                );

            payload = step.Value;
        }

        return Result.Success(new UpcastResult(payload, current, Changed: true));
    }

    public int CurrentVersion(string eventType) =>
        _currentVersions.TryGetValue(eventType, out int version) ? version : 1;
}
