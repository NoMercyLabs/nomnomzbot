// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
