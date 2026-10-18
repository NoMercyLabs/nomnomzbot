<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The features of the channel, each with a switch. A feature outside the channel's plan shows why and its switch
// stays off; one that needs Twitch permissions lists them. The bot flips the switch itself, so the page shows
// the state the bot answers with, never a guess.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useChannelFeatures, useToggleFeature, type ChannelFeature } from '@/api/features';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import RowGroup from '@/components/layout/RowGroup.vue';
import GatedSwitch from '@/components/layout/GatedSwitch.vue';
import { Badge } from '@/components/ui/badge';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useTime } from '@/composables/useTime';
import { errorText } from '@/lib/errors';

const { t } = useI18n();
const time = useTime();
const { channelId } = useCurrentChannel();
const features = useChannelFeatures(channelId);
const toggle = useToggleFeature(channelId);

const error = ref<string | undefined>(undefined);
const busyKey = ref<string | undefined>(undefined);

const rows = computed(() => features.data.value ?? []);

const state = computed<PanelState>(() => {
  if (features.isError.value)
    return 'error';

  if (features.isPending.value)
    return 'loading';

  return rows.value.length === 0
    ? 'empty'
    : 'ready';
});

function scopeLine(feature: ChannelFeature): string {
  return t('dashboard.settings.features.scopes', { scopes: feature.requiredScopes.join(', ') });
}

function since(feature: ChannelFeature): string | undefined {
  if (!feature.isEnabled || !feature.enabledAt)
    return undefined;

  return t('dashboard.settings.features.since', { date: time.date(feature.enabledAt) });
}

async function flip(feature: ChannelFeature): Promise<void> {
  if (!feature.entitled || busyKey.value !== undefined) return;

  error.value = undefined;
  busyKey.value = feature.featureKey;

  try {
    await toggle.mutateAsync(feature.featureKey);
  } catch (failure) {
    error.value = errorText(failure, t);
  } finally {
    busyKey.value = undefined;
  }
}
</script>

<template>
  <PageHeader :title="$t('frame.nav.settingsPages.features')" :crumb="$t('dashboard.settings.crumb')" />

  <PageBody>
    <PanelCard
      data-view="features"
      pad
      :title="$t('dashboard.settings.features.title')"
      :description="$t('dashboard.settings.features.description')"
      :state="state"
      :error-text="$t('dashboard.settings.features.loadFailed')"
      :empty-text="$t('dashboard.settings.features.empty')"
      @retry="features.refetch()"
    >
      <RowGroup>
        <div
          v-for="feature in rows"
          :key="feature.featureKey"
          :data-feature="feature.featureKey"
          class="flex min-h-11 items-center gap-3 rounded-[8px] px-3 py-2"
        >
          <div class="min-w-0 flex-1">
            <span class="flex flex-wrap items-center gap-2">
              <span :id="`feature-${feature.featureKey}`" class="text-sm font-medium wrap-break-word">{{ feature.label }}</span>
              <Badge v-if="!feature.entitled" variant="outline">
                {{ feature.requiredTier ?? $t('dashboard.settings.features.upgrade') }}
              </Badge>
            </span>
            <span class="block text-sm text-muted-foreground">{{ feature.description }}</span>
            <span v-if="!feature.entitled && feature.entitlementReason" class="block text-sm text-muted-foreground">
              {{ feature.entitlementReason }}
            </span>
            <span v-if="feature.requiredScopes.length > 0" class="block text-sm text-muted-foreground">
              {{ scopeLine(feature) }}
            </span>
            <span v-if="since(feature)" class="block text-sm text-muted-foreground">{{ since(feature) }}</span>
          </div>
          <GatedSwitch
            action="feature:write"
            :model-value="feature.isEnabled"
            :disabled="!feature.entitled || busyKey !== undefined"
            :aria-labelledby="`feature-${feature.featureKey}`"
            @update:model-value="flip(feature)"
          />
        </div>
      </RowGroup>
      <p v-if="error" role="alert" class="px-3 pt-3 text-sm text-destructive">{{ error }}</p>
    </PanelCard>
  </PageBody>
</template>
