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

import type { KeyParts } from '@/lib/array';

import { channelQuery, failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

// The channel's sound clips: short audio files a chat word, a command or a pipeline can play on the overlay.
export type SoundClip = components['schemas']['SoundClip'];
export type SoundClipBlastRadius = components['schemas']['BlastRadius'];

// What the add dialog sends: the audio file and the first settings. The name is fixed once saved.
export type NewSoundClip = {
  file: File;
  name: string;
  displayName: string;
  defaultVolume: number;
};

// What an edit sends: every setting but the file and the name.
export type SoundClipChange = {
  displayName: string;
  defaultVolume: number;
  isEnabled: boolean;
  cooldownSeconds: number;
  minPermissionLevel: string;
  triggerWord: string | null;
};

export type SaveSoundClip = { id: string; change: SoundClipChange };

export type SoundClipPage = {
  clips: SoundClip[];
  total: number;
  hasMore: boolean;
};

const version = '1';

export const soundClipsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'sound-clips',
];

async function readSoundClips(limit: number): Promise<SoundClipPage> {
  const { data, error, response } = await client.GET('/api/v{version}/sound-clips', {
    params: { path: { version }, query: { Page: 1, PageSize: limit } },
  });

  if (error !== undefined)
    throw failed('sound clips', response.status, error);

  const clips = [...(data?.data ?? [])];

  return {
    clips,
    total: Number(data?.total ?? clips.length),
    hasMore: data?.hasMore === true,
  };
}

// The channel's sound clips, the first `limit` of them.
export function useSoundClips(
  channelId: MaybeRefOrGetter<string | null>,
  limit: MaybeRefOrGetter<number>,
): UseQueryReturnType<SoundClipPage, Error> {
  return channelQuery(
    channelId,
    (id) => [...soundClipsKey(id), toValue(limit)],
    () => readSoundClips(toValue(limit)),
  );
}

// The multipart body is built here: the generated type names the file part, the form carries the real file.
export function useUploadSoundClip(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, NewSoundClip, void> {
  const queries = useQueryClient();

  return useMutation<void, Error, NewSoundClip, void>({
    mutationFn: async (clip: NewSoundClip): Promise<void> => {
      const form = new FormData();

      form.append('Name', clip.name);
      form.append('DisplayName', clip.displayName);
      form.append('DefaultVolume', String(clip.defaultVolume));
      form.append('File', clip.file);

      const { error, response } = await client.POST('/api/v{version}/sound-clips', {
        params: { path: { version } },
        body: { Name: clip.name },
        bodySerializer: () => form,
      });

      if (error !== undefined)
        throw failed('upload sound clip', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: soundClipsKey(toValue(channelId)) }),
  });
}

// Edits the settings of one clip; the list is read again after.
export function useSaveSoundClip(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, SaveSoundClip, void> {
  const queries = useQueryClient();

  return useMutation<void, Error, SaveSoundClip, void>({
    mutationFn: async ({ id, change }: SaveSoundClip): Promise<void> => {
      const { error, response } = await client.PUT('/api/v{version}/sound-clips/{id}', {
        params: { path: { version, id } },
        body: change,
      });

      if (error !== undefined)
        throw failed('save sound clip', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: soundClipsKey(toValue(channelId)) }),
  });
}

// What else uses a clip: the counted places a delete would break.
export async function fetchSoundClipBlastRadius(id: string): Promise<SoundClipBlastRadius> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/sound-clips/{id}/blast-radius',
    { params: { path: { version, id } } },
  );

  if (error !== undefined || !data?.data)
    throw failed('sound clip blast radius', response.status, error);

  return data.data;
}

// The blast radius is read again right before the delete: a clip whose impact cannot be read is not deleted.
export function useDeleteSoundClip(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, void> {
  const queries = useQueryClient();

  return useMutation<void, Error, string, void>({
    mutationFn: async (id: string): Promise<void> => {
      await fetchSoundClipBlastRadius(id);

      const { error, response } = await client.DELETE('/api/v{version}/sound-clips/{id}', {
        params: { path: { version, id } },
      });

      if (error !== undefined)
        throw failed('delete sound clip', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: soundClipsKey(toValue(channelId)) }),
  });
}
