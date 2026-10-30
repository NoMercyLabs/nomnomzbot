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

import { defineComponent } from 'vue';
import { createMemoryHistory, createRouter, type RouteRecordRaw, type Router } from 'vue-router';

import { dashboardChannelRoutes, dashboardRoutes } from '../dashboard';

// vue-router imports the component of the page it lands on, and the frame around it, on every navigation. A spec
// that only checks where a guard sends a person would then load the frame and a whole page cold in its worker
// (7 to 10 s alone, far more beside a build). The guard specs navigate this stand-in instead; nothing in them
// reads a page.
const StubPage = defineComponent({
  name: 'StubPage',
  render: () => null,
});

// The same record with a stand-in where the page or the frame was: path, name, meta, redirect, props and every
// guard stay the dashboard's own, and so do the children, down to the last level.
export function withStubPages(route: RouteRecordRaw): RouteRecordRaw {
  const page = 'component' in route
    ? { component: StubPage }
    : {};
  const nested = route.children === undefined
    ? {}
    : { children: route.children.map(withStubPages) };

  return {
    ...route,
    ...page,
    ...nested,
  } as RouteRecordRaw;
}

// A router over the real dashboard route table (the pages and the channel twins) that loads no page. The caller
// adds the guards it checks.
export function stubbedDashboardRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [dashboardRoutes, dashboardChannelRoutes].map(withStubPages),
  });
}
