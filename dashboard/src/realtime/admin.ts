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
} from '@/api/admin'

import type { HubEventContext, HubEventHandler } from './hubEvent'
import type { AdminChannelRegistryPayload, AdminSystemStatusPayload } from './payloads'

// What a registry push says about one row. Absent fields stay as they are.
function patchOf(update: AdminChannelRegistryPayload): Partial<AdminTenant> {
  const patch: Partial<AdminTenant> = {}

  if (update.isLive !== undefined) patch.isLive = update.isLive
  if (update.status !== undefined) patch.status = update.status

  return patch
}

function applyRegistryUpdate(
  { queries }: HubEventContext,
  update: AdminChannelRegistryPayload,
): void {
  const patch = patchOf(update)
  let found = false

  for (const [key, page] of queries.getQueriesData<AdminTenantsPage>({
    queryKey: adminTenantsPrefix,
  })) {
    if (!page?.data?.some((row: AdminTenant) => row.id === update.broadcasterId)) continue
    found = true
    queries.setQueryData<AdminTenantsPage>(key, {
      ...page,
      data: page.data.map((row: AdminTenant) =>
        row.id === update.broadcasterId ? { ...row, ...patch } : row,
      ),
    })
  }

  if (update.status !== undefined) {
    queries.setQueryData<AdminTenantDetail | null>(
      adminTenantKey(update.broadcasterId),
      (detail) => (detail ? { ...detail, status: update.status! } : detail),
    )
  }

  // A channel that is on no cached page cannot be patched, and it may belong on one: refetch the lists.
  if (!found) void queries.invalidateQueries({ queryKey: adminTenantsPrefix })
}

// The operator hub pushes the full snapshot every 15 seconds, so the payload replaces the cache entry.
export const adminHandlers: [
  HubEventHandler<AdminSystemStatusPayload>,
  HubEventHandler<AdminChannelRegistryPayload>,
] = [
  {
    event: 'ReceiveSystemStatus',
    handle: ({ queries }, snapshot) => {
      queries.setQueryData<AdminSystemStatusPayload>(adminSystemStatusKey, snapshot)
    },
  },
  {
    event: 'ReceiveChannelRegistryUpdate',
    handle: applyRegistryUpdate,
  },
]
