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
import { Button } from '@/components/ui/button';
import { useCurrentChannel, type ChannelChoice } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';

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
  <p v-if="!isLoaded" role="status" class="text-sm text-muted-foreground">
    {{ $t('site.invite.channelLoading') }}
  </p>
  <div v-else-if="target === null" class="flex flex-col gap-3">
    <p class="text-sm">{{ $t('site.invite.noChannel') }}</p>
    <Button as-child class="w-full">
      <RouterLink to="/app" data-action="dashboard">{{ $t('site.invite.dashboard') }}</RouterLink>
    </Button>
  </div>
  <div v-else-if="done" class="flex flex-col gap-3">
    <p data-result="redeemed" class="text-sm">
      {{ $t('site.invite.redeemed', { channel: target.displayName, gift: props.gift }) }}
    </p>
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
      {{ $t('site.invite.redeem', { channel: target.displayName }) }}
    </Button>
    <p v-if="failure" role="alert" class="text-sm text-destructive">{{ failure }}</p>
  </div>
</template>
