<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The channel's chat filters: each one a regex, a list of blocked terms or a link policy, with the action the bot
// takes on a match. A row switches a filter on or off, opens it for editing or removes it after a confirm. New
// filter is the one primary action of the list; the editor takes over the page body until Save or Cancel.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import {
  useChatFilters,
  useDeleteChatFilter,
  useToggleChatFilter,
  type ChatFilter,
} from '@/api/chatFilters';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { Switch } from '@/components/ui/switch';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import ChatFilterEditor from './ChatFilterEditor.vue';

const { t } = useI18n();
const { can } = useCan();
const { channelId } = useCurrentChannel();

const filters = useChatFilters(channelId);
const toggle = useToggleChatFilter(channelId);
const remove = useDeleteChatFilter(channelId);

const canWrite = computed<boolean>(() => can('moderation:filter:write'));
const state = usePanelState(filters, (list: ChatFilter[]) =>
  list.length === 0);

// null: the list. 'new': an empty editor. A filter: that filter in the editor.
const editing = ref<ChatFilter | 'new' | null>(null);
const removing = ref<ChatFilter | null>(null);

const removeOpen = computed<boolean>({
  get: () => removing.value !== null,
  set: (value: boolean) => {
    if (!value) {
      removing.value = null;
    }
  },
});

function detailOf(filter: ChatFilter): string {
  const type = t(`dashboard.settings.chatFilters.types.${filter.filterType}`);
  const action = t(`dashboard.settings.chatFilters.actions.${filter.action}`);
  const hits = t('dashboard.settings.chatFilters.matches', { count: Number(filter.matchCount) });

  return `${type} · ${action} · ${hits}`;
}

function setEnabled(filter: ChatFilter, isEnabled: boolean): void {
  toggle.mutate({ filter, isEnabled }, {
    onError: (error: Error) =>
      toast.error(t('dashboard.settings.chatFilters.saveError', { reason: error.message })),
  });
}

function confirmRemove(): void {
  const filter = removing.value;

  if (filter === null) return;

  remove.mutate(filter, {
    onError: () => toast.error(t('dashboard.settings.chatFilters.deleteError', { name: filter.name })),
    onSettled: () => {
      removing.value = null;
    },
  });
}
</script>

<template>
  <PageHeader
    :title="$t('frame.nav.settingsPages.chatFilters')"
    :crumb="$t('dashboard.settings.chatFilters.crumb')"
  >
    <template v-if="canWrite && editing === null" #actions>
      <Button size="sm" data-action="new" @click="editing = 'new'">
        {{ $t("dashboard.settings.chatFilters.new") }}
      </Button>
    </template>
  </PageHeader>
  <PageBody>
    <ChatFilterEditor
      v-if="canWrite && editing !== null"
      :key="editing === 'new' ? 'new' : editing.id"
      :filter="editing === 'new' ? null : editing"
      @close="editing = null"
    />
    <template v-else>
      <p class="text-sm text-muted-foreground">{{ $t("dashboard.settings.chatFilters.explain") }}</p>
      <PanelCard
        tier="lead"
        :title="$t('dashboard.settings.chatFilters.listTitle')"
        :state="state"
        :empty-text="$t('dashboard.settings.chatFilters.empty')"
        :error-text="$t('dashboard.settings.chatFilters.loadError')"
        @retry="filters.refetch()"
      >
        <RowGroup>
          <SummaryRow
            v-for="filter in filters.data.value"
            :key="filter.id"
            :data-filter="filter.id"
            :title="filter.name"
            :detail="detailOf(filter)"
          >
            <template #trailing>
              <template v-if="canWrite">
                <Switch
                  :model-value="filter.isEnabled"
                  :aria-label="$t('dashboard.settings.chatFilters.fields.enabled')"
                  :data-filter-enabled="filter.id"
                  :disabled="toggle.isPending.value"
                  @update:model-value="(on: boolean) => setEnabled(filter, on)"
                />
                <Button variant="outline" size="sm" :data-filter-edit="filter.id" @click="editing = filter">
                  {{ $t("dashboard.settings.chatFilters.edit") }}
                </Button>
                <Button variant="outline" size="sm" :data-filter-remove="filter.id" @click="removing = filter">
                  {{ $t("dashboard.settings.chatFilters.remove") }}
                </Button>
              </template>
              <StatusBadge
                v-else
                :data-filter-state="filter.id"
                :tone="filter.isEnabled ? 'ok' : 'off'"
                :label="$t(filter.isEnabled ? 'common.state.on' : 'common.state.off')"
              />
            </template>
          </SummaryRow>
        </RowGroup>
      </PanelCard>
    </template>
    <ConfirmDialog
      v-model:open="removeOpen"
      :title="$t('dashboard.settings.chatFilters.removeTitle', { name: removing?.name ?? '' })"
      :description="$t('dashboard.settings.chatFilters.removeDescription')"
      :action="$t('dashboard.settings.chatFilters.removeAction')"
      :pending="remove.isPending.value"
      @confirm="confirmRemove"
    />
  </PageBody>
</template>
