<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The Voice triggers page: the channel's spoken words on the left, the open one on the right.
// The open trigger lives in the address (?voice=), so a refresh or a link keeps it.
import Plus from '~icons/nnz/add';
import { computed, ref } from 'vue';

import {
  useDeleteVoiceTrigger,
  useVoiceListenerLink,
  useVoiceTriggers,
  type VoiceTrigger,
} from '@/api/voiceTriggers';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import CopyLinkButton from '@/components/layout/CopyLinkButton.vue';
import DetailEditorPane from '@/components/layout/DetailEditorPane.vue';
import ListDetail from '@/components/layout/ListDetail.vue';
import PageActionFooter from '@/components/layout/PageActionFooter.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import EmptyState from '@/components/layout/EmptyState.vue';
import RowEditDelete from '@/components/layout/RowEditDelete.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { useQueryPane } from '@/composables/useQueryPane';

import NewVoiceTriggerDialog from './components/NewVoiceTriggerDialog.vue';
import VoiceListenerHeader from './components/VoiceListenerHeader.vue';
import VoiceTriggerEditor from './components/VoiceTriggerEditor.vue';

const creating = ref(false);

const { channelId } = useCurrentChannel();
const { can } = useCan();
// Creating and editing need voicetriggers:write; a reader sees the list only.
const canWrite = computed<boolean>(() => can('voicetriggers:write'));
const triggers = useVoiceTriggers(channelId);
const listener = useVoiceListenerLink(channelId);
const remove = useDeleteVoiceTrigger(channelId);

const panel = usePanelState(triggers, (data: VoiceTrigger[]): boolean =>
  data.length === 0);

const pane = useQueryPane('voice');
const selected = pane.id;
const row = computed<VoiceTrigger | undefined>(() =>
  (triggers.data.value ?? []).find((item): boolean => item.id === selected.value),
);

function removeTrigger(item: VoiceTrigger): void {
  pane.close();
  remove.mutate(item.id);
}

// The row's Delete asks here; the editor's own Delete asks in its footer.
const asking = ref<VoiceTrigger>();

function confirmRowDelete(): void {
  const item = asking.value;

  asking.value = undefined;

  if (item) {
    removeTrigger(item);
  }
}
</script>

<template>
  <PageHeader
    :title="$t('dashboard.voiceTriggers.title')"
    :crumb="$t('dashboard.voiceTriggers.crumb')"
  >
    <template #actions>
      <!-- Below the tablet width the listener and the new button move into the frame's sticky action footer. -->
      <PageActionFooter>
        <div class="flex flex-wrap items-center gap-3">
          <VoiceListenerHeader :triggers="triggers.data.value ?? []" :link="listener.data.value" />
          <Button v-if="canWrite"
                  type="button"
                  variant="outline"
                  size="sm"
                  @click="creating = true"
          >
            <Plus aria-hidden="true" />
            {{ $t('dashboard.voiceTriggers.new') }}
          </Button>
        </div>
      </PageActionFooter>
    </template>
  </PageHeader>
  <ListDetail :show-detail="row !== undefined">
    <template #list>
      <PanelCard
        :title="$t('dashboard.voiceTriggers.list.title')"
        :state="panel"
        :error-text="$t('dashboard.voiceTriggers.list.error')"
        @retry="triggers.refetch()"
      >
        <template v-if="listener.data.value" #actions>
          <CopyLinkButton
            data-voice-listener-link
            :url="listener.data.value"
            :label="$t('dashboard.voiceTriggers.list.copyListener')"
          />
        </template>
        <template #empty>
          <EmptyState kind="none" :description="$t('dashboard.voiceTriggers.list.empty')">
            <template v-if="canWrite" #action>
              <Button type="button" size="sm" variant="outline" @click="creating = true">{{
                $t('dashboard.voiceTriggers.list.makeFirst')
              }}</Button>
            </template>
          </EmptyState>
        </template>
        <SummaryRow
          v-for="item in triggers.data.value ?? []"
          :key="item.id"
          pick
          :selected="item.id === selected"
          :title="item.word"
          :detail="$t('dashboard.voiceTriggers.list.heard', { count: item.currentCount }, Number(item.currentCount))"
          data-voice-row
          :data-voice-word="item.word"
          @click="pane.open(item.id)"
        >
          <template v-if="!item.isEnabled" #end>
            <StatusBadge tone="off" :label="$t('dashboard.voiceTriggers.list.off')" />
          </template>
          <template v-if="canWrite" #trailing>
            <RowEditDelete
              :edit-label="$t('dashboard.voiceTriggers.list.edit', { name: item.word })"
              :delete-label="$t('dashboard.voiceTriggers.list.delete', { name: item.word })"
              @edit="pane.open(item.id)"
              @delete="asking = item"
            />
          </template>
        </SummaryRow>
      </PanelCard>
    </template>
    <template v-if="canWrite" #detail>
      <DetailEditorPane
        :open="row !== undefined"
        :idle-title="$t('dashboard.voiceTriggers.list.pickTitle')"
        :idle-text="$t('dashboard.voiceTriggers.list.pick')"
        :back-label="$t('dashboard.voiceTriggers.back')"
        @back="pane.close()"
      >
        <VoiceTriggerEditor v-if="row" :key="row.id" :row="row" @remove="removeTrigger" />
      </DetailEditorPane>
    </template>
  </ListDetail>
  <NewVoiceTriggerDialog v-model:open="creating" />
  <ConfirmDialog
    :open="asking !== undefined"
    :title="$t('dashboard.voiceTriggers.editor.confirmTitle', { name: asking?.word })"
    :description="$t('dashboard.voiceTriggers.editor.confirmBody', { count: asking?.currentCount })"
    :action="$t('dashboard.voiceTriggers.editor.confirmAction')"
    @update:open="asking = undefined"
    @confirm="confirmRowDelete"
  />
</template>
