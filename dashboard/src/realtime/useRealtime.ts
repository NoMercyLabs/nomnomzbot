// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { watch } from 'vue'

import { queryClient } from '@/api/queryClient'
import { useCurrentUser, useSignedIn } from '@/api/auth'
import { useCurrentChannel } from '@/composables/useCurrentChannel'

import { startAdminHub, stopAdminHub } from './adminHub'
import { startDashboardHub, stopDashboardHub, watchChannel } from './dashboardHub'

// Called once by the dashboard frame: both hubs run while the person is signed in, and stop at sign-out.
export function useRealtime(): void {
  const signedIn = useSignedIn()
  const user = useCurrentUser()
  const { channelId } = useCurrentChannel()

  watch(
    signedIn,
    (on: boolean) => {
      if (!on) {
        void stopDashboardHub()

        return
      }

      void watchChannel(channelId.value)
      void startDashboardHub(queryClient)
    },
    { immediate: true },
  )

  watch(channelId, (id: string | null) => {
    if (signedIn.value) void watchChannel(id)
  })

  watch(
    () => signedIn.value && user.data.value?.isAdmin === true,
    (operator: boolean) => {
      if (operator) void startAdminHub(queryClient)
      else void stopAdminHub()
    },
    { immediate: true },
  )
}
