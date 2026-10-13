// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { type UseMutationReturnType, type UseQueryReturnType } from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import { useOptimisticSave } from '@/composables/useOptimisticSave';

import { channelQuery, failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

export type Poll = components['schemas']['TwitchPoll'];
export type Prediction = components['schemas']['TwitchPrediction'];
export type Timer = components['schemas']['TimerListItem'];

const version = '1';
const timerPageSize = 100;

export const timersKey = (channelId: string | null): unknown[] => ['channels', channelId, 'timers'];

export const pollsKey = (channelId: string | null): unknown[] => [
  'channels',
  channelId,
  'live-ops-polls',
];
export const predictionsKey = (channelId: string | null): unknown[] => [
  'channels',
  channelId,
  'live-ops-predictions',
];

async function readPolls(channelId: string): Promise<Poll[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/live-ops/polls',
    {
      params: { path: { version, channelId } },
    },
  );

  if (error !== undefined)
    throw failed('polls', response.status);

  return [...(data?.data ?? [])];
}

// The channel's polls, active and ended. Use isRunning to pick the one that still takes votes.
export function usePolls(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<Poll[], Error> {
  return channelQuery(channelId, pollsKey, readPolls);
}

// The channel's predictions, open, locked and ended.
export function usePredictions(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<Prediction[], Error> {
  return channelQuery(channelId, predictionsKey, readPredictions);
}

async function readTimers(channelId: string): Promise<Timer[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/timers',
    {
      params: { path: { version, channelId }, query: { PageSize: timerPageSize } },
    },
  );

  if (error !== undefined)
    throw failed('timers', response.status);

  return [...(data?.data ?? [])];
}

// The channel's timers. Use isEnabled to pick the ones that fire.
export function useTimers(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<Timer[], Error> {
  return channelQuery(channelId, timersKey, readTimers);
}

// Pauses an enabled timer or resumes a paused one. A paused timer stays saved and does not fire.
// The variable is the timer id; the list shows the new state at once and takes the server's list after.
export function useToggleTimer(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<Timer[], Error, string, number> {
  return useOptimisticSave<Timer[], string>({
    queryKey: () => timersKey(toValue(channelId)),
    mutationFn: async (timerId: string): Promise<Timer[]> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/timers/{id}/toggle',
        {
          params: {
            path: {
              version,
              channelId: id,
              id: timerId,
            },
          },
        },
      );

      if (error !== undefined)
        throw failed('timer-toggle', response.status);

      return readTimers(id);
    },
    apply: (current: Timer[] | undefined, timerId: string): Timer[] =>
      (current ?? []).map((timer: Timer) =>
        timer.id === timerId
          ? { ...timer, isEnabled: !timer.isEnabled }
          : timer,
      ),
  });
}

export type NewPoll = components['schemas']['CreatePollDto'];

// A poll is running while Twitch reports it ACTIVE.
export const isPollRunning = (poll: Poll): boolean => poll.status.toUpperCase() === 'ACTIVE';

// Starts a poll on Twitch. The list shows the new poll at once, with no votes, and takes the server's list after.
export function useStartPoll(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<Poll[], Error, NewPoll, number> {
  return useOptimisticSave<Poll[], NewPoll>({
    queryKey: () => pollsKey(toValue(channelId)),
    mutationFn: async (poll: NewPoll): Promise<Poll[]> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/live-ops/polls',
        {
          params: { path: { version, channelId: id } },
          body: poll,
        },
      );

      if (error !== undefined)
        throw failed('poll-start', response.status);

      return readPolls(id);
    },
    apply: (current: Poll[] | undefined, poll: NewPoll): Poll[] => [
      provisionalPoll(poll),
      ...(current ?? []),
    ],
  });
}

function provisionalPoll(poll: NewPoll): Poll {
  return {
    id: 'pending',
    broadcasterId: '',
    broadcasterName: '',
    broadcasterLogin: '',
    title: poll.title,
    choices: poll.choices.map((title: string, index: number) => ({
      id: `pending-${index}`,
      title,
      votes: 0,
      channelPointsVotes: 0,
      bitsVotes: 0,
    })),
    bitsVotingEnabled: false,
    bitsPerVote: 0,
    channelPointsVotingEnabled: poll.channelPointsVotingEnabled,
    channelPointsPerVote: poll.channelPointsPerVote,
    status: 'ACTIVE',
    duration: poll.durationSeconds,
    startedAt: new Date().toISOString(),
    endedAt: null,
  };
}

export type NewPrediction = components['schemas']['CreatePredictionDto'];

export type PredictionResolution = {
  predictionId: string;
  winningOutcomeId: string;
};

// A prediction is running while Twitch reports it ACTIVE (taking points) or LOCKED (waiting for the result).
export const isPredictionRunning = (prediction: Prediction): boolean =>
  ['ACTIVE', 'LOCKED'].includes(prediction.status.toUpperCase());

function provisionalPrediction(prediction: NewPrediction): Prediction {
  return {
    id: 'pending',
    broadcasterId: '',
    broadcasterName: '',
    broadcasterLogin: '',
    title: prediction.title,
    winningOutcomeId: null,
    outcomes: prediction.outcomes.map((title: string, index: number) => ({
      id: `pending-${index}`,
      title,
      users: 0,
      channelPoints: 0,
      topPredictors: [],
      color: '',
    })),
    predictionWindow: prediction.predictionWindowSeconds,
    status: 'ACTIVE',
    createdAt: new Date().toISOString(),
    endedAt: null,
    lockedAt: null,
  };
}

async function readPredictions(channelId: string): Promise<Prediction[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/live-ops/predictions',
    {
      params: { path: { version, channelId } },
    },
  );

  if (error !== undefined)
    throw failed('predictions', response.status);

  return [...(data?.data ?? [])];
}

// Starts a prediction on Twitch. The list shows it at once, with no points, and takes the server's list after.
export function useStartPrediction(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<Prediction[], Error, NewPrediction, number> {
  return useOptimisticSave<Prediction[], NewPrediction>({
    queryKey: () => predictionsKey(toValue(channelId)),
    mutationFn: async (prediction: NewPrediction): Promise<Prediction[]> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/live-ops/predictions',
        {
          params: { path: { version, channelId: id } },
          body: prediction,
        },
      );

      if (error !== undefined)
        throw failed('prediction-start', response.status);

      return readPredictions(id);
    },
    apply: (current: Prediction[] | undefined, prediction: NewPrediction): Prediction[] => [
      provisionalPrediction(prediction),
      ...(current ?? []),
    ],
  });
}

async function endPrediction(
  channelId: string,
  predictionId: string,
  body: components['schemas']['EndPredictionDto'],
): Promise<Prediction[]> {
  const { error, response } = await client.PATCH(
    '/api/v{version}/channels/{channelId}/live-ops/predictions/{predictionId}/end',
    {
      params: { path: {
        version,
        channelId,
        predictionId,
      } },
      body,
    },
  );

  if (error !== undefined)
    throw failed('prediction-end', response.status);

  return readPredictions(channelId);
}

function changePrediction(
  predictions: Prediction[] | undefined,
  predictionId: string,
  change: Partial<Prediction>,
): Prediction[] {
  return (predictions ?? []).map((prediction: Prediction) =>
    prediction.id === predictionId
      ? { ...prediction, ...change }
      : prediction,
  );
}

// Locks an open prediction: viewers can no longer put points on it. The variable is the prediction id.
export function useLockPrediction(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<Prediction[], Error, string, number> {
  return useOptimisticSave<Prediction[], string>({
    queryKey: () => predictionsKey(toValue(channelId)),
    mutationFn: (predictionId: string): Promise<Prediction[]> =>
      endPrediction(toValue(channelId) as string, predictionId, { status: 'LOCKED' }),
    apply: (current: Prediction[] | undefined, predictionId: string): Prediction[] =>
      changePrediction(current, predictionId, {
        status: 'LOCKED',
        lockedAt: new Date().toISOString(),
      }),
  });
}

// Resolves a prediction: the points of the winning outcome's predictors pay out on Twitch.
export function useResolvePrediction(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<Prediction[], Error, PredictionResolution, number> {
  return useOptimisticSave<Prediction[], PredictionResolution>({
    queryKey: () => predictionsKey(toValue(channelId)),
    mutationFn: (resolution: PredictionResolution): Promise<Prediction[]> =>
      endPrediction(toValue(channelId) as string, resolution.predictionId, {
        status: 'RESOLVED',
        winningOutcomeId: resolution.winningOutcomeId,
      }),
    apply: (current: Prediction[] | undefined, resolution: PredictionResolution): Prediction[] =>
      changePrediction(current, resolution.predictionId, {
        status: 'RESOLVED',
        winningOutcomeId: resolution.winningOutcomeId,
      }),
  });
}

// Cancels a prediction: Twitch gives every viewer their points back. The variable is the prediction id.
export function useCancelPrediction(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<Prediction[], Error, string, number> {
  return useOptimisticSave<Prediction[], string>({
    queryKey: () => predictionsKey(toValue(channelId)),
    mutationFn: (predictionId: string): Promise<Prediction[]> =>
      endPrediction(toValue(channelId) as string, predictionId, { status: 'CANCELED' }),
    apply: (current: Prediction[] | undefined, predictionId: string): Prediction[] =>
      changePrediction(current, predictionId, { status: 'CANCELED' }),
  });
}

// Ends a running poll early and shows its result to viewers on Twitch. The variable is the poll id.
export function useEndPoll(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<Poll[], Error, string, number> {
  return useOptimisticSave<Poll[], string>({
    queryKey: () => pollsKey(toValue(channelId)),
    mutationFn: async (pollId: string): Promise<Poll[]> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.PATCH(
        '/api/v{version}/channels/{channelId}/live-ops/polls/{pollId}/end',
        {
          params: { path: {
            version,
            channelId: id,
            pollId,
          } },
          body: { status: 'TERMINATED' },
        },
      );

      if (error !== undefined)
        throw failed('poll-end', response.status);

      return readPolls(id);
    },
    apply: (current: Poll[] | undefined, pollId: string): Poll[] =>
      (current ?? []).map((poll: Poll) =>
        poll.id === pollId
          ? { ...poll, status: 'TERMINATED' }
          : poll,
      ),
  });
}
