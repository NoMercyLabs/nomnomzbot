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
import DetailEditorPane from '@/components/layout/DetailEditorPane.vue';
import ListDetail from '@/components/layout/ListDetail.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
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
      <DetailEditorPane
        :open="row !== undefined"
        :idle-title="$t('dashboard.eventResponses.list.pickTitle')"
        :idle-text="$t('dashboard.eventResponses.list.pick')"
        :back-label="$t('dashboard.eventResponses.back')"
        @back="pane.close"
      >
        <EventResponseEditor v-if="row" :key="row.eventType" :row="row" />
      </DetailEditorPane>
    </template>
  </ListDetail>
</template>
