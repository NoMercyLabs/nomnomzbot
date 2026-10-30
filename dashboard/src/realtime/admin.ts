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

import {
  adminSystemStatusKey,
  adminTenantKey,
  adminTenantsPrefix,
  type AdminTenant,
  type AdminTenantDetail,
  type AdminTenantsPage,
} from '@/api/admin';
import { adminAuditPrefix } from '@/api/adminAudit';
import { billingPrefix } from '@/api/adminBilling';
import { accessPrefix } from '@/api/adminAccess';
import { adminErrorBudgetPrefix } from '@/api/adminErrorBudget';
import { contentPrefix } from '@/api/adminContent';
import { dataRequestsPrefix } from '@/api/adminDataRequests';
import { eventReplayPrefix } from '@/api/adminEventReplay';
import { adminScheduledJobsPrefix } from '@/api/adminActivity';
import { platformBotPrefix } from '@/api/adminPlatformBot';
import { adminPlatformDefaultsKey } from '@/api/adminPlatformDefaults';
import { adminTenantUsagePrefix } from '@/api/adminUsage';
import { trustSafetyPrefix } from '@/api/adminTrustSafety';

import type { HubEventContext, HubEventHandler } from './hubEvent';
import type {
  AdminAccessPayload,
  AdminAuditPayload,
  AdminBillingPayload,
  AdminChannelRegistryPayload,
  AdminContentPayload,
  AdminDataRequestsPayload,
  AdminEventReplayPayload,
  AdminErrorBudgetPayload,
  AdminPlatformBotPayload,
  AdminPlatformDefaultsPayload,
  AdminScheduledJobsPayload,
  AdminSystemStatusPayload,
  AdminTrustSafetyPayload,
} from './payloads';

type ChannelPatch = Partial<NonNullable<AdminTenant['channel']>>;

// What a registry push says about one channel. Absent fields stay as they are.
function patchOf(update: AdminChannelRegistryPayload): ChannelPatch {
  const patch: ChannelPatch = {};

  if (update.isLive !== undefined) {
    patch.isLive = update.isLive;
  }

  if (update.status !== undefined) {
    patch.status = update.status;
  }

  return patch;
}

// A row is a person; only a streamer row carries a channel, and only that channel takes the push.
function holdsChannel(row: AdminTenant, broadcasterId: string): boolean {
  return row.channel?.broadcasterId === broadcasterId;
}

function patchRow(row: AdminTenant, broadcasterId: string, patch: ChannelPatch) {
  if (!row.channel || !holdsChannel(row, broadcasterId))
    return row;

  return { ...row, channel: { ...row.channel, ...patch } } as const satisfies AdminTenant;
}

function applyRegistryUpdate(
  { queries }: HubEventContext,
  update: AdminChannelRegistryPayload,
): void {
  const patch = patchOf(update);
  let found = false;

  for (const [key, page] of queries.getQueriesData<AdminTenantsPage>({
    queryKey: adminTenantsPrefix,
  })) {
    if (!page?.data?.some((row: AdminTenant) => holdsChannel(row, update.broadcasterId))) continue;

    found = true;

    queries.setQueryData<AdminTenantsPage>(key, {
      ...page,
      data: page.data.map((row) => patchRow(row, update.broadcasterId, patch)),
    });
  }

  if (update.status !== undefined) {
    queries.setQueryData<AdminTenantDetail | null>(
      adminTenantKey(update.broadcasterId),
      (detail) =>
        detail
          ? { ...detail, status: update.status! }
          : detail,
    );
  }

  // A channel that is on no cached page cannot be patched, and it may belong on one: refetch the lists.
  if (!found) {
    void queries.invalidateQueries({ queryKey: adminTenantsPrefix });
  }
}

// The operator hub pushes the full snapshot every 15 seconds, so the payload replaces the cache entry.
export const adminHandlers: [
  HubEventHandler<AdminSystemStatusPayload>,
  HubEventHandler<AdminChannelRegistryPayload>,
  HubEventHandler<AdminTrustSafetyPayload>,
  HubEventHandler<AdminErrorBudgetPayload>,
  HubEventHandler<AdminContentPayload>,
  HubEventHandler<AdminAccessPayload>,
  HubEventHandler<AdminDataRequestsPayload>,
  HubEventHandler<AdminEventReplayPayload>,
  HubEventHandler<AdminBillingPayload>,
  HubEventHandler<AdminPlatformBotPayload>,
  HubEventHandler<AdminAuditPayload>,
  HubEventHandler<AdminPlatformDefaultsPayload>,
  HubEventHandler<AdminScheduledJobsPayload>,
] = [
  {
    event: 'ReceiveSystemStatus',
    handle: ({ queries }, snapshot) => {
      queries.setQueryData<AdminSystemStatusPayload>(adminSystemStatusKey, snapshot);
    },
  },
  {
    event: 'ReceiveChannelRegistryUpdate',
    handle: applyRegistryUpdate,
  },
  {
    // Another operator changed the desk: the queue and the block list reload, nothing is patched by hand.
    event: 'ReceiveTrustSafetyChanged',
    handle: ({ queries }) => {
      void queries.invalidateQueries({ queryKey: trustSafetyPrefix });
    },
  },
  {
    // A webhook attempt resolved, so one tenant's budget moved: the pages reload from the server.
    event: 'ReceiveErrorBudgetChanged',
    handle: ({ queries }) => {
      void queries.invalidateQueries({ queryKey: adminErrorBudgetPrefix });
    },
  },
  {
    // Another operator changed the catalog: the list and the open definition reload.
    event: 'ReceiveContentChanged',
    handle: ({ queries }) => {
      void queries.invalidateQueries({ queryKey: contentPrefix });
    },
  },
  {
    // Another operator changed platform access: the principals and the roles reload.
    event: 'ReceiveAccessChanged',
    handle: ({ queries }) => {
      void queries.invalidateQueries({ queryKey: accessPrefix });
    },
  },
  {
    // A data request changed state: the ledger and its counts reload.
    event: 'ReceiveDataRequestsChanged',
    handle: ({ queries }) => {
      void queries.invalidateQueries({ queryKey: dataRequestsPrefix });
    },
  },
  {
    // A replay finished: the list of executed replays reloads.
    event: 'ReceiveEventReplayChanged',
    handle: ({ queries }) => {
      void queries.invalidateQueries({ queryKey: eventReplayPrefix });
    },
  },
  {
    // Another operator changed billing: tiers, invites, invoices, grants and priced units reload.
    // A priced unit also moves the cost columns of the tenant usage page.
    event: 'ReceiveBillingChanged',
    handle: ({ queries }, change) => {
      void queries.invalidateQueries({ queryKey: billingPrefix });

      if (change.area === 'priced-unit') {
        void queries.invalidateQueries({ queryKey: adminTenantUsagePrefix });
      }
    },
  },
  {
    // Another operator changed a platform default: every section of the defaults page reloads.
    event: 'ReceivePlatformDefaultsChanged',
    handle: ({ queries }) => {
      void queries.invalidateQueries({ queryKey: adminPlatformDefaultsKey });
    },
  },
  {
    // A scheduled job was retried: the jobs list reloads.
    event: 'ReceiveScheduledJobsChanged',
    handle: ({ queries }) => {
      void queries.invalidateQueries({ queryKey: adminScheduledJobsPrefix });
    },
  },
  {
    // The shared platform bot connected or disconnected: the state reloads from the server.
    event: 'ReceivePlatformBotChanged',
    handle: ({ queries }) => {
      void queries.invalidateQueries({ queryKey: platformBotPrefix });
    },
  },
  {
    // A platform permission was checked and an audit row written: the log reloads from the server.
    event: 'ReceiveAuditChanged',
    handle: ({ queries }) => {
      void queries.invalidateQueries({ queryKey: adminAuditPrefix });
    },
  },
];
