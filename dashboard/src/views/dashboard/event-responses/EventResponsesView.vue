<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Event responses page: the channel's event types on the left, the open event's reply on the right.
import { computed } from 'vue';
import { useRoute, useRouter } from 'vue-router';

import { useEventResponses, type EventResponse } from '@/api/eventResponses';
import BackLink from '@/components/layout/BackLink.vue';
import ListDetail from '@/components/layout/ListDetail.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import EventResponseEditor from './components/EventResponseEditor.vue';
import EventResponseList from './components/EventResponseList.vue';

const route = useRoute();
const router = useRouter();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const responses = useEventResponses(channelId);

// The open event lives in the address (?event=), so a refresh or a link keeps it.
const selected = computed<string | null>(() =>
  typeof route.query.event === 'string'
    ? route.query.event
    : null,
);
const row = computed<EventResponse | undefined>(() =>
  (responses.data.value ?? []).find(
    (item): boolean => item.eventType === selected.value,
  ),
);

function select(eventType: string | undefined): void {
  void router.push({ query: { ...route.query, event: eventType } });
}
</script>

<template>
  <PageHeader
    :title="$t('dashboard.eventResponses.title')"
    :crumb="$t('dashboard.eventResponses.crumb')"
  />
  <ListDetail :show-detail="row !== undefined">
    <template #list>
      <EventResponseList :selected="selected" @pick="select" />
    </template>
    <template v-if="can('eventresponses:write')" #detail>
      <div v-if="row" class="flex flex-1 flex-col gap-2 md:min-h-0">
        <BackLink
          data-detail-back
          data-page-inset
          class="self-start md:hidden"
          :label="$t('dashboard.eventResponses.back')"
          @click="select(undefined)"
        />
        <EventResponseEditor :key="row.eventType" :row="row" />
      </div>
      <PanelCard
        v-else
        state="empty"
        :title="$t('dashboard.eventResponses.list.pickTitle')"
        :empty-text="$t('dashboard.eventResponses.list.pick')"
      />
    </template>
  </ListDetail>
</template>
