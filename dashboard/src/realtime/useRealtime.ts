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

import { watch } from 'vue';

import { queryClient } from '@/api/queryClient';
import { useCurrentUser, useSignedIn } from '@/api/auth';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import { startAdminHub, stopAdminHub } from './adminHub';
import { startDashboardHub, stopDashboardHub, watchChannel } from './dashboardHub';

// Called once by the dashboard frame: both hubs run while the person is signed in, and stop at sign-out.
export function useRealtime(): void {
  const signedIn = useSignedIn();
  const user = useCurrentUser();
  const { channelId } = useCurrentChannel();

  watch(
    signedIn,
    (on: boolean) => {
      if (!on) {
        void stopDashboardHub();

        return;
      }

      void watchChannel(channelId.value);
      void startDashboardHub(queryClient);
    },
    { immediate: true },
  );

  watch(channelId, (id: string | null) => {
    if (signedIn.value) {
      void watchChannel(id);
    }
  });

  watch(
    () => signedIn.value && user.data.value?.isAdmin === true,
    (operator: boolean) => {
      if (operator) {
        void startAdminHub(queryClient);
      } else {
        void stopAdminHub();
      }
    },
    { immediate: true },
  );
}
