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
