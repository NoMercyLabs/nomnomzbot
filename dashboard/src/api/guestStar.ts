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

import { useMutation, useQuery, useQueryClient, type UseQueryReturnType } from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelQuery, failed } from './channelQuery';
import { client } from './client';
import { ApiError } from './errors';
import type { components } from './schema';

// Twitch Guest Star for the channel: the settings, the running session, its invites and its slots.
// Twitch holds all of it; the channel's own token reads and changes it.
export type GuestStarSettings = components['schemas']['TwitchGuestStarChannelSettings'];
export type GuestStarSettingsChange = components['schemas']['UpdateGuestStarSettingsRequest'];
export type GuestStarSession = components['schemas']['TwitchGuestStarSession'];
export type GuestStarGuest = components['schemas']['TwitchGuestStarGuest'];
export type GuestStarInvite = components['schemas']['TwitchGuestStarInvite'];
export type GuestStarSlotChange = components['schemas']['UpdateSlotSettingsRequest'];

const version = '1';

// The reasons the bot gives for a refusal, in the `code` of the error body.
export const guestStarReasons = ['missing_scope', 'no_token', 'not_found', 'rate_limited'] as const;
export type GuestStarReason = (typeof guestStarReasons)[number] | 'other';

export function guestStarReason(error: Error): GuestStarReason {
  const code = error instanceof ApiError
    ? error.serverCode
    : undefined;

  return guestStarReasons.find((reason) => reason === code) ?? 'other';
}

export const guestStarKey = (channelId: string | null): KeyParts => ['channels', channelId, 'guest-star'];

const settingsKey = (channelId: string | null): KeyParts => ['channels', channelId, 'guest-star', 'settings'];
const sessionKey = (channelId: string | null): KeyParts => ['channels', channelId, 'guest-star', 'session'];
const invitesKey = (channelId: string | null, sessionId: string | null): KeyParts =>
  ['channels', channelId, 'guest-star', 'invites', sessionId];

async function readSettings(channelId: string): Promise<GuestStarSettings> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/guest-star/settings',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined || data?.data == null)
    throw failed('guest star settings', response.status, error);

  return data.data;
}

// Twitch answers 404 while no session runs; that is a state of the page, not a failure.
async function readSession(channelId: string): Promise<GuestStarSession | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/guest-star/session',
    { params: { path: { version, channelId } } },
  );

  if (response.status === 404)
    return null;

  if (error !== undefined)
    throw failed('guest star session', response.status, error);

  return data?.data ?? null;
}

async function readInvites(channelId: string, sessionId: string): Promise<GuestStarInvite[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/guest-star/sessions/{sessionId}/invites',
    { params: { path: {
      version,
      channelId,
      sessionId,
    } } },
  );

  if (error !== undefined)
    throw failed('guest star invites', response.status, error);

  return [...(data?.data ?? [])];
}

export function useGuestStarSettings(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<GuestStarSettings, Error> {
  return channelQuery(channelId, settingsKey, readSettings);
}

export function useGuestStarSession(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<GuestStarSession | null, Error> {
  return channelQuery(channelId, sessionKey, readSession);
}

export function useGuestStarInvites(
  channelId: MaybeRefOrGetter<string | null>,
  sessionId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<GuestStarInvite[], Error> {
  return useQuery(computed(() => {
    const channel = toValue(channelId);
    const session = toValue(sessionId);

    return {
      queryKey: invitesKey(channel, session),
      queryFn: () => readInvites(channel as string, session as string),
      enabled: channel !== null && session !== null,
    };
  }));
}

// Every change reads the whole page again, so each control shows what Twitch now holds.
function useGuestStarMutation<Input>(
  channelId: MaybeRefOrGetter<string | null>,
  send: (channel: string, input: Input) => Promise<void>,
) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: (input: Input) => send(toValue(channelId) as string, input),
    onSettled: () => queries.invalidateQueries({ queryKey: guestStarKey(toValue(channelId)) }),
  });
}

export function useSaveGuestStarSettings(channelId: MaybeRefOrGetter<string | null>) {
  return useGuestStarMutation(channelId, async (channel, body: GuestStarSettingsChange) => {
    const { error, response } = await client.PUT(
      '/api/v{version}/channels/{channelId}/guest-star/settings',
      { params: { path: { version, channelId: channel } }, body },
    );

    if (error !== undefined)
      throw failed('save guest star settings', response.status, error);
  });
}

export function useStartGuestStarSession(channelId: MaybeRefOrGetter<string | null>) {
  return useGuestStarMutation(channelId, async (channel, _input: void) => {
    const { error, response } = await client.POST(
      '/api/v{version}/channels/{channelId}/guest-star/session',
      { params: { path: { version, channelId: channel } } },
    );

    if (error !== undefined)
      throw failed('start guest star session', response.status, error);
  });
}

export function useEndGuestStarSession(channelId: MaybeRefOrGetter<string | null>) {
  return useGuestStarMutation(channelId, async (channel, sessionId: string) => {
    const { error, response } = await client.DELETE(
      '/api/v{version}/channels/{channelId}/guest-star/sessions/{sessionId}',
      { params: { path: {
        version,
        channelId: channel,
        sessionId,
      } } },
    );

    if (error !== undefined)
      throw failed('end guest star session', response.status, error);
  });
}

export function useInviteGuestStarGuest(channelId: MaybeRefOrGetter<string | null>) {
  return useGuestStarMutation(
    channelId,
    async (channel, input: { sessionId: string; guestTwitchUserId: string }) => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/guest-star/sessions/{sessionId}/invites',
        {
          params: { path: {
            version,
            channelId: channel,
            sessionId: input.sessionId,
          } },
          body: { guestTwitchUserId: input.guestTwitchUserId },
        },
      );

      if (error !== undefined)
        throw failed('invite guest star guest', response.status, error);
    },
  );
}

export function useRemoveGuestStarInvite(channelId: MaybeRefOrGetter<string | null>) {
  return useGuestStarMutation(
    channelId,
    async (channel, input: { sessionId: string; guestTwitchUserId: string }) => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/guest-star/sessions/{sessionId}/invites/{guestTwitchUserId}',
        {
          params: {
            path: {
              version,
              channelId: channel,
              sessionId: input.sessionId,
              guestTwitchUserId: input.guestTwitchUserId,
            },
          },
        },
      );

      if (error !== undefined)
        throw failed('remove guest star invite', response.status, error);
    },
  );
}

export function useAssignGuestStarSlot(channelId: MaybeRefOrGetter<string | null>) {
  return useGuestStarMutation(
    channelId,
    async (channel, input: {
      sessionId: string;
      guestTwitchUserId: string;
      slotId: string;
    }) => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/guest-star/sessions/{sessionId}/slots',
        {
          params: { path: {
            version,
            channelId: channel,
            sessionId: input.sessionId,
          } },
          body: { guestTwitchUserId: input.guestTwitchUserId, slotId: input.slotId },
        },
      );

      if (error !== undefined)
        throw failed('assign guest star slot', response.status, error);
    },
  );
}

// No destination swaps the guest with the empty interface.
export function useMoveGuestStarSlot(channelId: MaybeRefOrGetter<string | null>) {
  return useGuestStarMutation(
    channelId,
    async (channel, input: {
      sessionId: string;
      slotId: string;
      destinationSlotId: string | null;
    }) => {
      const { error, response } = await client.PATCH(
        '/api/v{version}/channels/{channelId}/guest-star/sessions/{sessionId}/slots/{slotId}',
        {
          params: {
            path: {
              version,
              channelId: channel,
              sessionId: input.sessionId,
              slotId: input.slotId,
            },
          },
          body: { destinationSlotId: input.destinationSlotId },
        },
      );

      if (error !== undefined)
        throw failed('move guest star slot', response.status, error);
    },
  );
}

export function useRemoveGuestStarSlot(channelId: MaybeRefOrGetter<string | null>) {
  return useGuestStarMutation(
    channelId,
    async (
      channel,
      input: {
        sessionId: string;
        slotId: string;
        guestTwitchUserId: string;
        shouldReinviteGuest: boolean;
      },
    ) => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/guest-star/sessions/{sessionId}/slots/{slotId}',
        {
          params: {
            path: {
              version,
              channelId: channel,
              sessionId: input.sessionId,
              slotId: input.slotId,
            },
            query: {
              guestTwitchUserId: input.guestTwitchUserId,
              shouldReinviteGuest: input.shouldReinviteGuest,
            },
          },
        },
      );

      if (error !== undefined)
        throw failed('remove guest star slot', response.status, error);
    },
  );
}

export function useSaveGuestStarSlotSettings(channelId: MaybeRefOrGetter<string | null>) {
  return useGuestStarMutation(
    channelId,
    async (channel, input: {
      sessionId: string;
      slotId: string;
      change: GuestStarSlotChange;
    }) => {
      const { error, response } = await client.PATCH(
        '/api/v{version}/channels/{channelId}/guest-star/sessions/{sessionId}/slots/{slotId}/settings',
        {
          params: {
            path: {
              version,
              channelId: channel,
              sessionId: input.sessionId,
              slotId: input.slotId,
            },
          },
          body: input.change,
        },
      );

      if (error !== undefined)
        throw failed('save guest star slot settings', response.status, error);
    },
  );
}
