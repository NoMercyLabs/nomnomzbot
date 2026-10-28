<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The Chat triggers tab: the channel's keyword triggers on the left, the open trigger on the right.
// The open trigger lives in the address (?trigger=), so a refresh or a link keeps it.
import { computed } from 'vue';
import { useRoute, useRouter } from 'vue-router';

import { useChatTriggers, useDeleteChatTrigger, type ChatTrigger } from '@/api/chatTriggers';
import ListDetail from '@/components/layout/ListDetail.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import ChatTriggerEditor from './ChatTriggerEditor.vue';
import NewChatTriggerDialog from './NewChatTriggerDialog.vue';

const creating = defineModel<boolean>('creating', { required: true });

const route = useRoute();
const router = useRouter();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const triggers = useChatTriggers(channelId);
const remove = useDeleteChatTrigger(channelId);

const panel = usePanelState(triggers, (data: ChatTrigger[]): boolean =>
  data.length === 0);
// The empty state holds a button, so the panel shows it as ready content.
const state = computed<PanelState>(() =>
  panel.value === 'empty'
    ? 'ready'
    : panel.value);
const none = computed<boolean>(() => panel.value === 'empty');

const selected = computed<string | null>(() =>
  typeof route.query.trigger === 'string'
    ? route.query.trigger
    : null,
);
const row = computed<ChatTrigger | undefined>(() =>
  (triggers.data.value ?? []).find((item): boolean => item.id === selected.value),
);

function select(id: string | undefined): void {
  void router.push({ query: { ...route.query, trigger: id } });
}

function removeTrigger(item: ChatTrigger): void {
  select(undefined);
  remove.mutate(item.id);
}
</script>

<template>
  <ListDetail :show-detail="row !== undefined">
    <template #list>
      <PanelCard
        :title="$t('dashboard.timers.triggers.list.title')"
        :state="state"
        :error-text="$t('dashboard.timers.triggers.list.error')"
        @retry="triggers.refetch()"
      >
        <div
          v-if="none"
          data-slot="panel-empty"
          class="flex h-full flex-col items-center justify-center gap-3 p-4 text-center text-sm text-muted-foreground"
        >
          {{ $t('dashboard.timers.triggers.list.empty') }}
          <Button v-if="can('chattriggers:write')" type="button" size="sm" @click="creating = true">{{
            $t('dashboard.timers.triggers.list.makeFirst')
          }}</Button>
        </div>
        <SummaryRow
          v-for="item in triggers.data.value ?? []"
          v-else
          :key="item.id"
          pick
          :selected="item.id === selected"
          :title="item.pattern"
          :detail="$t(`dashboard.timers.triggers.match.${item.matchType}`)"
          data-trigger-row
          :data-trigger-pattern="item.pattern"
          @click="select(item.id)"
        >
          <template v-if="!item.isEnabled" #end>
            <StatusBadge tone="off" :label="$t('dashboard.timers.list.off')" />
          </template>
        </SummaryRow>
      </PanelCard>
    </template>
    <template v-if="can('chattriggers:write')" #detail>
      <ChatTriggerEditor v-if="row" :key="row.id" :row="row" @remove="removeTrigger" />
      <PanelCard
        v-else
        state="empty"
        :title="$t('dashboard.timers.triggers.list.pickTitle')"
        :empty-text="$t('dashboard.timers.triggers.list.pick')"
      />
    </template>
  </ListDetail>
  <NewChatTriggerDialog v-model:open="creating" />
</template>
