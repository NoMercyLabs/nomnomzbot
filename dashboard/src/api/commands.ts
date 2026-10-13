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
} from '@tanstack/vue-query'
import { toValue, type MaybeRefOrGetter } from 'vue'

import { useOptimisticSave } from '@/composables/useOptimisticSave'

import { channelQuery, failed, refusal } from './channelQuery'
import { client } from './client'
import type { components } from './schema'

export type CommandListItem = components['schemas']['CommandListItem']
export type CommandChange = components['schemas']['UpdateCommandDto']
export type NewCommand = components['schemas']['CreateCommandDto']
export type BuiltinCommand = components['schemas']['BuiltinCommandDto']

const version = '1'
const pageSize = 100
// A guard against a server that never says "no more": 100 pages of 100 is far beyond any channel.
const maxPages = 100

// The realtime layer writes into these entries, so the keys are part of the module's surface.
export const commandsKey = (channelId: string | null): unknown[] => [
  'channels',
  channelId,
  'commands',
]
export const builtinsKey = (channelId: string | null): unknown[] => [
  'channels',
  channelId,
  'builtins',
]

async function readCommands(channelId: string): Promise<CommandListItem[]> {
  const rows: CommandListItem[] = []

  for (let page = 1; page <= maxPages; page += 1) {
    const { data, error, response } = await client.GET(
      '/api/v{version}/channels/{channelId}/commands',
      {
        params: { path: { version, channelId }, query: { Page: page, PageSize: pageSize } },
      },
    )

    if (error !== undefined)
      throw failed('commands', response.status)

    rows.push(...(data?.data ?? []))

    if (data?.hasMore !== true) break
  }

  return rows
}

async function readBuiltins(channelId: string): Promise<BuiltinCommand[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/builtins',
    {
      params: { path: { version, channelId } },
    },
  )

  if (error !== undefined)
    throw failed('built-in commands', response.status)

  return [...(data?.data ?? [])]
}

export function useCommands(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<CommandListItem[], Error> {
  return channelQuery(channelId, commandsKey, readCommands)
}

export function useBuiltins(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<BuiltinCommand[], Error> {
  return channelQuery(channelId, builtinsKey, readBuiltins)
}

type RowsMutation<TVariables> = UseMutationReturnType<CommandListItem[], Error, TVariables, number>

// Null and missing fields mean "unchanged" on the wire, so the cached row takes only the fields that are set.
function applyChange(row: CommandListItem, change: CommandChange): CommandListItem {
  const set = Object.fromEntries(
    Object.entries(change).filter(([, value]) => value !== undefined && value !== null),
  ) as Partial<CommandListItem>

  return { ...row, ...set }
}

async function putCommand(
  channelId: string,
  commandName: string,
  change: CommandChange,
): Promise<CommandListItem[]> {
  const { error, response } = await client.PUT(
    '/api/v{version}/channels/{channelId}/commands/{commandName}',
    {
      params: { path: { version, channelId, commandName } },
      body: change,
    },
  )

  if (error !== undefined)
    throw refusal('save command', error, response.status)

  return readCommands(channelId)
}

function saveRow<TVariables>(
  channelId: MaybeRefOrGetter<string | null>,
  send: (variables: TVariables) => { commandName: string; change: CommandChange },
): RowsMutation<TVariables> {
  return useOptimisticSave<CommandListItem[], TVariables>({
    queryKey: () => commandsKey(toValue(channelId)),
    mutationFn: (variables: TVariables): Promise<CommandListItem[]> => {
      const { commandName, change } = send(variables)

      return putCommand(toValue(channelId) as string, commandName, change)
    },
    apply: (current: CommandListItem[] | undefined, variables: TVariables): CommandListItem[] => {
      const { commandName, change } = send(variables)

      return (current ?? []).map((row: CommandListItem) =>
        row.name === commandName
          ? applyChange(row, change)
          : row,
      )
    },
  })
}

// Only the changed fields travel in the PUT. A refusal keeps the bot's reason on the mutation error.
export function useUpdateCommand(
  channelId: MaybeRefOrGetter<string | null>,
): RowsMutation<{ commandName: string; change: CommandChange }> {
  return saveRow<{ commandName: string; change: CommandChange }>(
    channelId,
    (variables) => variables,
  )
}

export function useToggleCommand(
  channelId: MaybeRefOrGetter<string | null>,
): RowsMutation<{ commandName: string; isEnabled: boolean }> {
  return saveRow<{ commandName: string; isEnabled: boolean }>(
    channelId,
    ({ commandName, isEnabled }) => ({
      commandName,
      change: { isEnabled },
    }),
  )
}

// The row leaves the list at once; a refused delete puts it back and reaches the error inbox.
export function useDeleteCommand(
  channelId: MaybeRefOrGetter<string | null>,
): RowsMutation<{ commandName: string }> {
  return useOptimisticSave<CommandListItem[], { commandName: string }>({
    queryKey: () => commandsKey(toValue(channelId)),
    mutationFn: async ({ commandName }: { commandName: string }): Promise<CommandListItem[]> => {
      const id = toValue(channelId) as string
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/commands/{commandName}',
        {
          params: { path: { version, channelId: id, commandName } },
        },
      )

      if (error !== undefined)
        throw refusal('delete command', error, response.status)

      return readCommands(id)
    },
    apply: (
      current: CommandListItem[] | undefined,
      { commandName }: { commandName: string },
    ): CommandListItem[] =>
      (current ?? []).filter((row: CommandListItem) => row.name !== commandName),
  })
}

// A new command is not shown before the bot accepts it: the name can be taken and the quota can be full.
export function useCreateCommand(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, NewCommand, unknown> {
  const queries = useQueryClient()

  return useMutation<void, Error, NewCommand>({
    mutationFn: async (command: NewCommand): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/commands',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: command,
        },
      )

      if (error !== undefined)
        throw refusal('create command', error, response.status)
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: commandsKey(toValue(channelId)) })
    },
  })
}

export function useToggleBuiltin(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<
  BuiltinCommand[],
  Error,
  { builtinKey: string; enabled: boolean },
  number
> {
  return useOptimisticSave<BuiltinCommand[], { builtinKey: string; enabled: boolean }>({
    queryKey: () => builtinsKey(toValue(channelId)),
    mutationFn: async ({
      builtinKey,
      enabled,
    }: {
      builtinKey: string
      enabled: boolean
    }): Promise<BuiltinCommand[]> => {
      const id = toValue(channelId) as string
      const { error, response } = await client.PATCH(
        '/api/v{version}/channels/{channelId}/builtins/{builtinKey}',
        {
          params: { path: { version, channelId: id, builtinKey } },
          body: { enabled },
        },
      )

      if (error !== undefined)
        throw refusal('built-in command', error, response.status)

      return readBuiltins(id)
    },
    apply: (
      current: BuiltinCommand[] | undefined,
      { builtinKey, enabled }: { builtinKey: string; enabled: boolean },
    ): BuiltinCommand[] =>
      (current ?? []).map((row: BuiltinCommand) =>
        row.builtinKey === builtinKey
          ? { ...row, isEnabled: enabled }
          : row,
      ),
  })
}
