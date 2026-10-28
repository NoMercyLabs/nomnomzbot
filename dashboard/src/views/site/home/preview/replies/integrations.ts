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

import { sampleChannel } from '../../sampleChannel';
import type { FakeReply } from '../fakeApi';
import integrationsCapture from '../fixtures/integrations.json';
import integrationsSpotify from '../fixtures/integrations-spotify.json';
import integrationStatus from '../fixtures/integrations-status.json';
import { channelPath, fixture, get, minutesAgo } from './shape';

// A fresh install has only Twitch connected. The sample channel has also linked these providers, which is the
// part that is sample: the providers, names, categories and scopes are the server's own.
const linked = {
  spotify: ['spotify_playback', 'spotify_library'],
  kick: ['kick_chat'],
  youtube: ['youtube_readonly'],
  discord: [],
} as const satisfies Record<string, readonly string[]>;

function isLinked(provider: string): provider is keyof typeof linked {
  return provider in linked;
}

const integrations = fixture(integrationsCapture).data.integrations.map((integration) =>
  isLinked(integration.id)
    ? {
        ...integration,
        connected: true,
        connectedAs: sampleChannel.handle,
        lastSync: minutesAgo(12),
      }
    : integration);

const statuses = fixture(integrationStatus).data.map((status) =>
  isLinked(status.provider)
    ? {
        ...status,
        connected: true,
        accountName: sampleChannel.handle,
        grantedScopeSets: [...linked[status.provider]],
      }
    : status);

export const integrationReplies: FakeReply[] = [
  get(`${channelPath}/integrations`, {
    status: 'ok',
    data: { integrations },
    args: [],
  }),
  get(`${channelPath}/integrations/status`, {
    status: 'ok',
    data: statuses,
    args: [],
  }),
  get(`${channelPath}/integrations/spotify/credentials`, fixture(integrationsSpotify)),
  // A channel that has not connected its own bot account speaks through the shared bot.
  get(`${channelPath}/bot/status`, {
    status: 'success',
    data: {
      connected: true,
      login: null,
      displayName: null,
      profileImageUrl: null,
      usesSharedBot: true,
    },
  }),
];
