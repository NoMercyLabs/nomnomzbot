// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useQuery, type UseQueryReturnType } from '@tanstack/vue-query'

import type { components } from './schema'
import { client } from './client'

export type CodeScriptSummary = components['schemas']['CodeScriptSummaryDto']
export type ScriptProject = components['schemas']['ProjectDto']

// The channel's scripts, first page. The bot reads the channel from the signed-in session, not from the path.
// A refusal (custom code switched off, no right) fails with the bot's reason; it is not an empty list.
export async function fetchCodeScripts(): Promise<CodeScriptSummary[]> {
  const { data, error, response } = await client.GET('/api/v{version}/code-scripts', {
    params: { path: { version: '1' }, query: { PageSize: 100 } },
  })
  if (error !== undefined) {
    const reason: string | null | undefined = (error as { message?: string | null }).message
    throw new Error(reason ?? `code scripts ${response.status}`)
  }
  return [...(data?.data ?? [])]
}

export function useCodeScripts(): UseQueryReturnType<CodeScriptSummary[], Error> {
  return useQuery({ queryKey: ['code-scripts'], queryFn: fetchCodeScripts })
}

export async function fetchScriptProject(id: string): Promise<ScriptProject | null> {
  const { data } = await client.GET('/api/v{version}/code-scripts/{id}/project', {
    params: { path: { version: '1', id } },
  })
  return data?.data ?? null
}

// Validates, compiles and publishes the whole project. The bot's reason for a refusal comes back as the message.
export async function saveScriptProject(
  id: string,
  project: ScriptProject,
): Promise<{ ok: boolean; message: string }> {
  const { response, error } = await client.PUT('/api/v{version}/code-scripts/{id}/project', {
    params: { path: { version: '1', id } },
    body: project,
  })
  const reason: string | null | undefined = (error as { message?: string | null } | undefined)?.message
  return { ok: response.ok, message: reason ?? response.statusText }
}

// The typed `nnz` declarations that make the editor complete and check the script. Null when they cannot load.
export async function fetchScriptSdkTypes(id: string): Promise<string | null> {
  const { data, response } = await client.GET('/api/v{version}/sdk/types.d.ts', {
    params: { path: { version: '1' }, query: { context: 'script', script: id } },
    parseAs: 'text',
  })
  return response.ok && typeof data === 'string' ? data : null
}
