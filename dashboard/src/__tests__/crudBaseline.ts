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

// Server create / edit / delete endpoints that no dashboard code calls yet (S-CRUD-COMPLETE,
// 2026-10-25). The rows are grandfathered: this map only ever shrinks. Build the missing UI
// action, delete its row here, and crudCoverage.spec.ts keeps it deleted. Adding a row is not
// allowed; an endpoint that must stay uncalled goes in crudExceptions.ts with its reason.
// Print the live picture with: node dashboard/scripts/crud-audit.mjs --gaps
export const crudBaseline: Record<string, readonly string[]> = {
  'admin/impersonation': ['DELETE /admin/impersonation/{accessGrantId}'],
  assets: ['POST /assets', 'DELETE /assets/{id}'],
  'auth/twitch/bot': ['DELETE /auth/twitch/bot'],
  'bundles/installed': ['DELETE /channels/{channelId}/bundles/installed/{id}'],
  channels: ['POST /channels', 'PUT /channels/{channelId}'],
  'chat-polls': ['POST /channels/{channelId}/chat-polls'],
  'code-scripts/versions': ['POST /code-scripts/{id}/versions'],
  'compliance/erasure': ['POST /compliance/erasure'],
  'economy/games/consent': [
    'POST /channels/{channelId}/economy/games/consent',
    'DELETE /channels/{channelId}/economy/games/consent/{viewerUserId}',
  ],
  'economy/jars/memberships': [
    'DELETE /channels/{channelId}/economy/jars/memberships/{membershipId}',
  ],
  'eventsub/subscriptions': [
    'POST /channels/{channelId}/eventsub/subscriptions',
    'DELETE /channels/{channelId}/eventsub/subscriptions/{id}',
  ],
  'federation/peers': ['POST /federation/peers'],
  'federation/peers/keys': [
    'POST /federation/peers/{peerId}/keys',
    'DELETE /federation/peers/{peerId}/keys/{keyId}',
  ],
  'marketplace/publisher-token': [
    'PUT /channels/{channelId}/marketplace/publisher-token',
    'DELETE /channels/{channelId}/marketplace/publisher-token',
  ],
  'moderation/actions/mass-ban/opt-ins': [
    'PUT /channels/{channelId}/moderation/actions/mass-ban/opt-ins/{broadcasterLogin}',
    'DELETE /channels/{channelId}/moderation/actions/mass-ban/opt-ins/{broadcasterLogin}',
  ],
  'moderation/blocked-terms/all-moderated': [
    'POST /channels/{channelId}/moderation/blocked-terms/all-moderated',
    'DELETE /channels/{channelId}/moderation/blocked-terms/all-moderated/{term}',
  ],
  'moderation/nuke': ['POST /channels/{channelId}/moderation/nuke'],
  'moderation/reports': ['POST /channels/{channelId}/moderation/reports'],
  'moderation/rules': [
    'POST /channels/{channelId}/moderation/rules',
    'PUT /channels/{channelId}/moderation/rules/{ruleId}',
    'DELETE /channels/{channelId}/moderation/rules/{ruleId}',
  ],
  'moderation/shared-bans': ['PUT /channels/{channelId}/moderation/shared-bans'],
  'moderation/shared-bans/trusted': [
    'POST /channels/{channelId}/moderation/shared-bans/trusted',
    'DELETE /channels/{channelId}/moderation/shared-bans/trusted/{trustedChannelId}',
  ],
  'music/queue': ['POST /channels/{channelId}/music/queue'],
  'music/repeat': ['PATCH /channels/{channelId}/music/repeat'],
  'music/shuffle': ['PATCH /channels/{channelId}/music/shuffle'],
  'music/volume': ['PUT /channels/{channelId}/music/volume'],
  'platform/content/audio-assets': [
    'POST /platform/content/audio-assets',
    'DELETE /platform/content/audio-assets/{id}',
  ],
  'platform/iam/principals': ['POST /platform/iam/principals'],
  'spam-defense/lockdown': ['POST /channels/{channelId}/spam-defense/lockdown'],
  'system/ipc/keys': ['POST /system/ipc/keys', 'DELETE /system/ipc/keys/{keyId}'],
  'system/setup/credentials/kick': ['PUT /system/setup/credentials/kick'],
  'users/profile': ['PUT /users/{userId}/profile'],
  'viewers/data': ['PUT /viewers/{viewerId}/data/{key}', 'DELETE /viewers/{viewerId}/data/{key}'],
  'widget-gallery': ['POST /widget-gallery'],
};
