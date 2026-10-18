<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';
import Locked from '~icons/nnz/locked';

import { signOut } from '@/api/auth';
import { Button } from '@/components/ui/button';
import {
  Empty,
  EmptyContent,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from '@/components/ui/empty';

import ChannelSwitch from './ChannelSwitch.vue';

// The suspended channel's frame: the reason, the date and one way to reach the platform. It replaces every page.
const props = defineProps<{
  name: string;
  reason: string | null;
  suspendedAt: string | null;
  // Null until a platform admin sets the support contact: the page then says who to ask instead of linking.
  contactUrl: string | null;
  canSwitch: boolean;
}>();

const { t, d } = useI18n();
const router = useRouter();

async function leave(): Promise<void> {
  await signOut();
  await router.push('/');
}
</script>

<template>
  <div
    data-slot="channel-suspended"
    class="flex min-h-0 flex-1 items-center justify-center overflow-y-auto p-6"
  >
    <Empty>
      <EmptyHeader>
        <EmptyMedia variant="icon"><Locked aria-hidden="true" /></EmptyMedia>
        <EmptyTitle>{{ t('frame.channel.suspended.title', { name: props.name }) }}</EmptyTitle>
        <EmptyDescription>{{ t('frame.channel.suspended.description') }}</EmptyDescription>
      </EmptyHeader>
      <EmptyContent>
        <dl class="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-left text-sm">
          <dt class="text-muted-foreground">{{ t('frame.channel.suspended.reason') }}</dt>
          <dd data-slot="suspension-reason">{{ props.reason ?? t('frame.channel.suspended.noReason') }}</dd>
          <template v-if="props.suspendedAt">
            <dt class="text-muted-foreground">{{ t('frame.channel.suspended.since') }}</dt>
            <dd>{{ d(new Date(props.suspendedAt), 'long') }}</dd>
          </template>
        </dl>
        <p v-if="!props.contactUrl" data-slot="suspension-no-contact" class="text-sm text-muted-foreground">
          {{ t('frame.channel.suspended.noContact') }}
        </p>
        <div class="flex flex-wrap justify-center gap-2">
          <Button
            v-if="props.contactUrl"
            as="a"
            :href="props.contactUrl"
            target="_blank"
            rel="noopener"
            data-slot="suspension-contact"
          >
            {{ t('frame.channel.suspended.contact') }}
          </Button>
          <Button variant="ghost" @click="leave">{{ t('frame.channel.suspended.signOut') }}</Button>
        </div>
        <div v-if="props.canSwitch" class="w-56">
          <ChannelSwitch compact />
        </div>
      </EmptyContent>
    </Empty>
  </div>
</template>
