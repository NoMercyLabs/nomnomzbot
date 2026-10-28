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

import { onScopeDispose, watch, type Ref } from 'vue';

import { queryClient } from '@/api/queryClient';

import { startDashboardHub, watchViewerChat } from './dashboardHub';

// Keeps the viewer chat live: the page follows one channel's chat group while it is open, with no polling.
export function useViewerChatLive(channelId: Ref<string | null>): void {
  void startDashboardHub(queryClient);

  watch(
    channelId,
    (id) => {
      void watchViewerChat(id);
    },
    { immediate: true },
  );

  onScopeDispose(() => {
    void watchViewerChat(null);
  });
}
