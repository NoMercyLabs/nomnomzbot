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

// What the code editor needs from the bot to edit one overlay widget: its files, save and compile,
// the typed `nnz` declarations, the sample events, the preview actions and the saved versions.

import type {
  CompileFeedback,
  PreviewActionRequest,
  PreviewActionResult,
  VersionsPage,
} from '@/composables/useEditorBridge';
import type { JsonObject } from '@/lib/json';
import { numberOrNull } from '@/lib/number';

import { refusal } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

export type WidgetProject = components['schemas']['Project'];

const version = '1';
const versionsPageSize = 25;

export async function fetchWidgetProject(
  channelId: string,
  widgetId: string,
): Promise<WidgetProject> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/widgets/{widgetId}/project',
    { params: { path: {
      version,
      channelId,
      widgetId,
    } } },
  );

  if (error !== undefined || !data?.data)
    throw refusal('widget project', error, response.status);

  return data.data;
}

// The bot rebuilds the project itself; a broken build comes back as its reason and nothing is saved.
export async function saveWidgetProject(
  channelId: string,
  widgetId: string,
  project: WidgetProject,
): Promise<CompileFeedback> {
  const { error, response } = await client.PUT(
    '/api/v{version}/channels/{channelId}/widgets/{widgetId}/project',
    {
      params: { path: {
        version,
        channelId,
        widgetId,
      } },
      body: project,
    },
  );

  if (error === undefined)
    return { ok: true, message: '' };

  return { ok: false, message: refusal('save widget project', error, response.status).message };
}

// The widget's own `nnz` declarations; its settings are typed from its schema. Null when they cannot load.
export async function fetchWidgetSdkTypes(widgetId: string): Promise<string | null> {
  const { data, response } = await client.GET('/api/v{version}/sdk/types.d.ts', {
    params: { path: { version }, query: { context: 'widget', widget: widgetId } },
    parseAs: 'text',
  });

  return response.ok && typeof data === 'string'
    ? data
    : null;
}

// The sample payload of every event type; the editor's fire bar sends from this table.
export async function fetchWidgetSamples(channelId: string): Promise<JsonObject> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/widgets/test-event/samples',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined)
    throw refusal('widget samples', error, response.status);

  // The document types the table's values as unknown; they are the JSON sample payloads.
  return (data?.data ?? {}) as JsonObject;
}

export async function runWidgetPreviewAction(
  channelId: string,
  request: PreviewActionRequest,
): Promise<PreviewActionResult> {
  const { data, error, response } = await client.POST(
    '/api/v{version}/channels/{channelId}/widgets/preview-action',
    {
      params: { path: { version, channelId } },
      body: {
        actionType: request.actionType,
        parameters: request.params,
        variables: request.variables,
      },
    },
  );

  if (error !== undefined || !data?.data)
    throw refusal('widget preview action', error, response.status);

  const answer = data.data;
  const variables = answer.variables;

  return {
    success: answer.success,
    output: answer.output ?? null,
    error: answer.error ?? null,
    errorCode: answer.errorCode ?? null,
    variables,
  };
}

// One page of saved versions, newest first; `currentVersionId` marks the one the overlay serves.
export async function fetchWidgetVersions(
  channelId: string,
  widgetId: string,
  currentVersionId: string | null,
  page: number,
): Promise<VersionsPage> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/widgets/{widgetId}/versions',
    {
      params: {
        path: {
          version,
          channelId,
          widgetId,
        },
        query: { Page: page, PageSize: versionsPageSize },
      },
    },
  );

  if (error !== undefined)
    throw refusal('widget versions', error, response.status);

  return {
    versions: (data?.data ?? []).map((entry) => ({
      id: entry.id,
      version: numberOrNull(entry.versionNumber) ?? 0,
      validationStatus: entry.buildStatus,
      isCurrent: entry.id === currentVersionId,
    })),
    hasMore: data?.hasMore ?? false,
  };
}

export async function rollbackWidget(
  channelId: string,
  widgetId: string,
  versionId: string,
): Promise<void> {
  const { error, response } = await client.POST(
    '/api/v{version}/channels/{channelId}/widgets/{widgetId}/rollback/{versionId}',
    { params: { path: {
      version,
      channelId,
      widgetId,
      versionId,
    } } },
  );

  if (error !== undefined)
    throw refusal('rollback widget', error, response.status);
}
