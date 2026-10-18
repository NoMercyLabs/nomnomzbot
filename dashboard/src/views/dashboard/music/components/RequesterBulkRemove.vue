<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Picks one requester from the people who have songs in the queue now and removes all their songs at once.
// The list is built from the queue rows the page already holds. The confirm names the person and the count.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useRemoveRequesterSongs, type QueueItem } from '@/api/music';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import { requestersIn, type Requester } from './requesters';

const props = defineProps<{ rows: QueueItem[] }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const removeAll = useRemoveRequesterSongs(channelId);

const requesters = computed<Requester[]>(() => requestersIn(props.rows));
const picked = ref<string>('');
const confirmOpen = ref<boolean>(false);
const chosen = computed<Requester | undefined>(() =>
  requesters.value.find((requester) => requester.name === picked.value),
);

function confirmRemove(): void {
  if (chosen.value === undefined) return;

  removeAll.mutate({ name: chosen.value.name });
  confirmOpen.value = false;
  picked.value = '';
}
</script>

<template>
  <div
    v-if="requesters.length > 0 && can('music:queue:moderate')"
    data-requester-remove
    class="flex items-center gap-2 px-4 py-3"
  >
    <Select v-model="picked">
      <SelectTrigger class="min-w-0 flex-1" :aria-label="$t('dashboard.music.queue.pickRequester')">
        <SelectValue :placeholder="$t('dashboard.music.queue.pickRequester')" />
      </SelectTrigger>
      <SelectContent>
        <SelectItem
          v-for="requester in requesters"
          :key="requester.name"
          :value="requester.name"
          data-requester-option
        >
          {{ $t("dashboard.music.queue.requesterOption", requester) }}
        </SelectItem>
      </SelectContent>
    </Select>
    <Button variant="destructive" :disabled="chosen === undefined" @click="confirmOpen = true">
      {{ $t("dashboard.music.queue.removeAll") }}
    </Button>
  </div>
  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="
      t('dashboard.music.queue.removeAllTitle', {
        count: chosen?.count ?? 0,
        name: chosen?.name ?? '',
      })
    "
    :description="$t('dashboard.music.queue.removeAllBody')"
    :action="$t('dashboard.music.queue.removeAll')"
    @confirm="confirmRemove"
  />
</template>
