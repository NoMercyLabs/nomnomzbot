// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
import { adminErrorBudgetPrefix } from '@/api/adminErrorBudget';
import { contentPrefix } from '@/api/adminContent';
import { platformBotPrefix } from '@/api/adminPlatformBot';
import { trustSafetyPrefix } from '@/api/adminTrustSafety';

import type { HubEventContext, HubEventHandler } from './hubEvent';
import type {
  AdminAuditPayload,
  AdminBillingPayload,
  AdminChannelRegistryPayload,
  AdminContentPayload,
  AdminErrorBudgetPayload,
  AdminPlatformBotPayload,
  AdminSystemStatusPayload,
  AdminTrustSafetyPayload,
} from './payloads';

// What a registry push says about one row. Absent fields stay as they are.
function patchOf(update: AdminChannelRegistryPayload): Partial<AdminTenant> {
  const patch: Partial<AdminTenant> = {};

  if (update.isLive !== undefined) {
    patch.isLive = update.isLive;
  }

  if (update.status !== undefined) {
    patch.status = update.status;
  }

  return patch;
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
    if (!page?.data?.some((row: AdminTenant) => row.id === update.broadcasterId)) continue;

    found = true;

    queries.setQueryData<AdminTenantsPage>(key, {
      ...page,
      data: page.data.map((row: AdminTenant) =>
        row.id === update.broadcasterId
          ? { ...row, ...patch }
          : row,
      ),
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
  HubEventHandler<AdminBillingPayload>,
  HubEventHandler<AdminPlatformBotPayload>,
  HubEventHandler<AdminAuditPayload>,
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
    // Another operator changed billing: tiers, invites, invoices, grants and priced units reload.
    event: 'ReceiveBillingChanged',
    handle: ({ queries }) => {
      void queries.invalidateQueries({ queryKey: billingPrefix });
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
