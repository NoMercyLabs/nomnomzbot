// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { RouteRecordRaw } from 'vue-router';

import { loadPage } from '@/i18n';

const chatPage = () =>
  loadPage(
    () => import('./ChatView.vue'),
    (locale) => import(`./i18n/${locale}.json`),
  );

export const dashboardChatRoute = {
  path: 'chat',
  name: 'dashboard-chat',
  component: chatPage,
} as const satisfies RouteRecordRaw;

// The same page as a viewer sees it, for a person without the operator chat (chat-client.md 9a).
export const dashboardViewerChatRoute = {
  path: 'viewer-chat',
  name: 'dashboard-viewer-chat',
  component: chatPage,
  meta: { viewerChat: true },
} as const satisfies RouteRecordRaw;

// The viewer chat under the viewer frame, for one channel by its name.
export const viewerChatRoute = {
  path: ':channel/chat',
  name: 'viewer-chat',
  component: chatPage,
  meta: { viewerChat: true },
} as const satisfies RouteRecordRaw;

// The same page in its own window, under the popout frame.
export const popoutChatRoute = {
  path: 'chat',
  name: 'popout-chat',
  component: chatPage,
  meta: { popout: true },
} as const satisfies RouteRecordRaw;
