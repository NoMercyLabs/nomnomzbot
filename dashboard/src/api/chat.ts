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
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'

import type { ChatMessagePayload } from '@/realtime/payloads'

import { client } from './client'

// The live feed of one channel, oldest line first. The hub appends to it; the page seeds it from the history call.
export const chatKey = (channelId: string | null): unknown[] => ['channels', channelId, 'chat']

export const chatLineLimit = 500

// Why a line is no longer shown as normal: one message deleted, a person's lines purged, or the whole chat cleared.
export type ChatLineRemoval = 'deleted' | 'purged' | 'cleared'

export type ChatLine = ChatMessagePayload & { removed?: ChatLineRemoval }

export const chatMessageMaxLength = 500

export type ChatScope = 'this_channel' | 'all_moderated'

export type SendChatInput = {
  message: string
  senderIdentity?: string
  replyToMessageId?: string | null
}
export type TimeoutInput = { targetUserId: string; durationSeconds: number; reason?: string | null }
export type BanInput = { targetUserId: string; scope: ChatScope; reason?: string | null }
export type UnbanInput = { targetUserId: string; scope: ChatScope }

function ensureOk(response: Response, what: string): void {
  if (!response.ok) throw new Error(`${what} ${response.status}`)
}

// Lines that arrived live while the history call ran stay; the history wins on a shared id.
function mergeHistory(history: ChatLine[], live: ChatLine[] | undefined): ChatLine[] {
  const known: Set<string> = new Set(history.map((line) => line.id))
  const fresh: ChatLine[] = (live ?? []).filter((line) => !known.has(line.id))

  return [...history, ...fresh].slice(-chatLineLimit)
}

export function useChatHistory(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ChatLine[], Error> {
  const queries = useQueryClient()

  return useQuery({
    queryKey: computed(() => chatKey(toValue(channelId))),
    queryFn: async (): Promise<ChatLine[]> => {
      const id = toValue(channelId) as string
      const { data } = await client.GET('/api/v{version}/channels/{channelId}/chat/messages', {
        params: { path: { version: '1', channelId: id }, query: { limit: chatLineLimit } },
      })

      return mergeHistory(data?.data ?? [], queries.getQueryData<ChatLine[]>(chatKey(id)))
    },
    enabled: computed(() => toValue(channelId) !== null),
    staleTime: Infinity,
  })
}

export function useSendChat(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, SendChatInput, unknown> {
  return useMutation({
    mutationFn: async (input: SendChatInput): Promise<void> => {
      if (input.message.length > chatMessageMaxLength) throw new Error('chat message too long')
      const { response } = await client.POST('/api/v{version}/channels/{channelId}/chat/messages', {
        params: { path: { version: '1', channelId: toValue(channelId) as string } },
        body: {
          message: input.message,
          senderIdentity: input.senderIdentity ?? 'you',
          replyToMessageId: input.replyToMessageId ?? null,
        },
      })

      ensureOk(response, 'send')
    },
  })
}

export function useDeleteChatMessage(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, unknown> {
  return useMutation({
    mutationFn: async (messageId: string): Promise<void> => {
      const { response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/chat/messages/{messageId}',
        {
          params: { path: { version: '1', channelId: toValue(channelId) as string, messageId } },
        },
      )

      ensureOk(response, 'delete')
    },
  })
}

export function useTimeoutUser(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, TimeoutInput, unknown> {
  return useMutation({
    mutationFn: async (input: TimeoutInput): Promise<void> => {
      const { response } = await client.POST(
        '/api/v{version}/channels/{channelId}/moderation/actions',
        {
          params: { path: { version: '1', channelId: toValue(channelId) as string } },
          body: {
            action: 'timeout',
            targetUserId: input.targetUserId,
            durationSeconds: input.durationSeconds,
            reason: input.reason ?? null,
          },
        },
      )

      ensureOk(response, 'timeout')
    },
  })
}

export function useBanUser(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, BanInput, unknown> {
  return useMutation({
    mutationFn: async (input: BanInput): Promise<void> => {
      const { response } = await client.POST(
        '/api/v{version}/channels/{channelId}/moderation/actions/ban',
        {
          params: { path: { version: '1', channelId: toValue(channelId) as string } },
          body: {
            targetTwitchUserId: input.targetUserId,
            scope: input.scope,
            reason: input.reason ?? null,
          },
        },
      )

      ensureOk(response, 'ban')
    },
  })
}

export function useUnbanUser(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, UnbanInput, unknown> {
  return useMutation({
    mutationFn: async (input: UnbanInput): Promise<void> => {
      const { response } = await client.POST(
        '/api/v{version}/channels/{channelId}/moderation/actions/unban',
        {
          params: { path: { version: '1', channelId: toValue(channelId) as string } },
          body: { targetTwitchUserId: input.targetUserId, scope: input.scope },
        },
      )

      ensureOk(response, 'unban')
    },
  })
}
