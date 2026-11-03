<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The Event responses page: the channel's event types on the left, the open event's reply on the right.
import { computed } from 'vue';

import { useEventResponses, type EventResponse } from '@/api/eventResponses';
import BackLink from '@/components/layout/BackLink.vue';
import ListDetail from '@/components/layout/ListDetail.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useQueryPane } from '@/composables/useQueryPane';

import EventResponseEditor from './components/EventResponseEditor.vue';
import EventResponseList from './components/EventResponseList.vue';

const { channelId } = useCurrentChannel();
const { can } = useCan();
const responses = useEventResponses(channelId);

// The open event lives in the address (?event=), so a refresh or a link keeps it.
const pane = useQueryPane('event');
const selected = pane.id;
const row = computed<EventResponse | undefined>(() =>
  (responses.data.value ?? []).find(
    (item): boolean => item.eventType === selected.value,
  ),
);

</script>

<template>
  <PageHeader
    :title="$t('dashboard.eventResponses.title')"
    :crumb="$t('dashboard.eventResponses.crumb')"
  />
  <ListDetail :show-detail="row !== undefined">
    <template #list>
      <EventResponseList :selected="selected" @pick="pane.open" />
    </template>
    <template v-if="can('eventresponses:write')" #detail>
      <div v-if="row" class="flex flex-1 flex-col gap-2 md:min-h-0">
        <BackLink
          data-detail-back
          data-page-inset
          class="self-start md:hidden"
          :label="$t('dashboard.eventResponses.back')"
          @click="pane.close"
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
