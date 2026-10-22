// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import {
  useMutation,
  useQuery,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { useOptimisticSave } from '@/composables/useOptimisticSave';
import type { KeyParts } from '@/lib/array';

import { client } from './client';
import { failed } from './errors';
import type { components } from './schema';

export type PlayerButton = components['schemas']['PlayerButton'];
export type ActionRunRequest = components['schemas']['ChannelActionRunRequest'];
export type ActionRunResult = components['schemas']['ChannelActionRunResponse'];

// The server keeps at most this many buttons (PlayerButtonService) and refuses a longer label. A label may be
// empty: the button then shows only its icon.
export const MAX_PLAYER_BUTTONS = 12;
export const MAX_BUTTON_LABEL = 24;

const ICON_KEY = /^[a-z0-9-]+$/;

// What is wrong with one button, as the i18n keys of the messages; a key is absent when that part is fine.
export type ButtonProblems = {
  label?: string;
  icon?: string;
  action?: string;
};

// The same rules the bot enforces on a save (PlayerButtonService), so a refusal is shown before it is sent.
export function buttonProblems(button: PlayerButton): ButtonProblems {
  const length = button.label.trim().length;
  const problems: ButtonProblems = {};

  if (length > MAX_BUTTON_LABEL) {
    problems.label = 'dashboard.music.now.buttons.editor.problem.label';
  }

  if (!ICON_KEY.test(button.icon)) {
    problems.icon = 'dashboard.music.now.buttons.editor.problem.icon';
  }

  if (button.actionType.trim() === '') {
    problems.action = 'dashboard.music.now.buttons.editor.problem.action';
  }

  return problems;
}

const version = '1';

export const playerButtonsKey = (channelId: string | null) => [
  'channels',
  channelId,
  'music-player-buttons',
] as const satisfies KeyParts;

async function readButtons(channelId: string): Promise<PlayerButton[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/music/player-buttons',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined)
    throw failed('player buttons', response.status, error);

  return data?.data ?? [];
}

// The buttons the streamer pinned to the Now playing panel, in the order they are shown.
export function usePlayerButtons(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<PlayerButton[], Error> {
  return useQuery({
    queryKey: computed(() =>
      playerButtonsKey(toValue(channelId))),
    queryFn: () =>
      readButtons(toValue(channelId) as string),
    enabled: computed(() =>
      toValue(channelId) !== null),
  });
}

// The whole list travels in one PUT: the bot keeps the order it is given.
export function useSavePlayerButtons(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<PlayerButton[], Error, PlayerButton[], number> {
  return useOptimisticSave<PlayerButton[], PlayerButton[]>({
    describe: () => ({ key: 'savePlayerButtons' }),
    queryKey: () =>
      playerButtonsKey(toValue(channelId)),
    mutationFn: async (buttons: PlayerButton[]): Promise<PlayerButton[]> => {
      const { data, error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/music/player-buttons',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: buttons,
        },
      );

      if (error !== undefined)
        throw failed('save player buttons', response.status, error);

      return data?.data ?? buttons;
    },
    apply: (_current: PlayerButton[] | undefined, buttons: PlayerButton[]): PlayerButton[] =>
      buttons,
  });
}

// Runs any action of the pipeline catalog once, seeded with the given variables.
export function useRunAction(channelId: MaybeRefOrGetter<string | null>) {
  return useMutation<ActionRunResult, Error, ActionRunRequest>({
    mutationFn: async (request: ActionRunRequest): Promise<ActionRunResult> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/actions/run',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: request,
        },
      );

      if (error !== undefined || !data?.data)
        throw failed('run action', response.status, error);

      return data.data;
    },
  });
}
