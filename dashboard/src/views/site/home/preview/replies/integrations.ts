// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { sampleChannel } from '../../sampleChannel';
import type { FakeReply } from '../fakeApi';
import { channelPath, get, minutesAgo, one } from './shape';

type Provider = {
  id: string;
  name: string;
  category: string;
  description: string;
  connected: boolean;
  scopes?: string[];
  capabilities?: Record<string, boolean>;
};

const providers: Provider[] = [
  {
    id: 'twitch',
    name: 'Twitch',
    category: 'platform',
    description: 'Chat, follows, subscriptions, raids, rewards and moderation.',
    connected: true,
  },
  {
    id: 'kick',
    name: 'Kick',
    category: 'platform',
    description: 'Chat and channel events from your Kick channel.',
    connected: true,
    scopes: ['kick_chat'],
  },
  {
    id: 'youtube',
    name: 'YouTube',
    category: 'platform',
    description: 'Live chat, memberships and Super Chats.',
    connected: true,
    scopes: ['youtube_readonly'],
  },
  {
    id: 'spotify',
    name: 'Spotify',
    category: 'music',
    description: 'Song requests and playback control for your queue.',
    connected: true,
    scopes: ['spotify_playback', 'spotify_library'],
    capabilities: {
      playback: true,
      library: true,
      premium: true,
    },
  },
  {
    id: 'discord',
    name: 'Discord',
    category: 'community',
    description: 'Go-live posts and role sync for your server.',
    connected: true,
  },
  {
    id: 'obs',
    name: 'OBS Studio',
    category: 'streaming',
    description: 'Scenes, sources and filters from a pipeline step.',
    connected: false,
  },
];

const integrations = providers.map((provider) => ({
  id: provider.id,
  name: provider.name,
  category: provider.category,
  description: provider.description,
  connected: provider.connected,
  connectedAs: provider.connected
    ? sampleChannel.handle
    : null,
  oauthUrl: `https://auth.example.test/${provider.id}`,
  lastSync: provider.connected
    ? minutesAgo(12)
    : null,
}));

const statuses = providers.map((provider) => ({
  provider: provider.id,
  connected: provider.connected,
  accountName: provider.connected
    ? sampleChannel.handle
    : null,
  grantedScopeSets: provider.scopes ?? [],
  capabilities: provider.capabilities ?? {},
  needsReauth: false,
  loginOnly: false,
  decryptFailed: false,
  connectionId: `sample-connection-${provider.id}`,
}));

export const integrationReplies: FakeReply[] = [
  get(`${channelPath}/integrations`, one({ integrations })),
  get(`${channelPath}/integrations/status`, one(statuses)),
  get(`${channelPath}/integrations/spotify/credentials`, one({
    clientId: null,
    hasClientSecret: false,
    hasSharedApp: true,
  })),
];
