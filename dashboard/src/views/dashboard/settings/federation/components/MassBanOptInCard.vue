<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Whether a moderator's mass ban across their channels also reaches this channel. Only the broadcaster can change
// it (the switch carries the permission check); the shown value is always the one the bot stores.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useMassBanOptIn, useSaveMassBanOptIn } from '@/api/massBanOptIn';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { errorText } from '@/lib/errors';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const optIn = useMassBanOptIn(channelId);
const save = useSaveMassBanOptIn(channelId);
const state = usePanelState(optIn);

const failure = ref<string | undefined>(undefined);

const accepts = computed({
  get: () => optIn.data.value?.accepts ?? false,
  set: (on) => void change(on),
});

async function change(on: boolean): Promise<void> {
  failure.value = undefined;

  try {
    await save.mutateAsync({ accepts: on });
  } catch (error) {
    failure.value = errorText(error, t);
  }
}
</script>

<template>
  <PanelCard
    data-view="mass-ban-opt-in"
    :title="$t('dashboard.settings.federation.massBan.title')"
    :description="$t('dashboard.settings.federation.massBan.description')"
    :state="state"
    :error-text="$t('dashboard.settings.federation.massBan.loadFailed')"
    @retry="optIn.refetch()"
  >
    <RowGroup>
      <div class="flex min-h-11 flex-col gap-2 rounded-[8px] px-3 py-2 md:flex-row md:flex-wrap md:items-center md:gap-3">
        <div class="min-w-0 md:flex-1">
          <span class="block text-sm font-medium wrap-break-word">
            {{ $t('dashboard.settings.federation.massBan.label') }}
          </span>
          <span class="block text-sm text-muted-foreground">
            {{ $t('dashboard.settings.federation.massBan.hint') }}
          </span>
        </div>
        <LabeledSwitch
          v-model="accepts"
          action="moderation:massban:optout"
          :label="$t('dashboard.settings.federation.massBan.switch')"
          :disabled="save.isPending.value"
        />
      </div>
    </RowGroup>
    <p v-if="failure" role="alert" class="pt-3 text-sm text-destructive">{{ failure }}</p>
  </PanelCard>
</template>
