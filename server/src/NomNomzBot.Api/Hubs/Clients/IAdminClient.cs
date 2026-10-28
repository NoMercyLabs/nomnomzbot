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

namespace NomNomzBot.Api.Hubs.Clients;

public interface IAdminClient
{
    Task ReceiveSystemStatus(object statusData);
    Task ReceiveLog(object logEntry);
    Task ReceiveChannelRegistryUpdate(object registryData);
    Task ReceiveTrustSafetyChanged(object change);
    Task ReceiveBillingChanged(object change);
    Task ReceiveContentChanged(object change);
    Task ReceiveErrorBudgetChanged(object change);
    Task ReceivePlatformBotChanged(object change);
    Task ReceiveAuditChanged(object change);
    Task ReceiveAccessChanged(object change);
    Task ReceiveDataRequestsChanged(object change);
    Task ReceiveEventReplayChanged(object change);
    Task ReceiveScheduledJobsChanged(object change);
    Task ReceivePlatformDefaultsChanged(object change);
}
