<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->

<script setup lang="ts">
// The channel's sound clips: upload a new one, change its name, volume, cooldown, chat word and who may play it,
// switch it on or off, and delete it. A delete first counts the places that use the clip and shows the count.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  fetchSoundClipBlastRadius,
  useDeleteSoundClip,
  useSaveSoundClip,
  useSoundClips,
  type SoundClip,
} from '@/api/soundClips';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { errorText } from '@/lib/errors';
import { blastLines } from '@/views/dashboard/scripts/components/blastLines';

import Plus from '~icons/nnz/add';

import SoundClipAddDialog from './SoundClipAddDialog.vue';
import SoundClipEditDialog from './SoundClipEditDialog.vue';
import SoundClipRow from './SoundClipRow.vue';
import { changeFromForm, editFormFrom } from './soundClipForm';

const pageSize = 50;

const { t, te } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();

const limit = ref(pageSize);
const clips = useSoundClips(channelId, limit);
const save = useSaveSoundClip(channelId);
const remove = useDeleteSoundClip(channelId);

const rows = computed(() => clips.data.value?.clips ?? []);
const hasMore = computed(() => clips.data.value?.hasMore === true);
const listState = usePanelState(clips, (page) =>
  page.clips.length === 0);

const addOpen = ref(false);
const editOpen = ref(false);
const editing = ref<SoundClip | null>(null);

function openEdit(item: SoundClip): void {
  editing.value = item;
  editOpen.value = true;
}

const actionError = ref<string | undefined>(undefined);

function toggle(item: SoundClip, enabled: boolean): void {
  actionError.value = undefined;

  save.mutate(
    { id: item.id, change: changeFromForm(editFormFrom(item), enabled) },
    {
      onError: (failure: Error): void => {
        actionError.value = errorText(failure, t);
      },
    },
  );
}

const confirmOpen = ref(false);
const deleting = ref<SoundClip | null>(null);
const consequences = ref<string[]>([]);

async function askDelete(item: SoundClip): Promise<void> {
  actionError.value = undefined;

  try {
    const lines = blastLines(await fetchSoundClipBlastRadius(item.id), t, te);

    consequences.value = lines.length > 0
      ? lines
      : [t('dashboard.sounds.delete.nothing')];

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
  <PageHeader :title="t('dashboard.sounds.title')">
    <template #actions>
      <Button v-if="can('sounds:write')"
              type="button"
              size="sm"
              data-sound-new
              @click="addOpen = true"
      >
        <Plus aria-hidden="true" />
        {{ t("dashboard.sounds.new") }}
      </Button>
    </template>
  </PageHeader>
  <PageBody>
    <p v-if="actionError" role="alert" class="text-sm text-destructive">{{ actionError }}</p>
    <PanelCard
      tier="lead"
      :title="t('dashboard.sounds.listTitle')"
      :state="listState"
      :empty-text="t('dashboard.sounds.empty')"
      :error-text="t('dashboard.sounds.error')"
      @retry="clips.refetch()"
    >
      <SoundClipRow
        v-for="item in rows"
        :key="item.id"
        :clip="item"
        :pending="save.isPending.value"
        data-sound-row
        @toggle="toggle(item, $event)"
        @edit="openEdit(item)"
        @remove="askDelete(item)"
      />
      <div v-if="hasMore" class="pt-3">
        <Button type="button"
                variant="outline"
                size="sm"
                data-sound-more
                @click="loadMore"
        >
          {{ t("dashboard.sounds.more") }}
        </Button>
      </div>
    </PanelCard>
  </PageBody>
  <SoundClipAddDialog v-model:open="addOpen" />
  <SoundClipEditDialog v-model:open="editOpen" :clip="editing" />
  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="t('dashboard.sounds.delete.title', { name: deleting?.displayName ?? '' })"
    :description="t('dashboard.sounds.delete.description')"
    :consequences="consequences"
    :action="t('dashboard.sounds.delete.action')"
    :pending="remove.isPending.value"
    @confirm="confirmDelete"
  />
</template>
