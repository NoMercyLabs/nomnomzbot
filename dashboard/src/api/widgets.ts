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
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { useOptimisticSave } from '@/composables/useOptimisticSave';
import { numberOrNull } from '@/lib/number';

import { channelOptions, channelQuery, failed, refusal } from './channelQuery';
import type { ApiOperation } from './errors';
import { client } from './client';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type Widget = components['schemas']['WidgetDetail'];
export type WidgetSettingsSchema = components['schemas']['WidgetSettingsSchema'];
export type WidgetSettingsField = components['schemas']['WidgetSettingsField'];
export type WidgetSettings = Widget['settings'];

const version = '1';
const pageSize = 100;
const firstPage = 1;

export const widgetsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'widgets',
];
export const widgetSchemaKey = (channelId: string | null, widgetId: string | null): KeyParts => [
  'channels',
  channelId,
  'widget-schema',
  widgetId,
];

// Pages are read one after the other until the bot says there are no more.
async function readWidgets(channelId: string): Promise<Widget[]> {
  const widgets: Widget[] = [];
  let page: number | null = firstPage;

  while (page !== null) {
    const current: number = page;
    const { data, error, response } = await client.GET(
      '/api/v{version}/channels/{channelId}/widgets',
      {
        params: { path: { version, channelId }, query: { Page: current, PageSize: pageSize } },
      },
    );

    if (error !== undefined)
      throw failed('widgets', response.status, error);

    widgets.push(...(data?.data ?? []));

    page = data?.hasMore
      ? (numberOrNull(data.nextPage) ?? current + 1)
      : null;
  }

  return widgets;
}

export function useWidgets(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<Widget[], Error> {
  return channelQuery(channelId, widgetsKey, readWidgets);
}

export const widgetsOptions = (id: string) => channelOptions(widgetsKey, readWidgets, id);

// The schema is read per widget; a widget without one returns an empty field list.
export function useWidgetSchema(
  channelId: MaybeRefOrGetter<string | null>,
  widgetId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<WidgetSettingsSchema | null, Error> {
  return useQuery({
    queryKey: computed(() => widgetSchemaKey(toValue(channelId), toValue(widgetId))),
    queryFn: async (): Promise<WidgetSettingsSchema | null> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/widgets/{widgetId}/settings-schema',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              widgetId: toValue(widgetId) as string,
            },
          },
        },
      );

      if (error !== undefined)
        throw failed('widget settings', response.status, error);

      return data?.data ?? null;
    },
    enabled: computed(() => toValue(channelId) !== null && toValue(widgetId) !== null),
  });
}

// Ready means switched on and not failing on its last run.
export function isWidgetReady(widget: Widget): boolean {
  return widget.isEnabled && !widget.lastRuntimeError;
}

export type WidgetEnabledInput = { widgetId: string; isEnabled: boolean };
export type WidgetSettingsInput = { widgetId: string; settings: NonNullable<WidgetSettings> };

async function putWidget(
  channelId: string,
  widgetId: string,
  body: components['schemas']['UpdateWidgetRequest'],
  what: ApiOperation,
): Promise<Widget[]> {
  const { error, response } = await client.PUT(
    '/api/v{version}/channels/{channelId}/widgets/{widgetId}',
    {
      params: {
        path: {
          version,
          channelId,
          widgetId,
        },
      },
      body,
    },
  );

  if (error !== undefined)
    throw failed(what, response.status, error);

  return readWidgets(channelId);
}

function changed(
  current: Widget[] | undefined,
  widgetId: string,
  change: Partial<Widget>,
): Widget[] {
  return (current ?? []).map((row: Widget): Widget =>
    row.id === widgetId
      ? { ...row, ...change }
      : row,
  );
}

// The switch moves at once; a refused save puts it back and reaches the error inbox.
export function useSetWidgetEnabled(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<Widget[], Error, WidgetEnabledInput, number> {
  return useOptimisticSave<Widget[], WidgetEnabledInput>({
    describe: ({ widgetId, isEnabled }) => ({ key: isEnabled
      ? 'turnOnWidget'
      : 'turnOffWidget', params: { id: widgetId } }),
    queryKey: () => widgetsKey(toValue(channelId)),
    mutationFn: ({ widgetId, isEnabled }: WidgetEnabledInput): Promise<Widget[]> =>
      putWidget(toValue(channelId) as string, widgetId, { isEnabled }, 'switch widget'),
    apply: (current: Widget[] | undefined, { widgetId, isEnabled }: WidgetEnabledInput): Widget[] =>
      changed(current, widgetId, { isEnabled }),
  });
}

// The whole settings map travels in the PUT; the row shows it at once and then holds what the bot returns.
export function useSaveWidgetSettings(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<Widget[], Error, WidgetSettingsInput, number> {
  return useOptimisticSave<Widget[], WidgetSettingsInput>({
    describe: ({ widgetId }) => ({ key: 'saveWidgetSettings', params: { id: widgetId } }),
    queryKey: () => widgetsKey(toValue(channelId)),
    mutationFn: ({ widgetId, settings }: WidgetSettingsInput): Promise<Widget[]> =>
      putWidget(toValue(channelId) as string, widgetId, { settings }, 'save widget settings'),
    apply: (current: Widget[] | undefined, { widgetId, settings }: WidgetSettingsInput): Widget[] =>
      changed(current, widgetId, { settings }),
  });
}

export type WidgetTestInput = { widgetId: string; eventType: string };

export type WidgetTestResult = components['schemas']['WidgetTestEventResult'];

// The sample goes to the one overlay named; the answer says if it was sent and if a browser source had it open.
export function useTestWidgetEvent(
  channelId: MaybeRefOrGetter<string | null>,
) {
  return useMutation({
    mutationFn: async ({ widgetId, eventType }: WidgetTestInput): Promise<WidgetTestResult> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/widgets/{widgetId}/test-event',
        {
          params: { path: {
            version,
            channelId: toValue(channelId) as string,
            widgetId,
          } },
          // Without data the bot sends its own sample payload.
          body: { eventType } as components['schemas']['WidgetTestEventRequest'],
        },
      );

      if (error !== undefined)
        throw refusal('widget test', error, response.status);

      return data?.data ?? { delivered: false, attached: false };
    },
  });
}
