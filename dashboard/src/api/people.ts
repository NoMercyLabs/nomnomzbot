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
  useInfiniteQuery,
  useMutation,
  useQuery,
  useQueryClient,
  type InfiniteData,
  type QueryClient,
  type UseInfiniteQueryReturnType,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'

import { useOptimisticSave } from '@/composables/useOptimisticSave'

import { memberKey, type CommunityRow } from './community'
import { client } from './client'
import type { components } from './schema'

export type { CommunityRow }
export type UserNote = components['schemas']['UserNoteDto']
// The profile without the trust and heat scores: the People page never shows them (viewer pages carry no bully data).
export type PersonProfile = Omit<components['schemas']['ViewerProfileSummaryDto'], 'trust'>

export type CommunityPage = {
  rows: CommunityRow[]
  hasMore: boolean
  nextPage: number | null
  total: number | null
}

const version = '1'
const pageSize = 50
const firstPage = 1

// Prefix keys let one invalidation reach every role and search of a channel (the realtime layer does that).
export const communityListsKey = (channelId: string | null): unknown[] => [
  'community',
  channelId,
  'list',
]
export const communityListKey = (
  channelId: string | null,
  role: string,
  search: string,
): unknown[] => [...communityListsKey(channelId), role, search]
export const viewerProfilesKey = (channelId: string | null): unknown[] => [
  'community',
  channelId,
  'profile',
]
export const viewerProfileKey = (channelId: string | null, userId: string | null): unknown[] => [
  ...viewerProfilesKey(channelId),
  userId,
]
export const userNotesKey = (channelId: string | null, userId: string | null): unknown[] => [
  'community',
  channelId,
  'notes',
  userId,
]

function failed(what: string, status: number): Error {
  return new Error(`${what} ${status}`)
}

function numberOrNull(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined)
    return null

  const parsed = Number(value)

  return Number.isFinite(parsed)
    ? parsed
    : null
}

async function readPage(
  channelId: string,
  role: string,
  search: string,
  page: number,
): Promise<CommunityPage> {
  const text = search.trim()
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/community',
    {
      params: {
        path: { version, channelId },
        query: {
          Page: page,
          PageSize: pageSize,
          ...(role === 'all'
            ? {}
            : { role }),
          ...(text === ''
            ? {}
            : { Search: text }),
        },
      },
    },
  )

  // A refusal is an error state, not an empty community.
  if (error !== undefined)
    throw failed('community list', response.status)

  return {
    rows: data?.data ?? [],
    hasMore: data?.hasMore ?? false,
    nextPage: numberOrNull(data?.nextPage),
    total: numberOrNull(data?.total),
  }
}

// One tab of the People list: role is "all", "moderator" or "vip". Pages are appended while the bot says there are more.
export function useCommunityList(
  channelId: MaybeRefOrGetter<string | null>,
  role: MaybeRefOrGetter<string>,
  search: MaybeRefOrGetter<string>,
): UseInfiniteQueryReturnType<InfiniteData<CommunityPage, number>, Error> {
  return useInfiniteQuery({
    queryKey: computed(() => communityListKey(toValue(channelId), toValue(role), toValue(search))),
    queryFn: ({ pageParam }: { pageParam: number }): Promise<CommunityPage> =>
      readPage(toValue(channelId) as string, toValue(role), toValue(search), pageParam),
    initialPageParam: firstPage,
    getNextPageParam: (last: CommunityPage, all: CommunityPage[]): number | undefined =>
      last.hasMore
        ? (last.nextPage ?? all.length + 1)
        : undefined,
    enabled: computed(() => toValue(channelId) !== null),
  })
}

async function readProfile(channelId: string, userId: string): Promise<PersonProfile | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/community/{userId}/profile',
    {
      params: { path: { version, channelId, userId } },
    },
  )

  if (error !== undefined)
    throw failed('viewer profile', response.status)

  const summary = data?.data

  if (!summary)
    return null

  const { trust, ...profile } = summary

  void trust

  return profile
}

// The id is the internal Guid (CommunityRow.internalUserId). A person without one has no profile to read.
export function useViewerProfile(
  channelId: MaybeRefOrGetter<string | null>,
  internalUserId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<PersonProfile | null, Error> {
  return useQuery({
    queryKey: computed(() => viewerProfileKey(toValue(channelId), toValue(internalUserId))),
    queryFn: () => readProfile(toValue(channelId) as string, toValue(internalUserId) as string),
    enabled: computed(() => toValue(channelId) !== null && toValue(internalUserId) !== null),
  })
}

async function readNotes(channelId: string, userId: string): Promise<UserNote[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/moderation/users/{userId}/notes',
    {
      params: { path: { version, channelId, userId } },
    },
  )

  if (error !== undefined)
    throw failed('user notes', response.status)

  return data?.data ?? []
}

// The id is the Twitch user id.
export function useUserNotes(
  channelId: MaybeRefOrGetter<string | null>,
  userId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<UserNote[], Error> {
  return useQuery({
    queryKey: computed(() => userNotesKey(toValue(channelId), toValue(userId))),
    queryFn: () => readNotes(toValue(channelId) as string, toValue(userId) as string),
    enabled: computed(() => toValue(channelId) !== null && toValue(userId) !== null),
  })
}

export type NoteInput = {
  userId: string
  content: string
  pinned: boolean
}

// The note shows at once; a refused save takes it away again and reaches the error inbox.
export function useAddNote(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<UserNote[], Error, NoteInput, number> {
  return useOptimisticSave<UserNote[], NoteInput>({
    queryKey: ({ userId }: NoteInput) => userNotesKey(toValue(channelId), userId),
    mutationFn: async ({ userId, content, pinned }: NoteInput): Promise<UserNote[]> => {
      const id = toValue(channelId) as string
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/moderation/users/{userId}/notes',
        {
          params: { path: { version, channelId: id, userId } },
          body: { content, pinned },
        },
      )

      if (error !== undefined)
        throw failed('add note', response.status)

      return readNotes(id, userId)
    },
    apply: (
      current: UserNote[] | undefined,
      { userId, content, pinned }: NoteInput,
    ): UserNote[] => {
      const now = new Date().toISOString()
      const pending = {
        id: -Date.now(),
        subjectUserId: userId,
        content,
        pinned,
        authorName: null,
        createdAt: now,
        updatedAt: now,
      } as const satisfies UserNote

      return [pending, ...(current ?? [])]
    },
  })
}

// A moderation action changes the person's record, profile and place in every list.
function personChanged(queries: QueryClient, channelId: string, userId: string): void {
  void queries.invalidateQueries({ queryKey: memberKey(channelId, userId) })
  void queries.invalidateQueries({ queryKey: viewerProfilesKey(channelId) })
  void queries.invalidateQueries({ queryKey: communityListsKey(channelId) })
}

export type ShoutoutInput = { targetTwitchUserId: string }

// Resolves to the path the bot took ("helix" or a chat line), so the page can say how the shoutout went out.
export function useShoutout(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<string, Error, ShoutoutInput, unknown> {
  return useMutation({
    mutationFn: async (input: ShoutoutInput): Promise<string> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/moderation/shoutout',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: input,
        },
      )

      if (error !== undefined)
        throw failed('shoutout', response.status)

      return data?.data?.path ?? ''
    },
  })
}

export type TimeoutInput = {
  userId: string
  durationSeconds: number
  reason?: string
}

export function useTimeout(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, TimeoutInput, unknown> {
  const queries = useQueryClient()

  return useMutation({
    mutationFn: async ({ userId, durationSeconds, reason }: TimeoutInput): Promise<void> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/moderation/actions',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: {
            action: 'timeout',
            targetUserId: userId,
            durationSeconds,
            ...(reason === undefined
              ? {}
              : { reason }),
          },
        },
      )

      if (error !== undefined)
        throw failed('timeout', response.status)

      // The bot can answer 200 and still refuse; its message says why.
      if (data?.data?.success === false)
        throw new Error(data.data.message ?? 'timeout refused')
    },
    onSuccess: (_data: void, { userId }: TimeoutInput) =>
      personChanged(queries, toValue(channelId) as string, userId),
  })
}

export type PersonInput = { userId: string }

// Lifts a timeout (or a ban) in this channel.
export function useUnban(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, PersonInput, unknown> {
  const queries = useQueryClient()

  return useMutation({
    mutationFn: async ({ userId }: PersonInput): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/moderation/actions/unban',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { targetTwitchUserId: userId, scope: 'this_channel' },
        },
      )

      if (error !== undefined)
        throw failed('unban', response.status)
    },
    onSuccess: (_data: void, { userId }: PersonInput) =>
      personChanged(queries, toValue(channelId) as string, userId),
  })
}

// Undo for a ban made through useBanMember: the same member path with DELETE.
export function useLiftBan(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, PersonInput, unknown> {
  const queries = useQueryClient()

  return useMutation({
    mutationFn: async ({ userId }: PersonInput): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/community/{userId}/ban',
        {
          params: { path: { version, channelId: toValue(channelId) as string, userId } },
        },
      )

      if (error !== undefined)
        throw failed('lift ban', response.status)
    },
    onSuccess: (_data: void, { userId }: PersonInput) =>
      personChanged(queries, toValue(channelId) as string, userId),
  })
}
