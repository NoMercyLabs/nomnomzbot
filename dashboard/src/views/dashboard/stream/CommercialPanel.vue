<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Runs an ad break on Twitch. The length is one of the lengths Twitch allows; Run is the one action here.
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useStartCommercial } from '@/api/schedule';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

// The lengths Twitch accepts, in seconds.
const LENGTHS = [30, 60, 90, 120, 150, 180] as const satisfies readonly number[];

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const start = useStartCommercial(channelId);

const lengthSeconds = ref<number>(LENGTHS[0]);

function run(): void {
  start.mutate(lengthSeconds.value);
}
</script>

<template>
  <PanelCard
    v-if="can('live-ops:ads:write')"
    :title="t('dashboard.stream.ads.title')"
    :description="t('dashboard.stream.ads.description')"
  >
    <div class="flex flex-col gap-4">
      <div role="group" :aria-label="t('dashboard.stream.ads.lengthSeconds')" class="flex flex-wrap gap-2">
        <Button
          v-for="seconds in LENGTHS"
          :key="seconds"
          :variant="lengthSeconds === seconds ? 'secondary' : 'outline'"
          :aria-pressed="lengthSeconds === seconds"
          data-commercial-length
          @click="lengthSeconds = seconds"
        >
          {{ t('dashboard.stream.ads.seconds', { seconds }) }}
        </Button>
      </div>
      <p v-if="start.isSuccess.value" class="text-sm text-muted-foreground" role="status">
        {{ start.data.value?.message || t('dashboard.stream.ads.started') }}
      </p>
      <div class="flex justify-end">
        <Button data-commercial-run :disabled="start.isPending.value" @click="run">
          {{ t('dashboard.stream.ads.run') }}
        </Button>
      </div>
    </div>
  </PanelCard>
</template>
