<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->

<script setup lang="ts">
// The channel's named pick lists, the lists behind {list.pick.<name>} in a reply: search, add, edit (rename
// included) and delete. A delete first counts the pipeline steps that use the list and shows the count.
import { refDebounced } from '@vueuse/core';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  fetchPickListBlastRadius,
  useDeletePickList,
  usePickLists,
  type PickList,
} from '@/api/pickLists';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { errorText } from '@/lib/errors';
import { blastLines } from '@/views/dashboard/scripts/components/blastLines';

import Plus from '~icons/nnz/add';

import PickListDialog from './PickListDialog.vue';
import PickListRow from './PickListRow.vue';

const pageSize = 50;

const { t, te } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();

const search = ref('');
const typed = refDebounced(search, 300);
const limit = ref(pageSize);

const lists = usePickLists(channelId, typed, limit);
const remove = useDeletePickList(channelId);

const rows = computed(() => lists.data.value?.lists ?? []);
const hasMore = computed(() => lists.data.value?.hasMore === true);
const listState = usePanelState(lists, (page) =>
  page.lists.length === 0);

const dialogOpen = ref(false);
const editing = ref<PickList | null>(null);

function openDialog(item: PickList | null): void {
  editing.value = item;
  dialogOpen.value = true;
}

const confirmOpen = ref(false);
const deleting = ref<PickList | null>(null);
const consequences = ref<string[]>([]);
const actionError = ref<string | undefined>(undefined);

async function askDelete(item: PickList): Promise<void> {
  actionError.value = undefined;

  try {
    const lines = blastLines(await fetchPickListBlastRadius(item.id), t, te);

    consequences.value = lines.length > 0
      ? lines
      : [t('dashboard.picklists.delete.nothing')];

    deleting.value = item;
    confirmOpen.value = true;
  } catch (failure) {
    actionError.value = errorText(failure, t);
  }
}

function confirmDelete(): void {
  if (!deleting.value) return;

  remove.mutate(deleting.value.id, {
    onSettled: (): void => {
      confirmOpen.value = false;
    },
    onError: (failure: Error): void => {
      actionError.value = errorText(failure, t);
    },
  });
}

function loadMore(): void {
  limit.value += pageSize;
}
</script>

<template>
  <PageHeader :title="t('dashboard.picklists.title')">
    <template #actions>
      <Button v-if="can('picklists:write')"
              type="button"
              size="sm"
              data-picklist-new
              @click="openDialog(null)"
      >
        <Plus aria-hidden="true" />
        {{ t("dashboard.picklists.new") }}
      </Button>
    </template>
  </PageHeader>
  <PageBody>
    <p v-if="actionError" role="alert" class="text-sm text-destructive">{{ actionError }}</p>
    <Input
      v-model="search"
      type="search"
      data-picklist-search
      :placeholder="t('dashboard.picklists.search')"
      :aria-label="t('dashboard.picklists.search')"
    />
    <PanelCard
      tier="lead"
      :title="t('dashboard.picklists.listTitle')"
      :state="listState"
      :empty-text="typed === '' ? t('dashboard.picklists.empty') : t('dashboard.picklists.noMatch')"
      :error-text="t('dashboard.picklists.error')"
      @retry="lists.refetch()"
    >
      <PickListRow
        v-for="item in rows"
        :key="item.id"
        :list="item"
        data-picklist-row
        @edit="openDialog(item)"
        @remove="askDelete(item)"
      />
      <div v-if="hasMore" class="pt-3">
        <Button type="button"
                variant="outline"
                size="sm"
                data-picklist-more
                @click="loadMore"
        >
          {{ t("dashboard.picklists.more") }}
        </Button>
      </div>
    </PanelCard>
  </PageBody>
  <PickListDialog v-model:open="dialogOpen" :list="editing" />
  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="t('dashboard.picklists.delete.title', { name: deleting?.name ?? '' })"
    :description="t('dashboard.picklists.delete.description')"
    :consequences="consequences"
    :action="t('dashboard.picklists.delete.action')"
    :pending="remove.isPending.value"
    @confirm="confirmDelete"
  />
</template>
