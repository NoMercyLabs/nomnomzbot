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

import { startDashboardHub, watchViewerMusic } from './dashboardHub';

// Keeps a viewer page live: the connection runs while the page is open, and the page follows the queue pushes
// of one channel. The viewer frame has no management connection of its own.
export function useViewerMusicLive(channelId: Ref<string | null>): void {
  void startDashboardHub(queryClient);

  watch(
    channelId,
    (id: string | null) => {
      void watchViewerMusic(id);
    },
    { immediate: true },
  );

  onScopeDispose(() => {
    void watchViewerMusic(null);
  });
}
