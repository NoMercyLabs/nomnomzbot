<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { computed, type Component, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink, useRouter } from 'vue-router'

import { useActivity, type ActivityEvent } from '@/api/home'
import PanelCard from '@/components/layout/PanelCard.vue'
import RowGroup from '@/components/layout/RowGroup.vue'
import SummaryRow from '@/components/layout/SummaryRow.vue'
import { Button } from '@/components/ui/button'
import { useCurrentChannel } from '@/composables/useCurrentChannel'
import { usePanelState } from '@/composables/usePanelState'

import ArrowRight from '~icons/nnz/arrow-right'

import { describeActivity, type ActivityRowText } from './activityKinds'

defineProps<{ live: boolean }>()

const { t, locale } = useI18n()
const { channelId } = useCurrentChannel()

const activityQuery = useActivity(channelId)
const activityState = usePanelState(activityQuery, (items: ActivityEvent[]) =>
  items.length === 0)

const router = useRouter()
// "All events" shows only when an events page exists: a link that lands elsewhere breaks its promise.
const allEventsRoute: ComputedRef<string | null> = computed(() =>
  router.hasRoute('dashboard-events')
    ? 'dashboard-events'
    : null,
)

type Row = {
  id: string
  icon: Component
  title: string
  detail: string | undefined
  time: string
}

function say(text: ActivityRowText): string {
  const count: unknown = text.params?.count

  return t(text.key, text.params ?? {}, typeof count === 'number'
    ? count
    : 1)
}

function eventTime(event: ActivityEvent): string {
  return new Intl.DateTimeFormat(locale.value, {
    weekday: 'short',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).format(new Date(event.timestamp))
}

const rows: ComputedRef<Row[]> = computed(() =>
  (activityQuery.data.value ?? []).map((event: ActivityEvent): Row => {
    const view = describeActivity(event)
    const detail = say(view.detail)
    const title = view.title
      ? say(view.title)
      : (event.username ?? view.actor ?? undefined)

    return {
      id: event.id,
      icon: view.icon,
      title: title ?? detail,
      detail: title === undefined
        ? undefined
        : detail,
      time: eventTime(event),
    }
  }),
)
</script>

<template>
  <PanelCard
    :title="live ? t('dashboard.home.activity.live') : t('dashboard.home.activity.offline')"
    :state="activityState"
    :empty-text="t('dashboard.home.activity.empty')"
    :error-text="t('dashboard.home.activity.error')"
    @retry="activityQuery.refetch()"
  >
    <template v-if="allEventsRoute" #actions>
      <Button as-child variant="ghost" size="sm">
        <RouterLink :to="{ name: allEventsRoute }">
          {{ t('dashboard.home.activity.allEvents') }}
          <ArrowRight class="size-4" aria-hidden="true" />
        </RouterLink>
      </Button>
    </template>
    <RowGroup>
      <div v-for="row in rows" :key="row.id" data-activity-row>
        <SummaryRow :title="row.title" :detail="row.detail">
          <template #leading>
            <component
              :is="row.icon"
              class="size-5 flex-none text-muted-foreground"
              aria-hidden="true"
            />
          </template>
          <template #trailing>
            <span class="text-sm text-muted-foreground tabular-nums">{{ row.time }}</span>
          </template>
        </SummaryRow>
      </div>
    </RowGroup>
  </PanelCard>
</template>
