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
  useQueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import { useOptimisticSave } from '@/composables/useOptimisticSave';
import type { KeyParts } from '@/lib/array';
import { targetToWire, type PickedTarget } from '@/lib/bindingTarget';

import { channelQuery, failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

export type Giveaway = components['schemas']['Giveaway'];
export type GiveawayWinner = components['schemas']['GiveawayWinner'];

const version = '1';
const pageSize = 100;

export type GiveawayEntry = components['schemas']['GiveawayEntry'];
export type CodePool = components['schemas']['CodePool'];
export type GiveawayBlastRadius = components['schemas']['BlastRadius'];

// Who may enter. The server stores it as the eligibilityJson text; the dialog edits the four fields.
export type Eligibility = {
  requireSub: boolean;
  minStandingLevel: number | null;
  minWatchMinutes: number | null;
  minAccountAgeDays: number | null;
};

// Tickets per viewer kind (sub-luck). All 1 means no weighting.
export type Weighting = {
  subT1: number;
  subT2: number;
  subT3: number;
  vip: number;
};

export type GiveawayDraft = {
  title: string;
  entryMode: string;
  keyword: string;
  entryCost: number | null;
  maxEntriesPerUser: number;
  eligibility: Eligibility;
  weighting: Weighting;
  winnerCount: number;
  excludeModerators: boolean;
  claimWindowMinutes: number | null;
  prizeMode: string;
  prizeCurrencyAmount: number | null;
  prizeFromPot: boolean;
  prizeTarget: PickedTarget | null;
  prizeCodePoolId: string | null;
  requires18Plus: boolean;
  scheduledCloseAt: string | null;
};

export const noEligibility = {
  requireSub: false,
  minStandingLevel: null,
  minWatchMinutes: null,
  minAccountAgeDays: null,
} as const satisfies Eligibility;

export const noWeighting = {
  subT1: 1,
  subT2: 1,
  subT3: 1,
  vip: 1,
} as const satisfies Weighting;

type JsonValue = string | number | boolean | null | undefined;
type JsonFields = Record<string, JsonValue>;

function parseJson(text: string | null | undefined) {
  if (!text)
    return {} as const satisfies JsonFields;

  try {
    return JSON.parse(text) as JsonFields;
  } catch {
    return {} as const satisfies JsonFields;
  }
}

const numberOr = (value: JsonValue, fallback: number): number =>
  typeof value === 'number'
    ? value
    : fallback;

const numberOrNull = (value: JsonValue): number | null =>
  typeof value === 'number'
    ? value
    : null;

export function readEligibility(text: string | null | undefined) {
  const json = parseJson(text);

  return {
    requireSub: json.require_sub === true,
    minStandingLevel: numberOrNull(json.min_standing_level),
    minWatchMinutes: numberOrNull(json.min_watch_minutes),
    minAccountAgeDays: numberOrNull(json.min_account_age_days),
  } as const satisfies Eligibility;
}

export function readWeighting(text: string | null | undefined) {
  const json = parseJson(text);

  return {
    subT1: numberOr(json.sub_t1, 1),
    subT2: numberOr(json.sub_t2, 1),
    subT3: numberOr(json.sub_t3, 1),
    vip: numberOr(json.vip, 1),
  } as const satisfies Weighting;
}

// No rule set means no text at all, so a giveaway without limits stays as the server defaults it.
export function eligibilityText(rules: Eligibility): string | null {
  const json: JsonFields = {};

  if (rules.requireSub) {
    json.require_sub = true;
  }

  if (rules.minStandingLevel !== null) {
    json.min_standing_level = rules.minStandingLevel;
  }

  if (rules.minWatchMinutes !== null) {
    json.min_watch_minutes = rules.minWatchMinutes;
  }

  if (rules.minAccountAgeDays !== null) {
    json.min_account_age_days = rules.minAccountAgeDays;
  }

  return Object.keys(json).length === 0
    ? null
    : JSON.stringify(json);
}

export function weightingText(tickets: Weighting): string | null {
  const unweighted = Object.values(tickets).every((count): boolean => count === 1);

  return unweighted
    ? null
    : JSON.stringify({
        sub_t1: tickets.subT1,
        sub_t2: tickets.subT2,
        sub_t3: tickets.subT3,
        vip: tickets.vip,
      });
}

export type SaveGiveaway = { existing: Giveaway | null; draft: GiveawayDraft };

export const giveawaysKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'giveaways',
];

export const giveawayWinnersKey = (
  channelId: string | null,
  giveawayId: string | null,
): KeyParts => ['channels', channelId, 'giveaways', giveawayId, 'winners'];

async function readGiveaways(): Promise<Giveaway[]> {
  const { data, error, response } = await client.GET('/api/v{version}/giveaways', {
    params: { path: { version }, query: { PageSize: pageSize } },
  });

  if (error !== undefined)
    throw failed('giveaways', response.status, error);

  return [...(data?.data ?? [])];
}

async function readWinners(giveawayId: string): Promise<GiveawayWinner[]> {
  const { data, error, response } = await client.GET('/api/v{version}/giveaways/{id}/winners', {
    params: { path: { version, id: giveawayId }, query: { PageSize: pageSize } },
  });

  if (error !== undefined)
    throw failed('giveaway winners', response.status, error);

  return [...(data?.data ?? [])];
}

// The channel's giveaways, as the server sends them.
export function useGiveaways(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<Giveaway[], Error> {
  return channelQuery(channelId, giveawaysKey, () =>
    readGiveaways());
}

async function readEntries(giveawayId: string): Promise<GiveawayEntry[]> {
  const { data, error, response } = await client.GET('/api/v{version}/giveaways/{id}/entries', {
    params: { path: { version, id: giveawayId }, query: { PageSize: pageSize } },
  });

  if (error !== undefined)
    throw failed('giveaway entries', response.status, error);

  return [...(data?.data ?? [])];
}

async function readCodePools(): Promise<CodePool[]> {
  const { data, error, response } = await client.GET('/api/v{version}/giveaways/code-pools', {
    params: { path: { version }, query: { PageSize: pageSize } },
  });

  if (error !== undefined)
    throw failed('code pools', response.status, error);

  return [...(data?.data ?? [])];
}

// The entries of one giveaway, newest first.
export function useGiveawayEntries(
  channelId: MaybeRefOrGetter<string | null>,
  giveawayId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<GiveawayEntry[], Error> {
  return channelQuery(
    channelId,
    (id) => [...giveawaysKey(id), toValue(giveawayId), 'entries'],
    () => readEntries(toValue(giveawayId) as string),
  );
}

// The channel's code pools, counts only.
export function useCodePools(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<CodePool[], Error> {
  return channelQuery(
    channelId,
    (id) => [...giveawaysKey(id), 'code-pools'],
    () => readCodePools(),
  );
}

type UpsertGiveaway = components['schemas']['UpsertGiveawayRequest'];

// The older pipeline id column still follows the target when the target is a pipeline.
function prizePipelineId(draft: GiveawayDraft): string | null {
  if (draft.prizeMode !== 'pipeline' || draft.prizeTarget?.kind !== 'pipeline')
    return null;

  return draft.prizeTarget.id;
}

// An edit keeps every setting the dialog does not show; a new giveaway starts from the server defaults.
export function upsertBody({ existing, draft }: SaveGiveaway) {
  const base: UpsertGiveaway = existing
    ? { ...existing }
    : {
        title: '',
        entryMode: 'keyword',
        maxEntriesPerUser: 1,
        winnerCount: 1,
        excludeModerators: false,
        prizeMode: 'announce',
        prizeFromPot: false,
        requires18Plus: false,
      };

  return {
    ...base,
    title: draft.title,
    entryMode: draft.entryMode,
    keyword: draft.entryMode === 'keyword'
      ? draft.keyword
      : null,
    entryCost: draft.entryCost,
    maxEntriesPerUser: draft.maxEntriesPerUser,
    eligibilityJson: eligibilityText(draft.eligibility),
    weightingJson: weightingText(draft.weighting),
    winnerCount: draft.winnerCount,
    excludeModerators: draft.excludeModerators,
    claimWindowMinutes: draft.claimWindowMinutes,
    prizeMode: draft.prizeMode,
    prizeCurrencyAmount: draft.prizeMode === 'currency'
      ? draft.prizeCurrencyAmount
      : null,
    prizeFromPot: draft.prizeMode === 'currency' && draft.prizeFromPot,
    prizePipelineId: prizePipelineId(draft),
    target: draft.prizeMode === 'pipeline' && draft.prizeTarget !== null
      ? targetToWire(draft.prizeTarget)
      : null,
    prizeCodePoolId: draft.prizeMode === 'code_pool'
      ? draft.prizeCodePoolId
      : null,
    requires18Plus: draft.requires18Plus,
    scheduledCloseAt: draft.scheduledCloseAt,
  } as const satisfies UpsertGiveaway;
}

async function writeGiveaway(save: SaveGiveaway): Promise<void> {
  const body = upsertBody(save);
  const result = save.existing
    ? await client.PUT('/api/v{version}/giveaways/{id}', {
        params: { path: { version, id: save.existing.id } },
        body,
      })
    : await client.POST('/api/v{version}/giveaways', { params: { path: { version } }, body });

  if (result.error !== undefined)
    throw failed('save giveaway', result.response.status, result.error);
}

export type NewCodePool = {
  name: string;
  description: string;
  codes: string[];
};

// The pool is made first; its codes go in after, so a pool without codes is still a pool.
async function writeCodePool(pool: NewCodePool): Promise<void> {
  const created = await client.POST('/api/v{version}/giveaways/code-pools', {
    params: { path: { version } },
    body: { name: pool.name, description: pool.description },
  });
  const poolId = created.data?.data?.id;

  if (created.error !== undefined || !poolId)
    throw failed('create code pool', created.response.status, created.error);

  if (pool.codes.length === 0) return;

  const added = await client.POST('/api/v{version}/giveaways/code-pools/{poolId}/codes', {
    params: { path: { version, poolId } },
    body: { codes: pool.codes.map((code) => ({ code })) },
  });

  if (added.error !== undefined)
    throw failed('add codes to code pool', added.response.status, added.error);
}

// Makes a code pool with its codes; the pools are read again after, even when only the codes failed.
export function useCreateCodePool(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation<void, Error, NewCodePool>({
    mutationFn: writeCodePool,
    onSettled: () => queries.invalidateQueries({ queryKey: giveawaysKey(toValue(channelId)) }),
  });
}

// Creates or edits a giveaway; the list is read again after.
export function useSaveGiveaway(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation<void, Error, SaveGiveaway>({
    mutationFn: writeGiveaway,
    onSuccess: () => queries.invalidateQueries({ queryKey: giveawaysKey(toValue(channelId)) }),
  });
}

export async function fetchGiveawayBlastRadius(id: string): Promise<GiveawayBlastRadius> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/giveaways/{id}/blast-radius',
    {
      params: { path: { version, id } },
    },
  );

  if (error !== undefined || !data?.data)
    throw failed('giveaway blast radius', response.status, error);

  return data.data;
}

// The blast radius is read again right before the delete: a giveaway whose impact cannot be read is not deleted.
export function useDeleteGiveaway(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation<void, Error, string>({
    mutationFn: async (id: string): Promise<void> => {
      await fetchGiveawayBlastRadius(id);
      const { error, response } = await client.DELETE('/api/v{version}/giveaways/{id}', {
        params: { path: { version, id } },
      });

      if (error !== undefined)
        throw failed('delete giveaway', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: giveawaysKey(toValue(channelId)) }),
  });
}

export async function fetchCodePoolBlastRadius(poolId: string): Promise<GiveawayBlastRadius> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/giveaways/code-pools/{poolId}/blast-radius',
    {
      params: { path: { version, poolId } },
    },
  );

  if (error !== undefined || !data?.data)
    throw failed('code pool blast radius', response.status, error);

  return data.data;
}

// The blast radius is read again right before the delete: a pool whose impact cannot be read is not deleted.
export function useDeleteCodePool(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation<void, Error, string>({
    mutationFn: async (poolId: string): Promise<void> => {
      await fetchCodePoolBlastRadius(poolId);
      const { error, response } = await client.DELETE(
        '/api/v{version}/giveaways/code-pools/{poolId}',
        {
          params: { path: { version, poolId } },
        },
      );

      if (error !== undefined)
        throw failed('delete code pool', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: giveawaysKey(toValue(channelId)) }),
  });
}

// The winners of one giveaway, redraws included.
export function useGiveawayWinners(
  channelId: MaybeRefOrGetter<string | null>,
  giveawayId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<GiveawayWinner[], Error> {
  return channelQuery(
    channelId,
    (id) => giveawayWinnersKey(id, toValue(giveawayId)),
    () => readWinners(toValue(giveawayId) as string),
  );
}

export type GiveawayAction = 'open' | 'close' | 'draw';
export type GiveawayChange = { id: string; action: GiveawayAction };

const nextStatus = {
  open: 'open',
  close: 'closed',
  draw: 'drawn',
} as const satisfies Record<GiveawayAction, string>;

export function withGiveawayChange(
  rows: Giveaway[] | undefined,
  { id, action }: GiveawayChange,
): Giveaway[] {
  return (rows ?? []).map((row) =>
    row.id === id
      ? ({ ...row, status: nextStatus[action] } as const satisfies Giveaway)
      : row,
  );
}

async function send({ id, action }: GiveawayChange): Promise<void> {
  const params = { path: { version, id } };
  const result = await post(action, params);

  if (result.error !== undefined)
    throw failed(`${action} giveaway`, result.response.status, result.error);
}

function post(action: GiveawayAction, params: { path: { version: string; id: string } }) {
  switch (action) {
    case 'open':
      return client.POST('/api/v{version}/giveaways/{id}/open', { params });
    case 'close':
      return client.POST('/api/v{version}/giveaways/{id}/close', { params });
    case 'draw':
      return client.POST('/api/v{version}/giveaways/{id}/draw', { params });
  }
}

// Opens, closes or draws a giveaway. The row shows the new status at once; a refusal puts it back and
// reaches the error inbox. The list is read again after, because the server decides the final state.
export function useGiveawayAction(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<Giveaway[], Error, GiveawayChange, number> {
  const queries = useQueryClient();

  return useOptimisticSave<Giveaway[], GiveawayChange>({
    describe: ({ id, action }: GiveawayChange) => ({
      key: `${action}Giveaway`,
      params: { id },
    }),
    queryKey: () => giveawaysKey(toValue(channelId)),
    mutationFn: async (change: GiveawayChange): Promise<Giveaway[]> => {
      await send(change);
      // A draw adds winners, so every giveaway query of the channel (list and winners) is read again.
      await queries.invalidateQueries({ queryKey: giveawaysKey(toValue(channelId)) });

      return readGiveaways();
    },
    apply: withGiveawayChange,
  });
}

// Draws a replacement for one winner (a winner who does not answer). The winners list is read again after.
export function useRedrawWinner(
  channelId: MaybeRefOrGetter<string | null>,
  giveawayId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<GiveawayWinner[], Error, string, number> {
  return useOptimisticSave<GiveawayWinner[], string>({
    describe: (winnerId) => ({ key: 'redrawWinner', params: { id: winnerId } }),
    queryKey: () => giveawayWinnersKey(toValue(channelId), toValue(giveawayId)),
    mutationFn: async (winnerId: string): Promise<GiveawayWinner[]> => {
      const id = toValue(giveawayId) as string;
      const { error, response } = await client.POST(
        '/api/v{version}/giveaways/{id}/winners/{winnerId}/redraw',
        {
          params: {
            path: {
              version,
              id,
              winnerId,
            },
          },
        },
      );

      if (error !== undefined)
        throw failed('redraw winner', response.status, error);

      return readWinners(id);
    },
    apply: (current) => [...(current ?? [])],
  });
}
