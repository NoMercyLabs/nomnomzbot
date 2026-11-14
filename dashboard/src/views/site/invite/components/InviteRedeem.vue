<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The signed-in half of the invite page: Redeem on the person's own channel, then what that channel now has.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useRedeemInviteCode } from '@/api/billing';
import InlineNotice from '@/components/layout/InlineNotice.vue';
import { Button } from '@/components/ui/button';
import { Spinner } from '@/components/ui/spinner';
import { useCurrentChannel, type ChannelChoice } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';

import PublicWait from '../../components/PublicWait.vue';

const props = defineProps<{ code: string; gift: string }>();

const { t } = useI18n();
const { channels, current, isLoaded } = useCurrentChannel();

// An invite is a gift for a streamer's own channel, never for a channel they only moderate.
const target = computed<ChannelChoice | null>(() => {
  if (current.value?.kind === 'own')
    return current.value;

  return channels.value.find((row) => row.kind === 'own' && row.channelId !== null) ?? null;
});

const redeem = useRedeemInviteCode(computed(() => target.value?.channelId ?? null));
const done = ref(false);
const failure = ref('');

async function redeemNow(): Promise<void> {
  failure.value = '';

  try {
    await redeem.mutateAsync(props.code);
    done.value = true;
  } catch (error) {
    failure.value = errorText(error, t);
  }
}
</script>

<template>
  <PublicWait v-if="!isLoaded">{{ $t('site.invite.channelLoading') }}</PublicWait>
  <div v-else-if="target === null" class="flex flex-col gap-3">
    <InlineNotice tone="info" compact>{{ $t('site.invite.noChannel') }}</InlineNotice>
    <Button as-child class="w-full">
      <RouterLink to="/app" data-action="dashboard">{{ $t('site.invite.dashboard') }}</RouterLink>
    </Button>
  </div>
  <div v-else-if="done" class="flex flex-col gap-3">
    <InlineNotice tone="success" compact data-result="redeemed">
      {{ $t('site.invite.redeemed', { channel: target.displayName, gift: props.gift }) }}
    </InlineNotice>
    <Button as-child class="w-full">
      <RouterLink to="/app" data-action="dashboard">{{ $t('site.invite.dashboard') }}</RouterLink>
    </Button>
  </div>
  <div v-else class="flex flex-col gap-3">
    <Button
      data-action="redeem"
      class="w-full"
      :disabled="redeem.isPending.value"
      @click="redeemNow"
    >
      <Spinner v-if="redeem.isPending.value" />
      {{ $t('site.invite.redeem', { channel: target.displayName }) }}
    </Button>
    <InlineNotice v-if="failure" tone="error" compact>{{ failure }}</InlineNotice>
  </div>
</template>
