<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Raids another channel now. The channel is looked up on Twitch by name first, and Twitch's own answer names who
// the raid goes to in the confirm. Raid is the one primary action; Cancel raid stops a raid that has not begun.
import { refDebounced } from '@vueuse/core';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { useChannelSearch, type ChannelSearch } from '@/api/channels';
import { TwitchUserNotFoundError, useTwitchLookup, type TwitchViewer } from '@/api/community';
import { useCancelRaid, useStartRaid } from '@/api/raids';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import InlineNotice from '@/components/layout/InlineNotice.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Popover, PopoverAnchor, PopoverContent } from '@/components/ui/popover';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useTargetOpen } from '@/composables/useTargetOpen';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();

const lookup = useTwitchLookup(channelId);
const start = useStartRaid(channelId);
const cancel = useCancelRaid(channelId);

const login = ref('');
const target = ref<Pick<TwitchViewer, 'id' | 'displayName'> | null>(null);
const missing = ref('');
const confirmOpen = useTargetOpen(target);

// Twitch is asked once the typing pauses, so each key press does not cost a search.
const typed = refDebounced(computed(() => login.value.trim().replace(/^@/, '')), 300);
const found = useChannelSearch(channelId, typed);
const listOpen = computed<boolean>(
  () => login.value.trim() !== '' && target.value === null && (found.data.value?.length ?? 0) > 0,
);

function pick(channel: ChannelSearch): void {
  missing.value = '';
  target.value = { id: channel.id, displayName: channel.displayName };
}

async function find(): Promise<void> {
  const name = login.value.trim().replace(/^@/, '');

  missing.value = '';

  if (name === '') return;

  try {
    target.value = await lookup.mutateAsync(name);
  } catch (error: unknown) {
    if (error instanceof TwitchUserNotFoundError) {
      missing.value = name;
    } else {
      toast.error(t('dashboard.stream.raidNow.lookupError'));
    }
  }
}

function confirmRaid(): void {
  const viewer = target.value;

  if (viewer === null) return;

  start.mutate(viewer.id, {
    onSuccess: () => {
      login.value = '';
      toast.success(t('dashboard.stream.raidNow.started', { name: viewer.displayName }));
    },
    onError: () => toast.error(t('dashboard.stream.raidNow.startError', { name: viewer.displayName })),
    onSettled: () => {
      target.value = null;
    },
  });
}

function stop(): void {
  cancel.mutate(undefined, {
    onSuccess: () => toast.success(t('dashboard.stream.raidNow.canceled')),
    onError: () => toast.error(t('dashboard.stream.raidNow.cancelError')),
  });
}
</script>

<template>
  <PanelCard
    v-if="can('live-ops:raids:write')"
    data-view="raid-now"
    pad
    :title="t('dashboard.stream.raidNow.title')"
    :description="t('dashboard.stream.raidNow.description')"
  >
    <form class="flex gap-2" data-raid-form @submit.prevent="find">
      <!-- The list opens in a portal: the card around the box clips anything drawn inside it. -->
      <Popover :open="listOpen">
        <PopoverAnchor as-child>
          <Input
            v-model="login"
            data-raid-input
            autocomplete="off"
            :aria-label="t('dashboard.stream.raidNow.channelLabel')"
            :placeholder="t('dashboard.stream.raidNow.channelPlaceholder')"
          />
        </PopoverAnchor>
        <PopoverContent
          align="start"
          class="max-h-64 w-72 gap-0 overflow-auto p-1"
          @open-auto-focus.prevent
        >
          <button
            v-for="channel in found.data.value"
            :key="channel.id"
            type="button"
            data-raid-option
            class="flex w-full items-baseline gap-2 rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent"
            @click="pick(channel)"
          >
            <span>{{ channel.displayName }}</span>
            <span class="text-xs text-muted-foreground">{{ channel.login }}</span>
          </button>
        </PopoverContent>
      </Popover>
      <Button type="submit" data-raid-start :disabled="login.trim() === '' || lookup.isPending.value">
        {{ t("dashboard.stream.raidNow.raid") }}
      </Button>
      <Button type="button"
              variant="outline"
              data-raid-cancel
              :disabled="cancel.isPending.value"
              @click="stop"
      >
        {{ t("dashboard.stream.raidNow.cancel") }}
      </Button>
    </form>
    <InlineNotice v-if="missing"
                  data-raid-missing
                  tone="error"
                  compact
                  class="pt-3"
    >
      {{ t("dashboard.stream.raidNow.notFound", { login: missing }) }}
    </InlineNotice>
    <ConfirmDialog
      v-model:open="confirmOpen"
      :title="t('dashboard.stream.raidNow.confirmTitle', { name: target?.displayName ?? '' })"
      :description="t('dashboard.stream.raidNow.confirmDescription')"
      :action="t('dashboard.stream.raidNow.confirmAction')"
      :pending="start.isPending.value"
      @confirm="confirmRaid"
    />
  </PanelCard>
</template>
