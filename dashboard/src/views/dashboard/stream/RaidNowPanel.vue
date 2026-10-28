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
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { TwitchUserNotFoundError, useTwitchLookup, type TwitchViewer } from '@/api/community';
import { useCancelRaid, useStartRaid } from '@/api/raids';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
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
const target = ref<TwitchViewer | null>(null);
const missing = ref('');
const confirmOpen = useTargetOpen(target);

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
    :title="t('dashboard.stream.raidNow.title')"
    :description="t('dashboard.stream.raidNow.description')"
  >
    <form class="flex gap-2" data-raid-form @submit.prevent="find">
      <Input
        v-model="login"
        data-raid-input
        :aria-label="t('dashboard.stream.raidNow.channelLabel')"
        :placeholder="t('dashboard.stream.raidNow.channelPlaceholder')"
      />
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
    <p v-if="missing" data-raid-missing role="alert" class="pt-3 text-sm text-destructive">
      {{ t("dashboard.stream.raidNow.notFound", { login: missing }) }}
    </p>
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
