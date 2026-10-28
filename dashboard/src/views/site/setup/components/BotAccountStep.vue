<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The separate bot account is optional: without it the broadcaster account speaks as the bot.
// The person approves the account on Twitch in another tab; this step asks the bot until it is connected.
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query';
import { computed, ref, watch } from 'vue';

import {
  botStatusQuery,
  fetchBotAuthorizeUrl,
  setupWizardKey,
  type SetupStep,
} from '@/api/setup';
import { Button } from '@/components/ui/button';

import InstructionLine from './InstructionLine.vue';
import SetupAddress from './SetupAddress.vue';

defineProps<{ step: SetupStep }>();

const queries = useQueryClient();
const waiting = ref(false);
const status = useQuery({
  ...botStatusQuery,
  enabled: waiting,
  refetchInterval: 3000,
});
const connected = computed(() => status.data.value?.connected === true);
const authorizeUrl = ref('');
const authorize = useMutation({
  mutationFn: fetchBotAuthorizeUrl,
  onSuccess: (url) => {
    // The link stays on screen: a popup blocker can eat the window, and the person may need it in another
    // browser profile.
    authorizeUrl.value = url;
    window.open(url, '_blank', 'noopener');
    waiting.value = true;
  },
});

// Once the bot reports the account, the wizard step turns complete too.
watch(connected, (now) => {
  if (now) {
    void queries.invalidateQueries({ queryKey: setupWizardKey });
  }
});
</script>

<template>
  <div class="flex flex-col gap-4">
    <ul class="flex list-disc flex-col gap-1 pl-5 text-sm text-muted-foreground">
      <li v-for="line in step.instructions" :key="line"><InstructionLine :line="line" /></li>
    </ul>

    <p v-if="connected" class="text-sm" role="status">
      {{ $t('site.setup.botConnected', { name: status.data.value?.displayName ?? status.data.value?.login ?? '' }) }}
    </p>
    <p v-else-if="waiting" class="text-sm text-muted-foreground" role="status">
      {{ $t('site.setup.botWaiting') }}
    </p>
    <p v-if="authorize.isError.value" class="text-sm text-destructive" role="alert">
      {{ $t('site.setup.botFailed') }}
    </p>

    <div>
      <Button
        :variant="step.complete || connected ? 'outline' : 'default'"
        :disabled="authorize.isPending.value"
        @click="authorize.mutate()"
      >
        {{ $t('site.setup.botAuthorize') }}
      </Button>
    </div>

    <div v-if="authorizeUrl && !connected" class="flex flex-col gap-2">
      <p class="text-sm text-muted-foreground">{{ $t('site.setup.botOtherProfile') }}</p>
      <p class="text-sm"><SetupAddress :url="authorizeUrl" use="both" /></p>
    </div>
  </div>
</template>
