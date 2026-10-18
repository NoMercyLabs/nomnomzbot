<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Voice triggers page: the channel's spoken words on the left, the open one on the right.
// The open trigger lives in the address (?voice=), so a refresh or a link keeps it.
import Plus from '~icons/nnz/add';
import { computed, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';

import {
  useDeleteVoiceTrigger,
  useVoiceListenerLink,
  useVoiceTriggers,
  type VoiceTrigger,
} from '@/api/voiceTriggers';
import CopyLinkButton from '@/components/layout/CopyLinkButton.vue';
import ListDetail from '@/components/layout/ListDetail.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import NewVoiceTriggerDialog from './components/NewVoiceTriggerDialog.vue';
import VoiceTriggerEditor from './components/VoiceTriggerEditor.vue';

const creating = ref(false);

const route = useRoute();
const router = useRouter();
const { channelId } = useCurrentChannel();
const { can } = useCan();
// Creating and editing need voicetriggers:write; a reader sees the list only.
const canWrite = computed<boolean>(() => can('voicetriggers:write'));
const triggers = useVoiceTriggers(channelId);
const listener = useVoiceListenerLink(channelId);
const remove = useDeleteVoiceTrigger(channelId);

const panel = usePanelState(triggers, (data: VoiceTrigger[]): boolean =>
  data.length === 0);
// The empty state holds a button, so the panel shows it as ready content.
const state = computed<PanelState>(() =>
  panel.value === 'empty'
    ? 'ready'
    : panel.value);
const none = computed<boolean>(() => panel.value === 'empty');

const selected = computed<string | null>(() =>
  typeof route.query.voice === 'string'
    ? route.query.voice
    : null,
);
const row = computed<VoiceTrigger | undefined>(() =>
  (triggers.data.value ?? []).find((item): boolean => item.id === selected.value),
);

function select(id: string | undefined): void {
  void router.push({ query: { ...route.query, voice: id } });
}

function removeTrigger(item: VoiceTrigger): void {
  select(undefined);
  remove.mutate(item.id);
}
</script>

<template>
  <PageHeader
    :title="$t('dashboard.voiceTriggers.title')"
    :crumb="$t('dashboard.voiceTriggers.crumb')"
  >
    <template #actions>
      <Button v-if="canWrite"
              type="button"
              variant="outline"
              size="sm"
              @click="creating = true"
      >
        <Plus aria-hidden="true" />
        {{ $t('dashboard.voiceTriggers.new') }}
      </Button>
    </template>
  </PageHeader>
  <ListDetail :show-detail="row !== undefined">
    <template #list>
      <PanelCard
        :title="$t('dashboard.voiceTriggers.list.title')"
        :state="state"
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
        <div
          v-if="none"
          data-slot="panel-empty"
          class="flex h-full flex-col items-center justify-center gap-3 p-4 text-center text-sm text-muted-foreground"
        >
          {{ $t('dashboard.voiceTriggers.list.empty') }}
          <Button v-if="canWrite" type="button" size="sm" @click="creating = true">{{
            $t('dashboard.voiceTriggers.list.makeFirst')
          }}</Button>
        </div>
        <SummaryRow
          v-for="item in triggers.data.value ?? []"
          v-else
          :key="item.id"
          pick
          :selected="item.id === selected"
          :title="item.word"
          :detail="$t('dashboard.voiceTriggers.list.heard', { count: item.currentCount }, Number(item.currentCount))"
          data-voice-row
          :data-voice-word="item.word"
          @click="select(item.id)"
        >
          <template v-if="!item.isEnabled" #end>
            <StatusBadge tone="off" :label="$t('dashboard.voiceTriggers.list.off')" />
          </template>
        </SummaryRow>
      </PanelCard>
    </template>
    <template v-if="canWrite" #detail>
      <VoiceTriggerEditor v-if="row" :key="row.id" :row="row" @remove="removeTrigger" />
      <PanelCard
        v-else
        state="empty"
        :title="$t('dashboard.voiceTriggers.list.pickTitle')"
        :empty-text="$t('dashboard.voiceTriggers.list.pick')"
      />
    </template>
  </ListDetail>
  <NewVoiceTriggerDialog v-model:open="creating" />
</template>
