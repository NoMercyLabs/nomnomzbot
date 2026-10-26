// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// Server create / edit / delete endpoints that no dashboard code calls yet (S-CRUD-COMPLETE,
// 2026-10-25). The rows are grandfathered: this map only ever shrinks. Build the missing UI
// action, delete its row here, and crudCoverage.spec.ts keeps it deleted. Adding a row is not
// allowed; an endpoint that must stay uncalled goes in crudExceptions.ts with its reason.
// Print the live picture with: node dashboard/scripts/crud-audit.mjs --gaps
export const crudBaseline: Record<string, readonly string[]> = {
  'admin/impersonation': ['DELETE /admin/impersonation/{accessGrantId}'],
  'admin/support-contact': ['PUT /admin/support-contact'],
  assets: ['POST /assets', 'DELETE /assets/{id}'],
  'auth/twitch/bot': ['DELETE /auth/twitch/bot'],
  'bundles/installed': ['DELETE /channels/{channelId}/bundles/installed/{id}'],
  channels: ['POST /channels', 'PUT /channels/{channelId}'],
  'chat-polls': ['POST /channels/{channelId}/chat-polls'],
  'code-scripts/versions': ['POST /code-scripts/{id}/versions'],
  'community/trust': ['PUT /channels/{channelId}/community/{userId}/trust'],
  'compliance/erasure': ['POST /compliance/erasure'],
  'economy/games/consent': [
    'POST /channels/{channelId}/economy/games/consent',
    'DELETE /channels/{channelId}/economy/games/consent/{viewerUserId}',
  ],
  'economy/jars/memberships': [
    'DELETE /channels/{channelId}/economy/jars/memberships/{membershipId}',
  ],
  'egress-allowlist': [
    'POST /channels/{channelId}/egress-allowlist',
    'DELETE /channels/{channelId}/egress-allowlist/{allowlistId}',
  ],
  'egress-allowlist/enabled': ['PUT /channels/{channelId}/egress-allowlist/{allowlistId}/enabled'],
  'eventsub/subscriptions': [
    'POST /channels/{channelId}/eventsub/subscriptions',
    'DELETE /channels/{channelId}/eventsub/subscriptions/{id}',
  ],
  'federation/peers': ['POST /federation/peers'],
  'federation/peers/keys': [
    'POST /federation/peers/{peerId}/keys',
    'DELETE /federation/peers/{peerId}/keys/{keyId}',
  ],
  'live-ops/raids': ['DELETE /channels/{channelId}/live-ops/raids'],
  'marketplace/publisher-token': [
    'PUT /channels/{channelId}/marketplace/publisher-token',
    'DELETE /channels/{channelId}/marketplace/publisher-token',
  ],
  'moderation/actions/mass-ban/opt-ins': [
    'PUT /channels/{channelId}/moderation/actions/mass-ban/opt-ins/{broadcasterLogin}',
    'DELETE /channels/{channelId}/moderation/actions/mass-ban/opt-ins/{broadcasterLogin}',
  ],
  'moderation/bans': ['DELETE /channels/{channelId}/moderation/bans/{userId}'],
  'moderation/blocked-terms/all-moderated': [
    'POST /channels/{channelId}/moderation/blocked-terms/all-moderated',
    'DELETE /channels/{channelId}/moderation/blocked-terms/all-moderated/{term}',
  ],
  'moderation/chat-filters': [
    'POST /channels/{channelId}/moderation/chat-filters',
    'PUT /channels/{channelId}/moderation/chat-filters/{filterId}',
    'DELETE /channels/{channelId}/moderation/chat-filters/{filterId}',
  ],
  'moderation/nuke': ['POST /channels/{channelId}/moderation/nuke'],
  'moderation/reports': [
    'POST /channels/{channelId}/moderation/reports',
    'PATCH /channels/{channelId}/moderation/reports/{reportId}',
  ],
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
  permits: ['DELETE /channels/{channelId}/permits/{userId}'],
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
