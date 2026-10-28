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

import type { RouteRecordRaw } from 'vue-router';

import { merge } from '@/lib/array';
import { dashboardRoutes } from '@/router/dashboard';

import { sampleChannel } from '../../sampleChannel';
import type { FakeReply } from '../fakeApi';
import { ownerActionKeys } from './actionKeys';
import { channelPath, get, list, minutesAgo, one, sampleChannelId } from './shape';

// The sample owner may open every page the dashboard lists, so the sidebar shows them all. A page that
// asks for read access also asks for write access in its buttons, so both keys are held.
function pageActions(routes: readonly RouteRecordRaw[]): string[] {
  return routes
    .map((route) => route.meta?.action)
    .filter((action): action is string => typeof action === 'string')
    .flatMap((action) => [action, action.replace(':read', ':write')]);
}

const access = {
  userId: 'sample-owner',
  broadcasterId: sampleChannelId,
  effectiveLevel: '100',
  communityStanding: 'broadcaster',
  communityLevel: '100',
  managementRole: 'owner',
  managementLevel: '100',
  permitCapabilities: [],
  winningSource: 'broadcaster',
} as const;

const heldKeys = merge(pageActions(dashboardRoutes.children), ownerActionKeys);

export const baseReplies: FakeReply[] = [
  get(
    '/api/v1/channels',
    list([
      {
        id: sampleChannelId,
        login: sampleChannel.handle,
        displayName: sampleChannel.handle,
        profileImageUrl: null,
        isLive: true,
        role: 'broadcaster',
        viewerCount: sampleChannel.viewers,
        provider: 'twitch',
        chatColor: null,
        suspension: null,
      },
    ]),
  ),
  get('/api/v1/channels/moderated', one([])),
  get(
    '/api/v1/auth/me',
    one({
      id: 'sample-owner',
      username: sampleChannel.handle,
      displayName: sampleChannel.handle,
      profileImageUrl: null,
      color: null,
      broadcasterType: 'affiliate',
      isAdmin: false,
      createdAt: minutesAgo(60 * 24 * 400),
      platformPermissions: [],
    }),
  ),
  get(
    `${channelPath}/roles/effective/me`,
    () => one({ ...access, heldActionKeys: heldKeys }),
  ),
  get(`${channelPath}/stream/status`, one({ isLive: true, startedAt: minutesAgo(sampleChannel.uptimeMinutes) })),
];
