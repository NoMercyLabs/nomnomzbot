// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { createPinia } from 'pinia';
import { createApp, type App } from 'vue';
import { createMemoryHistory, createRouter, RouterView, type Router } from 'vue-router';

import { i18n } from '@/i18n';
import { dashboardChannelRoutes, dashboardRoutes } from '@/router/dashboard';

import PreviewRoot from './PreviewRoot.vue';

export type PreviewApp = {
  app: App;
  router: Router;
};

// A small app of its own, so the real page runs with its own router, its own cache and its own state, apart from
// the home page around it. The cache never goes stale and never refetches: the sample answers once.
export function createPreviewApp(path: string, shell: boolean): PreviewApp {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: Infinity,
        retry: false,
        refetchOnWindowFocus: false,
        refetchOnReconnect: false,
      },
    },
  });
  // The history starts on the page, so the first navigation matches a route and no empty path is tried.
  const history = createMemoryHistory();

  history.replace(path);

  const router = createRouter({
    history,
    routes: [
      {
        path: '/app',
        component: RouterView,
        children: dashboardRoutes.children,
      },
      // The sidebar links name the channel twin of each page, so the twins must resolve too.
      {
        path: '/app/u/:channelName',
        component: RouterView,
        children: dashboardChannelRoutes.children,
      },
    ],
  });
  const app = createApp(PreviewRoot, { shell });

  app.use(createPinia());
  app.use(VueQueryPlugin, { queryClient });
  app.use(i18n);
  app.use(router);

  return { app, router } as const satisfies PreviewApp;
}
