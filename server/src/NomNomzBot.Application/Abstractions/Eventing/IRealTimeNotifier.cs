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

namespace NomNomzBot.Application.Abstractions.Eventing;

public interface IRealTimeNotifier
{
    Task NotifyChannelAsync(
        string channelId,
        string eventName,
        object payload,
        CancellationToken ct = default
    );
    Task NotifyAllAsync(string eventName, object payload, CancellationToken ct = default);
}
