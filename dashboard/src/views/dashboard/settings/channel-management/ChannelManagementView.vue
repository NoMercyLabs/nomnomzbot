<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Channel management: whether the bot is in the channel, and the way to delete the channel. The delete shows what
// it destroys, counted by the bot, and asks for the channel name before it goes ahead. It is soft: for a while the
// channel can be brought back from this page.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  fetchChannelDeletePreview,
  fetchChannelResetPreview,
  useChannelInfo,
  useDeletedChannels,
  useDeleteChannel,
  useJoinChannel,
  useLeaveChannel,
  useResetChannel,
  useRestoreChannel,
  type ChannelDeletePreview,
  type ChannelResetPreview,
} from '@/api/channelManagement';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { merge } from '@/lib/array';
import { errorText } from '@/lib/errors';

import BotPresenceRow from './components/BotPresenceRow.vue';
import DeletedChannelNotice from './components/DeletedChannelNotice.vue';

const { t, te } = useI18n();
const { can } = useCan();
const { channelId } = useCurrentChannel();
const info = useChannelInfo(channelId);
const join = useJoinChannel(channelId);
const leave = useLeaveChannel(channelId);
const remove = useDeleteChannel(channelId);
const restore = useRestoreChannel(channelId);
const reset = useResetChannel(channelId);

const infoState = usePanelState(info);

const busy = ref(false);
const pageError = ref<string | undefined>(undefined);

const leaveOpen = ref(false);
const deleteOpen = ref(false);
const preview = ref<ChannelDeletePreview | null>(null);
const resetOpen = ref(false);
const resetPreview = ref<ChannelResetPreview | null>(null);
// Set once the channel is deleted: the page then offers the way back until the bot removes it for good.
const deleted = ref<ChannelDeletePreview | null>(null);

// One page action at a time: join, leave, delete and restore all report a refusal in the same place.
async function run(action: () => Promise<void>): Promise<void> {
  if (busy.value) return;

  pageError.value = undefined;
  busy.value = true;

  try {
    await action();
  } catch (failure) {
    pageError.value = errorText(failure, t);
  } finally {
    busy.value = false;
  }
}

// The bot names a kind with a key such as blast_radius_category_commands; the words are looked up by the part after the prefix.
function label(base: string, key: string): string {
  const short = key.replace(/^(blast_radius_category|channel_external)_/, '');
  const path = `dashboard.settings.channelManagement.delete.${base}.${short}`;

  return te(path)
    ? t(path)
    : short.replaceAll('_', ' ');
}

const consequences = computed(() => {
  if (preview.value === null)
    return [] as const satisfies readonly string[];

  const radius = preview.value.blastRadius;
  const counted = radius.categories.filter((category) => Number(category.count) > 0);

  const inside = counted.length > 0
    ? counted.map((category) =>
        t('dashboard.settings.channelManagement.delete.line', {
          count: Number(category.count),
          what: label('category', category.categoryKey),
        }))
    : [t('dashboard.settings.channelManagement.delete.nothing')];

  const minimum = radius.isMinimum
    ? [t('dashboard.settings.channelManagement.delete.minimum')]
    : [];

  const outside = preview.value.externalConsequences.map((effect) =>
    t('dashboard.settings.channelManagement.delete.line', {
      count: Number(effect.count),
      what: label('external', effect.consequenceKey),
    }));

  return merge(inside, minimum, outside);
});

async function askDelete(): Promise<void> {
  await run(async () => {
    preview.value = await fetchChannelDeletePreview(channelId.value as string);
    deleteOpen.value = true;
  });
}

async function confirmDelete(): Promise<void> {
  const target = preview.value;

  await run(async () => {
    await remove.mutateAsync();
    deleted.value = target;
  });

  deleteOpen.value = false;
}

const resetLines = computed(() => {
  const counted = (resetPreview.value?.blastRadius.categories ?? []).filter(
    (category) => Number(category.count) > 0,
  );

  return counted.length > 0
    ? counted.map((category) =>
        t('dashboard.settings.channelManagement.delete.line', {
          count: Number(category.count),
          what: label('category', category.categoryKey),
        }))
    : [t('dashboard.settings.channelManagement.reset.nothing')] as const satisfies readonly string[];
});

async function askReset(): Promise<void> {
  await run(async () => {
    resetPreview.value = await fetchChannelResetPreview(channelId.value as string);
    resetOpen.value = true;
  });
}

async function confirmReset(): Promise<void> {
  await run(() =>
    reset.mutateAsync());

  resetOpen.value = false;
}

async function confirmLeave(): Promise<void> {
  await run(() =>
    leave.mutateAsync());

  leaveOpen.value = false;
}

const deletedList = useDeletedChannels();

// The channel just deleted here has its own notice above; the list shows every other channel waiting to be restored.
const otherDeleted = computed(() =>
  (deletedList.data.value ?? []).filter((row) =>
    row.id !== channelId.value));

async function restoreListed(id: string): Promise<void> {
  await run(async () => {
    await restore.mutateAsync(id);
  });
}

async function restoreChannel(): Promise<void> {
  await run(async () => {
    await restore.mutateAsync();
    deleted.value = null;
  });
}
</script>

<template>
  <PageHeader :title="$t('frame.nav.settingsPages.channelManagement')" :crumb="$t('dashboard.settings.crumb')" />

  <PageBody>
    <PanelCard
      data-view="channel-presence"
      tier="lead"
      :title="$t('dashboard.settings.channelManagement.presence.title')"
      :description="$t('dashboard.settings.channelManagement.presence.description')"
      :state="infoState"
      :error-text="$t('dashboard.settings.channelManagement.loadFailed')"
      @retry="info.refetch()"
    >
      <RowGroup>
        <BotPresenceRow
          :joined-at="info.data.value?.botJoinedAt"
          :busy="busy"
          @join="run(() => join.mutateAsync())"
          @leave="leaveOpen = true"
        />
      </RowGroup>
    </PanelCard>
    <PanelCard
      data-view="channel-reset"
      tier="quiet"
      :title="$t('dashboard.settings.channelManagement.reset.title')"
      :description="$t('dashboard.settings.channelManagement.reset.description')"
    >
      <RowGroup>
        <div class="flex min-h-11 flex-wrap items-center gap-3 rounded-[8px] px-3 py-2">
          <p class="min-w-0 flex-1 text-sm text-muted-foreground">
            {{ $t('dashboard.settings.channelManagement.reset.help') }}
          </p>
          <Button v-if="can('setup:write')"
                  data-reset
                  variant="outline"
                  :disabled="busy"
                  @click="askReset"
          >
            {{ $t('dashboard.settings.channelManagement.reset.action') }}
          </Button>
        </div>
      </RowGroup>
    </PanelCard>
    <PanelCard
      data-view="channel-delete"
      tier="quiet"
      :title="$t('dashboard.settings.channelManagement.delete.title')"
      :description="$t('dashboard.settings.channelManagement.delete.description')"
    >
      <RowGroup>
        <DeletedChannelNotice
          v-if="deleted !== null"
          :permanent-after="deleted.permanentAfterUtc"
          :busy="busy"
          @restore="restoreChannel"
        />
        <DeletedChannelNotice
          v-for="gone in otherDeleted"
          :key="gone.id"
          :name="gone.displayName"
          :permanent-after="gone.permanentAfter ?? ''"
          :busy="busy"
          @restore="restoreListed(gone.id)"
        />
        <div v-if="deleted === null" class="flex min-h-11 flex-wrap items-center gap-3 rounded-[8px] px-3 py-2">
          <p class="min-w-0 flex-1 text-sm text-muted-foreground">
            {{ $t('dashboard.settings.channelManagement.delete.help') }}
          </p>
          <Button v-if="can('setup:write')"
                  data-delete
                  variant="destructive"
                  :disabled="busy"
                  @click="askDelete"
          >
            {{ $t('dashboard.settings.channelManagement.delete.action') }}
          </Button>
        </div>
      </RowGroup>
    </PanelCard>
    <p v-if="pageError" role="alert" class="pt-3 text-sm text-destructive">{{ pageError }}</p>
  </PageBody>

  <ConfirmDialog
    v-model:open="leaveOpen"
    :title="$t('dashboard.settings.channelManagement.leave.title')"
    :description="$t('dashboard.settings.channelManagement.leave.description')"
    :consequences="[
      $t('dashboard.settings.channelManagement.leave.stopsChat'),
      $t('dashboard.settings.channelManagement.leave.stopsEvents'),
    ]"
    :action="$t('dashboard.settings.channelManagement.presence.leave')"
    :pending="busy"
    @confirm="confirmLeave"
  />
  <ConfirmDialog
    v-model:open="resetOpen"
    :title="$t('dashboard.settings.channelManagement.reset.confirmTitle', { name: resetPreview?.channelName ?? '' })"
    :description="$t('dashboard.settings.channelManagement.reset.confirmDescription')"
    :consequences="resetLines"
    :action="$t('dashboard.settings.channelManagement.reset.action')"
    :type-to-confirm="resetPreview?.channelName"
    :type-label="$t('dashboard.settings.channelManagement.reset.typeLabel')"
    :pending="busy"
    @confirm="confirmReset"
  />
  <ConfirmDialog
    v-model:open="deleteOpen"
    :title="$t('dashboard.settings.channelManagement.delete.confirmTitle', { name: preview?.channelName ?? '' })"
    :description="$t('dashboard.settings.channelManagement.delete.restoreWindow', { days: Number(preview?.restoreWindowDays ?? 0) })"
    :consequences="consequences"
    :action="$t('dashboard.settings.channelManagement.delete.action')"
    :type-to-confirm="preview?.channelName"
    :type-label="$t('dashboard.settings.channelManagement.delete.typeLabel')"
    :pending="busy"
    @confirm="confirmDelete"
  />
</template>
