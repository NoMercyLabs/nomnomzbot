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
} from '@tanstack/vue-query';
import type { MaybeRefOrGetter } from 'vue';

import { useOptimisticSave } from '@/composables/useOptimisticSave';
import type {
  EditorOutcome,
  TestRunResult,
  TestTrigger,
  VersionsPage,
} from '@/composables/useEditorBridge';

import { channelQuery, refusal } from './channelQuery';
import type { components } from './schema';
import { client } from './client';

export type CodeScriptSummary = components['schemas']['CodeScriptSummaryDto'];
export type CodeScriptDetail = components['schemas']['CodeScriptDetailDto'];
export type ScriptProject = components['schemas']['ProjectDto'];
export type BlastRadius = components['schemas']['BlastRadiusDto'];
export type ScriptTestRequest = components['schemas']['ScriptTestRunRequest'];
export type NewCodeScript = components['schemas']['CreateCodeScriptRequest'];

const version = '1';
const versionsPageSize = 25;

export const codeScriptsKey: string[] = ['code-scripts'];
export const codeScriptKey = (id: string | null): unknown[] => ['code-scripts', 'detail', id];

// The channel's scripts, first page. The bot reads the channel from the signed-in session, not from the path.
// A refusal (custom code switched off, no right) fails with the bot's reason; it is not an empty list.
export async function fetchCodeScripts(): Promise<CodeScriptSummary[]> {
  const { data, error, response } = await client.GET('/api/v{version}/code-scripts', {
    params: { path: { version: '1' }, query: { PageSize: 100 } },
  });

  if (error !== undefined) {
    const reason = (error as { message?: string | null }).message;

    throw new Error(reason ?? `code scripts ${response.status}`);
  }

  return [...(data?.data ?? [])];
}

export function useCodeScripts(): UseQueryReturnType<CodeScriptSummary[], Error> {
  return useQuery({ queryKey: codeScriptsKey, queryFn: fetchCodeScripts });
}

async function readScript(id: string): Promise<CodeScriptDetail> {
  const { data, error, response } = await client.GET('/api/v{version}/code-scripts/{id}', {
    params: { path: { version, id } },
  });

  if (error !== undefined || !data?.data)
    throw refusal('code script', error, response.status);

  return data.data;
}

export function useScriptDetail(
  id: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<CodeScriptDetail, Error> {
  return channelQuery(id, codeScriptKey, readScript);
}

type EnabledChange = { id: string; isEnabled: boolean };

// The switch moves at once; a refusal puts it back and reaches the error inbox.
export function useSetScriptEnabled(): UseMutationReturnType<
  CodeScriptSummary[],
  Error,
  EnabledChange,
  number
> {
  const queries = useQueryClient();

  return useOptimisticSave<CodeScriptSummary[], EnabledChange>({
    queryKey: codeScriptsKey,
    mutationFn: async ({ id, isEnabled }: EnabledChange): Promise<CodeScriptSummary[]> => {
      const { error, response } = await client.PATCH('/api/v{version}/code-scripts/{id}/enabled', {
        params: { path: { version, id } },
        body: { isEnabled },
      });

      if (error !== undefined)
        throw refusal('code script switch', error, response.status);

      await queries.invalidateQueries({ queryKey: codeScriptKey(id) });

      return fetchCodeScripts();
    },
    apply: (
      current: CodeScriptSummary[] | undefined,
      { id, isEnabled }: EnabledChange,
    ): CodeScriptSummary[] =>
      (current ?? []).map((script: CodeScriptSummary): CodeScriptSummary =>
        script.id === id
          ? { ...script, isEnabled }
          : script,
      ),
  });
}

// Resolves to the new script's id, or null when the bot does not echo it.
export function useCreateCodeScript(): UseMutationReturnType<
  string | null,
  Error,
  NewCodeScript,
  unknown
> {
  const queries = useQueryClient();

  return useMutation<string | null, Error, NewCodeScript>({
    mutationFn: async (script: NewCodeScript): Promise<string | null> => {
      const { data, error, response } = await client.POST('/api/v{version}/code-scripts', {
        params: { path: { version } },
        body: script,
      });

      if (error !== undefined)
        throw refusal('create code script', error, response.status);

      const created: unknown = (data as { data?: unknown } | undefined)?.data;
      const id: unknown = (created as { id?: unknown } | null | undefined)?.id;

      return typeof id === 'string'
        ? id
        : null;
    },
    onSuccess: (): Promise<void> => queries.invalidateQueries({ queryKey: codeScriptsKey }),
  });
}

export async function fetchBlastRadius(id: string): Promise<BlastRadius> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/code-scripts/{id}/blast-radius',
    {
      params: { path: { version, id } },
    },
  );

  if (error !== undefined || !data?.data)
    throw refusal('blast radius', error, response.status);

  return data.data;
}

// The blast radius is read again right before the delete: a script whose impact cannot be read is not deleted.
export function useDeleteCodeScript(): UseMutationReturnType<void, Error, string, unknown> {
  const queries = useQueryClient();

  return useMutation<void, Error, string>({
    mutationFn: async (id: string): Promise<void> => {
      await fetchBlastRadius(id);
      const { error, response } = await client.DELETE('/api/v{version}/code-scripts/{id}', {
        params: { path: { version, id } },
      });

      if (error !== undefined)
        throw refusal('delete code script', error, response.status);
    },
    onSuccess: async (_none: void, id: string): Promise<void> => {
      queries.removeQueries({ queryKey: codeScriptKey(id) });
      await queries.invalidateQueries({ queryKey: codeScriptsKey });
    },
  });
}

export type TestRunOutcome =
  | { kind: 'ok'; result: TestRunResult } |
  {
    kind: 'rateLimited';
    message: string;
    retryAfterSeconds: number | null;
  };

function retryWindow(response: Response): number | null {
  const seconds = Number(response.headers.get('Retry-After'));

  return Number.isFinite(seconds) && seconds > 0
    ? seconds
    : null;
}

// The dry-run result in the shape the editor and the runs panel read.
export function toTestRunResult(dto: components['schemas']['TestRunResultDto']): TestRunResult {
  const position = dto.errorPosition;
  const message = dto.error ?? '';

  return {
    success: dto.success,
    durationMs: Number(dto.durationMs),
    hostCallCount: Number(dto.hostCallCount),
    error: dto.error,
    chatOutput: dto.chatOutput,
    variablesSet: dto.variablesSet,
    timeline: (dto.timeline ?? []).map((entry: components['schemas']['TimelineEntryDto']) => ({
      ...entry,
      seq: Number(entry.seq),
    })),
    console: dto.console,
    effects: dto.capturedEffects.map((effect: components['schemas']['CapturedEffectDto']) => ({
      name: effect.name,
      argsPreview: effect.argsPreview,
    })),
    ...(position
      ? {
          errors: [
            {
              message,
              ...(position.file
                ? { file: position.file }
                : {}),
              line: Number(position.line),
              column: Number(position.column),
            },
          ],
        }
      : {}),
  };
}

// A dry run: nothing it does reaches chat or the channel. Too many runs in a short time is an outcome, not an error.
export async function runScriptTest(
  id: string,
  request: ScriptTestRequest,
): Promise<TestRunOutcome> {
  const { data, error, response } = await client.POST(
    '/api/v{version}/code-scripts/{id}/test-run',
    {
      params: { path: { version, id } },
      body: request,
    },
  );

  if (response.status === 429) {
    const reason = (error as { message?: string | null } | undefined)?.message;

    return {
      kind: 'rateLimited',
      message: reason ?? `test run ${response.status}`,
      retryAfterSeconds: retryWindow(response),
    };
  }

  if (error !== undefined || !data?.data)
    throw refusal('test run', error, response.status);

  return { kind: 'ok', result: toTestRunResult(data.data) };
}

export function toEditorOutcome(outcome: TestRunOutcome): EditorOutcome<TestRunResult> {
  return outcome.kind === 'ok'
    ? { ok: true, value: outcome.result }
    : { ok: false, message: outcome.message };
}

export async function fetchTestTriggers(): Promise<TestTrigger[]> {
  const { data, error, response } = await client.GET('/api/v{version}/code-scripts/test-triggers', {
    params: { path: { version } },
  });

  if (error !== undefined)
    throw refusal('test triggers', error, response.status);

  return (data?.data ?? []).map((sample: components['schemas']['TestTriggerDto']): TestTrigger => ({
    id: sample.id,
    label: sample.userDisplayName
      ? `${sample.responseKey} (${sample.userDisplayName})`
      : sample.responseKey,
    variables: sample.variables,
  }));
}

// One page of a script's saved versions; `currentVersionId` marks the published one.
export async function fetchScriptVersions(
  id: string,
  currentVersionId: string | null,
  page: number,
): Promise<VersionsPage> {
  const { data, error, response } = await client.GET('/api/v{version}/code-scripts/{id}/versions', {
    params: { path: { version, id }, query: { Page: page, PageSize: versionsPageSize } },
  });

  if (error !== undefined)
    throw refusal('script versions', error, response.status);

  return {
    versions: (data?.data ?? []).map((entry: components['schemas']['CodeScriptVersionDto']) => ({
      id: entry.id,
      version: Number(entry.version),
      validationStatus: entry.validationStatus,
      isCurrent: entry.id === currentVersionId,
    })),
    hasMore: data?.hasMore ?? false,
  };
}

export async function publishScriptVersion(id: string, versionId: string): Promise<void> {
  const { error, response } = await client.POST(
    '/api/v{version}/code-scripts/{id}/versions/{versionId}/publish',
    {
      params: {
        path: {
          version,
          id,
          versionId,
        },
      },
    },
  );

  if (error !== undefined)
    throw refusal('publish version', error, response.status);
}

// The published version cannot be deleted; the bot's reason says so.
export async function deleteScriptVersion(id: string, versionId: string): Promise<void> {
  const { error, response } = await client.DELETE(
    '/api/v{version}/code-scripts/{id}/versions/{versionId}',
    {
      params: {
        path: {
          version,
          id,
          versionId,
        },
      },
    },
  );

  if (error !== undefined)
    throw refusal('delete version', error, response.status);
}

export async function fetchScriptProject(id: string): Promise<ScriptProject | null> {
  const { data } = await client.GET('/api/v{version}/code-scripts/{id}/project', {
    params: { path: { version: '1', id } },
  });

  return data?.data ?? null;
}

// Validates, compiles and publishes the whole project. The bot's reason for a refusal comes back as the message.
export async function saveScriptProject(
  id: string,
  project: ScriptProject,
): Promise<{ ok: boolean; message: string }> {
  const { response, error } = await client.PUT('/api/v{version}/code-scripts/{id}/project', {
    params: { path: { version: '1', id } },
    body: project,
  });
  const reason = (error as { message?: string | null } | undefined)?.message;

  return { ok: response.ok, message: reason ?? response.statusText };
}

// The typed `nnz` declarations that make the editor complete and check the script. Null when they cannot load.
export async function fetchScriptSdkTypes(id: string): Promise<string | null> {
  const { data, response } = await client.GET('/api/v{version}/sdk/types.d.ts', {
    params: { path: { version: '1' }, query: { context: 'script', script: id } },
    parseAs: 'text',
  });

  return response.ok && typeof data === 'string'
    ? data
    : null;
}
