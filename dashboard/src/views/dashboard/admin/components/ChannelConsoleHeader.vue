<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The top of a channel console: the header card with who the channel is (the page header above names the
// channel and leads back to the list), its live and status badges and its tier. Act as is the one filled button; Copy link is outline
// and always names the open tab. Acting as someone opens a support session first, so it takes both keys.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';

import type { AdminTenantDetail } from '@/api/admin';
import CopyLinkButton from '@/components/layout/CopyLinkButton.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Avatar, AvatarFallback } from '@/components/ui/avatar';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { usePlatformCan } from '@/composables/usePlatformCan';

import ActAsDialog, { type ActAsChannel } from './ActAsDialog.vue';
import { initialsOf, statusTone } from './channelDisplay';

const props = defineProps<{
  channel: AdminTenantDetail;
  live: boolean;
  tier?: string;
  tab: string;
}>();

const { t } = useI18n();
const { can } = usePlatformCan();
const route = useRoute();
const router = useRouter();

const canActAs = computed<boolean>(() => can('tenant:access') && can('user:impersonate'));
const actingOn = ref<ActAsChannel | null>(null);

const link = computed<string>(() => {
  const location = router.resolve({
    name: 'dashboard-admin-channels-console',
    params: { channelId: props.channel.id, tab: props.tab },
    query: route.query,
  });

  return window.location.origin + location.href;
});

function actAs(): void {
  actingOn.value = { id: props.channel.id, name: props.channel.name };
}
</script>

<template>
  <div class="grid gap-3">
    <Card data-console-header size="sm">
      <CardContent class="flex flex-wrap items-center gap-4">
        <Avatar class="size-12 rounded-lg">
          <AvatarFallback class="rounded-lg">{{ initialsOf(channel.name) }}</AvatarFallback>
        </Avatar>
        <div class="grid min-w-0 flex-1 gap-1">
          <h2 class="truncate text-lg font-semibold tracking-tight">{{ channel.name }}</h2>
          <div class="flex flex-wrap items-center gap-x-4 gap-y-1">
            <StatusBadge v-if="live" tone="live" :label="t('dashboard.admin.live.live')" />
            <StatusBadge
              :tone="statusTone(channel.status)"
              :label="t(`dashboard.admin.status.${channel.status}`)"
            />
            <span v-if="tier" class="text-sm text-muted-foreground">{{ tier }}</span>
          </div>
        </div>
        <div class="flex flex-wrap items-center gap-2">
          <CopyLinkButton :url="link" :label="t('dashboard.admin.console.copyLink')" />
          <Button v-if="canActAs" data-act-as @click="actAs">
            {{ t('dashboard.admin.actAs.open') }}
          </Button>
        </div>
      </CardContent>
    </Card>
    <ActAsDialog
      v-if="canActAs && actingOn !== null"
      :channel="actingOn"
      @close="actingOn = null"
    />
  </div>
</template>
