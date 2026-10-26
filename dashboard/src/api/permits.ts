// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useMutation, useQueryClient } from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import { client } from './client';
import { failed } from './errors';
import { personChanged } from './people';

const version = '1';

// One permit of one person: the person's internal id (the permit names it), what the permit grants (a role name or
// an action key, as the bot matches it) and the person's Twitch id, so their profile reads again.
export type RevokePermitInput = {
  userId: string;
  grants: string;
  twitchUserId: string;
};

// A revoked permit is re-issuable, so there is no confirm.
export function useRevokePermit(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ userId, grants }: RevokePermitInput): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/permits/{userId}',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              userId,
            },
            query: { actionKeyOrRole: grants },
          },
        },
      );

      if (error !== undefined)
        throw failed('revoke permit', response.status, error);
    },
    onSuccess: (_data: void, { twitchUserId }: RevokePermitInput) =>
      personChanged(queries, toValue(channelId) as string, twitchUserId),
  });
}
