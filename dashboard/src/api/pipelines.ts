// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { UseQueryReturnType } from '@tanstack/vue-query'
import type { MaybeRefOrGetter } from 'vue'

import { channelQuery, failed } from './channelQuery'
import { client } from './client'
import type { components } from './schema'

export type PipelineListItem = components['schemas']['PipelineListItemDto']

const version = '1'
const pageSize = 100
const maxPages = 100

export const pipelinesKey = (channelId: string | null): unknown[] => ['channels', channelId, 'pipelines']

async function readPipelines(channelId: string): Promise<PipelineListItem[]> {
  const rows: PipelineListItem[] = []
  for (let page = 1; page <= maxPages; page += 1) {
    const { data, error, response } = await client.GET('/api/v{version}/channels/{channelId}/pipelines', {
      params: { path: { version, channelId }, query: { Page: page, PageSize: pageSize } },
    })
    if (error !== undefined) throw failed('pipelines', response.status)
    rows.push(...(data?.data ?? []))
    if (data?.hasMore !== true) break
  }
  return rows
}

export function usePipelines(channelId: MaybeRefOrGetter<string | null>): UseQueryReturnType<PipelineListItem[], Error> {
  return channelQuery(channelId, pipelinesKey, readPipelines)
}
