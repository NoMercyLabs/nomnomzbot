// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useMutation } from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import { client } from './client';
import { failed } from './errors';

const version = '1';

// Sends the channel's viewers to another channel at the end of the stream. Twitch starts the raid after a short
// countdown, so it can still be canceled until then.
export function useStartRaid(channelId: MaybeRefOrGetter<string | null>) {
  return useMutation({
    mutationFn: async (targetTwitchBroadcasterId: string): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/live-ops/raids',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { targetTwitchBroadcasterId },
        },
      );

      if (error !== undefined)
        throw failed('start raid', response.status, error);
    },
  });
}

export function useCancelRaid(channelId: MaybeRefOrGetter<string | null>) {
  return useMutation({
    mutationFn: async (): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/live-ops/raids',
        { params: { path: { version, channelId: toValue(channelId) as string } } },
      );

      if (error !== undefined)
        throw failed('cancel raid', response.status, error);
    },
  });
}
