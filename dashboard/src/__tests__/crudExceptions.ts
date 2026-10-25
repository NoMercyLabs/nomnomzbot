// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// Server create / edit / delete endpoints the dashboard is meant never to call, with the reason.
// Keyed by entity, then by the endpoint ("VERB /path"). Only add a row after you read the code
// that proves the reason; a gap that is only not built yet belongs in crudBaseline.ts instead.
export const crudExceptions = {
  'auth/twitch/callback': {
    'POST /auth/twitch/callback':
      'Sign-in code exchange for the mobile and SPA flow; the dashboard signs in through the ' +
      'server redirect built in api/signIn.ts, and this is not data a person manages.',
  },
  rewards: {
    'PUT /channels/{channelId}/rewards/{rewardId}':
      'Full-replace twin of the PATCH route the dashboard calls for the same edit.',
  },
  'stream/title': {
    'PATCH /channels/{channelId}/stream/title':
      'One-field twin of the PUT /stream route (api/stream.ts useSaveStreamInfo) that saves title, category and tags together.',
  },
  'stream/game': {
    'PATCH /channels/{channelId}/stream/game':
      'One-field twin of the PUT /stream route (api/stream.ts useSaveStreamInfo) that saves title, category and tags together.',
  },
  'stream/tags': {
    'PATCH /channels/{channelId}/stream/tags':
      'One-field twin of the PUT /stream route (api/stream.ts useSaveStreamInfo) that saves title, category and tags together.',
  },
  'chat/settings': {
    'PATCH /channels/{channelId}/chat/settings':
      'Partial-update twin of the PUT route the dashboard calls for the same edit.',
  },
} as const satisfies Record<string, Record<string, string>>;
