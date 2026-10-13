// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { HubEventHandler } from './hubEvent'
import type { AdminSystemStatusPayload } from './payloads'

// No api/admin.ts exists yet. It takes this key over when the admin pages arrive.
export const adminSystemStatusKey: unknown[] = ['admin', 'system-status']

// The operator hub pushes the full snapshot every 15 seconds, so the payload replaces the cache entry.
export const adminHandlers: [HubEventHandler<AdminSystemStatusPayload>] = [
  {
    event: 'ReceiveSystemStatus',
    handle: ({ queries }, snapshot) => {
      queries.setQueryData<AdminSystemStatusPayload>(adminSystemStatusKey, snapshot)
    },
  },
]
