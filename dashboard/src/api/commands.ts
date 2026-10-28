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

import {
  useMutation,
  useQueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import { useOptimisticSave } from '@/composables/useOptimisticSave';

import { channelOptions, channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type CommandListItem = components['schemas']['CommandListItem'];
export type CommandBlastRadius = components['schemas']['BlastRadius'];
export type CommandChange = components['schemas']['UpdateCommand'];
export type NewCommand = components['schemas']['CreateCommand'];
export type BuiltinCommand = components['schemas']['BuiltinCommand'];
export type CommandPreset = components['schemas']['CommandPreset'];

// A built-in's name is its chat trigger with the "!" (`!sr`); the dashboard shows the bare word under its own "!".
export function triggerWord(command: BuiltinCommand): string {
  return command.name.replace(/^!+/, '');
}

const version = '1';
const pageSize = 100;
// A guard against a server that never says "no more": 100 pages of 100 is far beyond any channel.
const maxPages = 100;

// The realtime layer writes into these entries, so the keys are part of the module's surface.
export const commandsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'commands',
];

export const presetsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'command-presets',
];

export const builtinsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'builtins',
];

async function readCommands(channelId: string): Promise<CommandListItem[]> {
  const rows: CommandListItem[] = [];

  for (let page = 1; page <= maxPages; page += 1) {
    const { data, error, response } = await client.GET(
      '/api/v{version}/channels/{channelId}/commands',
      {
        params: { path: { version, channelId }, query: { Page: page, PageSize: pageSize } },
      },
    );

    if (error !== undefined)
      throw failed('commands', response.status, error);

    rows.push(...(data?.data ?? []));

    if (data?.hasMore !== true) break;
  }

  return rows;
}

async function readBuiltins(channelId: string): Promise<BuiltinCommand[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/builtins',
    {
      params: { path: { version, channelId } },
    },
  );

  if (error !== undefined)
    throw failed('built-in commands', response.status, error);

  return [...(data?.data ?? [])];
}

async function readPresets(channelId: string): Promise<CommandPreset[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/command-presets',
    {
      params: { path: { version, channelId } },
    },
  );

  if (error !== undefined)
    throw failed('command presets', response.status, error);

  return [...(data?.data ?? [])];
}

export function useCommandPresets(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<CommandPreset[], Error> {
  return channelQuery(channelId, presetsKey, readPresets);
}

export function useCommands(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<CommandListItem[], Error> {
  return channelQuery(channelId, commandsKey, readCommands);
}

export function useBuiltins(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<BuiltinCommand[], Error> {
  return channelQuery(channelId, builtinsKey, readBuiltins);
}

export const commandsOptions = (id: string) => channelOptions(commandsKey, readCommands, id);
export const builtinsOptions = (id: string) => channelOptions(builtinsKey, readBuiltins, id);

type RowsMutation<TVariables> = UseMutationReturnType<CommandListItem[], Error, TVariables, number>;

// Null and missing fields mean "unchanged" on the wire, so the cached row takes only the fields that are set.
function applyChange(row: CommandListItem, change: CommandChange) {
  const set = Object.fromEntries(
    Object.entries(change).filter(([, value]) => value !== undefined && value !== null),
  ) as Partial<CommandListItem>;

  return { ...row, ...set } as const satisfies CommandListItem;
}

async function putCommand(
  channelId: string,
  commandName: string,
  change: CommandChange,
): Promise<CommandListItem[]> {
  const { error, response } = await client.PUT(
    '/api/v{version}/channels/{channelId}/commands/{commandName}',
    {
      params: {
        path: {
          version,
          channelId,
          commandName,
        },
      },
      body: change,
    },
  );

  if (error !== undefined)
    throw refusal('save command', error, response.status);

  return readCommands(channelId);
}

function saveKey(change: CommandChange): 'turnOnCommand' | 'turnOffCommand' | 'updateCommand' {
  if (change.isEnabled === true)
    return 'turnOnCommand';

  if (change.isEnabled === false)
    return 'turnOffCommand';

  return 'updateCommand';
}

function saveRow<TVariables>(
  channelId: MaybeRefOrGetter<string | null>,
  send: (variables: TVariables) => { commandName: string; change: CommandChange },
): RowsMutation<TVariables> {
  return useOptimisticSave<CommandListItem[], TVariables>({
    describe: (variables) => {
      const { commandName, change } = send(variables);

      return { key: saveKey(change), params: { name: commandName } };
    },
    queryKey: () => commandsKey(toValue(channelId)),
    mutationFn: (variables: TVariables): Promise<CommandListItem[]> => {
      const { commandName, change } = send(variables);

      return putCommand(toValue(channelId) as string, commandName, change);
    },
    apply: (current: CommandListItem[] | undefined, variables: TVariables): CommandListItem[] => {
      const { commandName, change } = send(variables);

      return (current ?? []).map((row) =>
        row.name === commandName
          ? applyChange(row, change)
          : row,
      );
    },
  });
}

// Only the changed fields travel in the PUT. A refusal keeps the bot's reason on the mutation error.
export function useUpdateCommand(
  channelId: MaybeRefOrGetter<string | null>,
): RowsMutation<{ commandName: string; change: CommandChange }> {
  return saveRow<{ commandName: string; change: CommandChange }>(
    channelId,
    (variables) => variables,
  );
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
  );
}

// The row leaves the list at once; a refused delete puts it back and reaches the error inbox.
// What still runs the command. The bot refuses a delete while anything does, so this is read first.
export async function fetchCommandBlastRadius(
  channelId: string,
  commandName: string,
): Promise<CommandBlastRadius> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/commands/{commandName}/blast-radius',
    { params: { path: {
      version,
      channelId,
      commandName,
    } } },
  );

  if (error !== undefined || !data?.data)
    throw refusal('command blast radius', error, response.status);

  return data.data;
}

export function useDeleteCommand(
  channelId: MaybeRefOrGetter<string | null>,
): RowsMutation<{ commandName: string }> {
  return useOptimisticSave<CommandListItem[], { commandName: string }>({
    describe: ({ commandName }) => ({ key: 'deleteCommand', params: { name: commandName } }),
    queryKey: () => commandsKey(toValue(channelId)),
    mutationFn: async ({ commandName }: { commandName: string }): Promise<CommandListItem[]> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/commands/{commandName}',
        {
          params: {
            path: {
              version,
              channelId: id,
              commandName,
            },
          },
        },
      );

      if (error !== undefined)
        throw refusal('delete command', error, response.status);

      return readCommands(id);
    },
    apply: (
      current: CommandListItem[] | undefined,
      { commandName }: { commandName: string },
    ): CommandListItem[] => (current ?? []).filter((row) => row.name !== commandName),
  });
}

// A new command is not shown before the bot accepts it: the name can be taken and the quota can be full.
export function useCreateCommand(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation<void, Error, NewCommand>({
    mutationFn: async (command: NewCommand): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/commands',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: command,
        },
      );

      if (error !== undefined)
        throw refusal('create command', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: commandsKey(toValue(channelId)) });
    },
  });
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
    describe: ({ builtinKey, enabled }) => ({
      key: enabled
        ? 'turnOnBuiltin'
        : 'turnOffBuiltin',
      params: { name: builtinKey },
    }),
    queryKey: () => builtinsKey(toValue(channelId)),
    mutationFn: async ({
      builtinKey,
      enabled,
    }: {
      builtinKey: string;
      enabled: boolean;
    }): Promise<BuiltinCommand[]> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.PATCH(
        '/api/v{version}/channels/{channelId}/builtins/{builtinKey}',
        {
          params: {
            path: {
              version,
              channelId: id,
              builtinKey,
            },
          },
          body: { enabled },
        },
      );

      if (error !== undefined)
        throw refusal('built-in command', error, response.status);

      return readBuiltins(id);
    },
    apply: (
      current: BuiltinCommand[] | undefined,
      { builtinKey, enabled }: { builtinKey: string; enabled: boolean },
    ): BuiltinCommand[] =>
      (current ?? []).map((row) =>
        row.builtinKey === builtinKey
          ? { ...row, isEnabled: enabled }
          : row,
      ),
  });
}

export type BuiltinReplyGroup = components['schemas']['BuiltinReplyGroup'];
export type BuiltinReply = components['schemas']['BuiltinReply'];

export const builtinRepliesKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'builtin-replies',
];

async function readBuiltinReplies(channelId: string): Promise<BuiltinReplyGroup[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/builtins/replies',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined)
    throw failed('built-in commands', response.status, error);

  return [...(data?.data ?? [])];
}

export function useBuiltinReplies(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<BuiltinReplyGroup[], Error> {
  return channelQuery(channelId, builtinRepliesKey, readBuiltinReplies);
}

// A null field means "the default applies"; the server replaces all three at once, so every save names all three.
export type BuiltinSettingsChange = {
  builtinKey: string;
  cooldownSeconds: number | null;
  userCooldownSeconds: number | null;
  minPermissionLevel: string | null;
};

export function useUpdateBuiltinSettings(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<BuiltinCommand[], Error, BuiltinSettingsChange, number> {
  return useOptimisticSave<BuiltinCommand[], BuiltinSettingsChange>({
    describe: ({ builtinKey }) => ({ key: 'changeBuiltinSettings', params: { name: builtinKey } }),
    queryKey: () => builtinsKey(toValue(channelId)),
    mutationFn: async ({
      builtinKey,
      ...body
    }: BuiltinSettingsChange): Promise<BuiltinCommand[]> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/builtins/{builtinKey}/settings',
        {
          params: {
            path: {
              version,
              channelId: id,
              builtinKey,
            },
          },
          body,
        },
      );

      if (error !== undefined)
        throw refusal('built-in command', error, response.status);

      return readBuiltins(id);
    },
    apply: (
      current: BuiltinCommand[] | undefined,
      change: BuiltinSettingsChange,
    ): BuiltinCommand[] =>
      (current ?? []).map((row) =>
        row.builtinKey === change.builtinKey
          ? {
              ...row,
              cooldownSecondsOverride: change.cooldownSeconds,
              userCooldownSecondsOverride: change.userCooldownSeconds,
              minPermissionLevelOverride: change.minPermissionLevel,
            }
          : row,
      ),
  });
}

export type BuiltinReplyChange = {
  builtinKey: string;
  slot: string;
  // null = put the slot back to the default (DELETE); text = the channel's own wording (PUT).
  template: string | null;
};

export function useSetBuiltinReply(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<BuiltinReplyGroup[], Error, BuiltinReplyChange, number> {
  return useOptimisticSave<BuiltinReplyGroup[], BuiltinReplyChange>({
    describe: ({ builtinKey, slot, template }) => ({
      key: template === null
        ? 'resetBuiltinReply'
        : 'changeBuiltinReply',
      params: { name: builtinKey, slot },
    }),
    queryKey: () => builtinRepliesKey(toValue(channelId)),
    mutationFn: async ({
      builtinKey,
      slot,
      template,
    }: BuiltinReplyChange): Promise<BuiltinReplyGroup[]> => {
      const id = toValue(channelId) as string;
      const path = {
        version,
        channelId: id,
        builtinKey,
        slot,
      };
      const url = '/api/v{version}/channels/{channelId}/builtins/{builtinKey}/replies/{slot}';
      const { error, response } =
        template === null
          ? await client.DELETE(url, { params: { path } })
          : await client.PUT(url, { params: { path }, body: { template } });

      if (error !== undefined)
        throw refusal('built-in command', error, response.status);

      return readBuiltinReplies(id);
    },
    apply: (
      current: BuiltinReplyGroup[] | undefined,
      { builtinKey, slot, template }: BuiltinReplyChange,
    ): BuiltinReplyGroup[] =>
      (current ?? []).map((group) => ({
        ...group,
        replies: group.replies.map((reply) =>
          reply.builtinKey === builtinKey && reply.slot === slot
            ? {
                ...reply,
                effectiveTemplate: template ?? reply.defaultTemplate,
                isOverridden: template !== null,
              }
            : reply,
        ),
      })),
  });
}

export type BuiltinTtsChange = { builtinKey: string; enabled: boolean };

export function useSetBuiltinTts(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<BuiltinCommand[], Error, BuiltinTtsChange, number> {
  return useOptimisticSave<BuiltinCommand[], BuiltinTtsChange>({
    describe: ({ builtinKey }) => ({ key: 'changeBuiltinTts', params: { name: builtinKey } }),
    queryKey: () => builtinsKey(toValue(channelId)),
    mutationFn: async ({ builtinKey, enabled }): Promise<BuiltinCommand[]> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/builtins/{builtinKey}/tts',
        {
          params: {
            path: {
              version,
              channelId: id,
              builtinKey,
            },
          },
          body: { enabled },
        },
      );

      if (error !== undefined)
        throw refusal('built-in command', error, response.status);

      return readBuiltins(id);
    },
    apply: (current: BuiltinCommand[] | undefined, { builtinKey, enabled }): BuiltinCommand[] =>
      (current ?? []).map((row) =>
        row.builtinKey === builtinKey
          ? { ...row, speakWithTts: enabled }
          : row,
      ),
  });
}

// A reset writes the preset back over the command, so the list is read again instead of guessed.
export function useResetCommandToPreset(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation<void, Error, { commandName: string }>({
    mutationFn: async ({ commandName }: { commandName: string }): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/commands/{commandName}/reset-to-preset',
        {
          params: { path: {
            version,
            channelId: toValue(channelId) as string,
            commandName,
          } },
        },
      );

      if (error !== undefined)
        throw refusal('reset command', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: commandsKey(toValue(channelId)) });
    },
  });
}

// A reset puts the built-in back to its defaults: on, quiet, default cooldowns, role and wording.
export function useResetBuiltin(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation<void, Error, { builtinKey: string }>({
    mutationFn: async ({ builtinKey }: { builtinKey: string }): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/builtins/{builtinKey}/settings',
        {
          params: { path: {
            version,
            channelId: toValue(channelId) as string,
            builtinKey,
          } },
        },
      );

      if (error !== undefined)
        throw refusal('reset command', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: builtinsKey(toValue(channelId)) });
      await queries.invalidateQueries({ queryKey: builtinRepliesKey(toValue(channelId)) });
    },
  });
}
