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

import { useChatTriggers, useDeleteChatTrigger, type ChatTrigger } from '@/api/chatTriggers';
import ListDetail from '@/components/layout/ListDetail.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import EmptyState from '@/components/layout/EmptyState.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { useQueryPane } from '@/composables/useQueryPane';

import ChatTriggerEditor from './ChatTriggerEditor.vue';
import NewChatTriggerDialog from './NewChatTriggerDialog.vue';

const creating = defineModel<boolean>('creating', { required: true });

const { channelId } = useCurrentChannel();
const { can } = useCan();
const triggers = useChatTriggers(channelId);
const remove = useDeleteChatTrigger(channelId);

const panel = usePanelState(triggers, (data: ChatTrigger[]): boolean =>
  data.length === 0);

const pane = useQueryPane('trigger');
const selected = pane.id;
const row = computed<ChatTrigger | undefined>(() =>
  (triggers.data.value ?? []).find((item): boolean => item.id === selected.value),
);

function removeTrigger(item: ChatTrigger): void {
  pane.close();
  remove.mutate(item.id);
}
</script>

<template>
  <ListDetail :show-detail="row !== undefined">
    <template #list>
      <PanelCard
        :title="$t('dashboard.timers.triggers.list.title')"
        :state="panel"
        :error-text="$t('dashboard.timers.triggers.list.error')"
        @retry="triggers.refetch()"
      >
        <template #empty>
          <EmptyState kind="none" :description="$t('dashboard.timers.triggers.list.empty')">
            <template v-if="can('chattriggers:write')" #action>
              <Button type="button" size="sm" variant="outline" @click="creating = true">{{
                $t('dashboard.timers.triggers.list.makeFirst')
              }}</Button>
            </template>
          </EmptyState>
        </template>
        <SummaryRow
          v-for="item in triggers.data.value ?? []"
          :key="item.id"
          pick
          :selected="item.id === selected"
          :title="item.pattern"
          :detail="$t(`dashboard.timers.triggers.match.${item.matchType}`)"
          data-trigger-row
          :data-trigger-pattern="item.pattern"
          @click="pane.open(item.id)"
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
