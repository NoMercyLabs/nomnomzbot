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
  useQueryClient,
  type UseQueryReturnType,
} from '@tanstack/vue-query';

import type { Ref } from 'vue';

import { client } from './client';
import { problemError } from './errors';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type ActionDefault = components['schemas']['ActionDefault'];
export type PlatformDefaultBlastRadius = components['schemas']['PlatformDefaultBlastRadius'];
export type SetActionDefaultRequest = components['schemas']['SetActionDefaultRequest'];

const version = '1';

// Every platform-defaults query lives under this prefix, so one invalidation (a save here, or a live
// event handler later) refreshes every section of the page.
export const adminPlatformDefaultsKey: KeyParts = ['admin', 'platform-defaults'];

export const adminActionDefaultsKey: KeyParts = [...adminPlatformDefaultsKey, 'actions'];

// The default role each action asks for, with the shipped value, the platform's own value and the floor.
export function useAdminActionDefaults(): UseQueryReturnType<ActionDefault[], Error> {
  return useQuery({
    queryKey: adminActionDefaultsKey,
    queryFn: async (): Promise<ActionDefault[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/platform-defaults/actions',
        { params: { path: { version } } },
      );

      if (error !== undefined)
        throw problemError('platform defaults actions', response.status, error);

      return [...(data?.data ?? [])];
    },
  });
}

// The counted blast radius of moving an action's default to a level; no level previews the shipped default.
export async function previewActionDefault(
  actionKey: string,
  level: number | null,
): Promise<PlatformDefaultBlastRadius> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/admin/platform-defaults/actions/{actionKey}/blast-radius',
    {
      params: {
        path: { version, actionKey },
        query: level === null
          ? {}
          : { level },
      },
    },
  );

  if (error !== undefined)
    throw problemError('platform defaults actions', response.status, error);

  if (!data?.data)
    throw problemError('platform defaults actions', response.status, { message: 'empty' });

  return data.data;
}

export type SaveActionDefault = { actionKey: string } & SetActionDefaultRequest;

// A saved default changes the list and every count behind it, so the whole page prefix is refetched.
export function useSaveActionDefault() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ actionKey, ...body }: SaveActionDefault): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/admin/platform-defaults/actions/{actionKey}',
        { params: { path: { version, actionKey } }, body },
      );

      if (error !== undefined)
        throw problemError('platform defaults actions', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: adminPlatformDefaultsKey }),
  });
}

export type BuiltinReplyDefault = components['schemas']['BuiltinReplyDefault'];
export type EventResponseDefault = components['schemas']['EventResponseDefault'];
export type EventResponseDefaultChange = components['schemas']['EventResponseDefaultChange'];

export const adminBuiltinReplyDefaultsKey: KeyParts = [
  ...adminPlatformDefaultsKey,
  'builtin-replies',
];

export const adminEventResponseDefaultsKey: KeyParts = [
  ...adminPlatformDefaultsKey,
  'event-responses',
];

// The wording of every built-in reply slot: the shipped text, the platform's own text and who has their own.
export function useAdminBuiltinReplyDefaults(): UseQueryReturnType<BuiltinReplyDefault[], Error> {
  return useQuery({
    queryKey: adminBuiltinReplyDefaultsKey,
    queryFn: async (): Promise<BuiltinReplyDefault[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/platform-defaults/builtin-replies',
        { params: { path: { version } } },
      );

      if (error !== undefined)
        throw problemError('platform defaults replies', response.status, error);

      return [...(data?.data ?? [])];
    },
  });
}

// The counted blast radius of a new wording; a null template previews the shipped wording.
export async function previewBuiltinReplyDefault(
  builtinKey: string,
  slot: string,
  template: string | null,
): Promise<PlatformDefaultBlastRadius> {
  const { data, error, response } = await client.POST(
    '/api/v{version}/admin/platform-defaults/builtin-replies/{builtinKey}/{slot}/blast-radius',
    { params: { path: {
      version,
      builtinKey,
      slot,
    } }, body: { template } },
  );

  if (error !== undefined)
    throw problemError('platform defaults replies', response.status, error);

  if (!data?.data)
    throw problemError('platform defaults replies', response.status, { message: 'empty' });

  return data.data;
}

export type SaveBuiltinReplyDefault = {
  builtinKey: string;
  slot: string;
  template: string | null;
  confirmedChannelsAffected: number;
};

export function useSaveBuiltinReplyDefault() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ builtinKey, slot, ...body }: SaveBuiltinReplyDefault): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/admin/platform-defaults/builtin-replies/{builtinKey}/{slot}',
        { params: { path: {
          version,
          builtinKey,
          slot,
        } }, body },
      );

      if (error !== undefined)
        throw problemError('platform defaults replies', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: adminPlatformDefaultsKey }),
  });
}

// The platform's response to every event type, with the variables it may use and who follows it.
export function useAdminEventResponseDefaults(): UseQueryReturnType<EventResponseDefault[], Error> {
  return useQuery({
    queryKey: adminEventResponseDefaultsKey,
    queryFn: async (): Promise<EventResponseDefault[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/platform-defaults/event-responses',
        { params: { path: { version } } },
      );

      if (error !== undefined)
        throw problemError('platform defaults events', response.status, error);

      return [...(data?.data ?? [])];
    },
  });
}

export async function previewEventResponseDefault(
  eventType: string,
  change: EventResponseDefaultChange,
): Promise<PlatformDefaultBlastRadius> {
  const { data, error, response } = await client.POST(
    '/api/v{version}/admin/platform-defaults/event-responses/{eventType}/blast-radius',
    { params: { path: { version, eventType } }, body: change },
  );

  if (error !== undefined)
    throw problemError('platform defaults events', response.status, error);

  if (!data?.data)
    throw problemError('platform defaults events', response.status, { message: 'empty' });

  return data.data;
}

export type SaveEventResponseDefault = {
  eventType: string;
  confirmedChannelsAffected: number;
} & EventResponseDefaultChange;

export function useSaveEventResponseDefault() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ eventType, ...body }: SaveEventResponseDefault): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/admin/platform-defaults/event-responses/{eventType}',
        { params: { path: { version, eventType } }, body },
      );

      if (error !== undefined)
        throw problemError('platform defaults events', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: adminPlatformDefaultsKey }),
  });
}

export type TtsVoiceDefault = components['schemas']['TtsVoiceDefault'];
export type TtsVoiceCandidate = components['schemas']['TtsVoiceCandidate'];

export const adminTtsVoiceDefaultKey: KeyParts = [...adminPlatformDefaultsKey, 'tts-voice'];

export const adminTtsVoiceCandidatesKey: KeyParts = [...adminTtsVoiceDefaultKey, 'candidates'];

// The voice every channel without its own pick speaks with, and how many channels follow it.
export function useAdminTtsVoiceDefault(): UseQueryReturnType<TtsVoiceDefault | undefined, Error> {
  return useQuery({
    queryKey: adminTtsVoiceDefaultKey,
    queryFn: async (): Promise<TtsVoiceDefault | undefined> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/platform-defaults/tts-voice',
        { params: { path: { version } } },
      );

      if (error !== undefined)
        throw problemError('platform defaults voice', response.status, error);

      return data?.data ?? undefined;
    },
  });
}

// Every voice the platform could make its default; read when the editor opens.
export function useAdminTtsVoiceCandidates(
  enabled: Ref<boolean>,
): UseQueryReturnType<TtsVoiceCandidate[], Error> {
  return useQuery({
    queryKey: adminTtsVoiceCandidatesKey,
    enabled,
    queryFn: async (): Promise<TtsVoiceCandidate[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/platform-defaults/tts-voice/candidates',
        { params: { path: { version } } },
      );

      if (error !== undefined)
        throw problemError('platform defaults voice', response.status, error);

      return [...(data?.data ?? [])];
    },
  });
}

// The counted blast radius of making a voice the platform default.
export async function previewTtsVoiceDefault(voiceId: string): Promise<PlatformDefaultBlastRadius> {
  const { data, error, response } = await client.POST(
    '/api/v{version}/admin/platform-defaults/tts-voice/blast-radius',
    { params: { path: { version } }, body: { voiceId } },
  );

  if (error !== undefined)
    throw problemError('platform defaults voice', response.status, error);

  if (!data?.data)
    throw problemError('platform defaults voice', response.status, { message: 'empty' });

  return data.data;
}

export type SaveTtsVoiceDefault = components['schemas']['SetTtsVoiceDefaultRequest'];

export function useSaveTtsVoiceDefault() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (body: SaveTtsVoiceDefault): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/admin/platform-defaults/tts-voice',
        { params: { path: { version } }, body },
      );

      if (error !== undefined)
        throw problemError('platform defaults voice', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: adminPlatformDefaultsKey }),
  });
}
