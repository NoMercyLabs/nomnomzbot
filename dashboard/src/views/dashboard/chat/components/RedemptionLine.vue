<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A redemption in the chat feed: who redeemed what, what it cost, and the words the viewer added.
// It is a quiet muted row with a rail at the left, so it never competes with the messages around it.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { ChatLine } from '@/api/chat';
import UserName from '@/components/layout/UserName.vue';
import { useTime } from '@/composables/useTime';

import { spacingPadding, type ChatDisplay } from '../chatDisplay';

const props = defineProps<{
  line: ChatLine;
  display: ChatDisplay;
  compact?: boolean;
}>();

const { t, te, locale } = useI18n();

// Twitch's fixed reward and Power-up types have a name here; a streamer's own title shows as written.
const fixedKinds = new Set(['automatic_reward', 'power_up']);

const title = computed<string>(() => {
  const key = `dashboard.chat.redemption.types.${props.line.redemption?.title}`;
  const fixed = fixedKinds.has(props.line.redemption?.kind ?? '');

  return fixed && te(key)
    ? t(key)
    : (props.line.redemption?.title ?? '');
});

const cost = computed<string>(() => {
  const unit = props.line.redemption?.costUnit ?? 'channel_points';
  const amount = (props.line.redemption?.cost ?? 0).toLocaleString(locale.value);

  return t(`dashboard.chat.redemption.cost.${unit}`, { amount });
});

const clock = useTime();

const stamp = computed<string>(() =>
  props.display.timestamps === '12'
    ? clock.time12(props.line.timestamp)
    : clock.time24(props.line.timestamp));
</script>

<template>
  <div
    :data-line-id="line.id"
    data-redemption
    :data-kind="line.redemption?.kind"
    class="flex gap-2 border-s-2 border-border text-sm leading-6"
    :class="[spacingPadding[display.spacing], compact ? 'px-2' : 'px-3']"
  >
    <time
      v-if="display.timestamps !== 'off'"
      class="flex-none text-muted-foreground tabular-nums"
      :datetime="line.timestamp"
    >{{ stamp }}</time>
    <p class="min-w-0 flex-1 wrap-anywhere text-muted-foreground">
      <UserName :user-id="line.userId" :name="line.displayName" class="text-foreground" />
      {{ $t('dashboard.chat.redemption.redeemed') }}
      <strong data-redemption-title class="font-medium text-foreground">{{ title }}</strong>
      <span data-redemption-cost class="ms-1 tabular-nums">{{ cost }}</span>
      <span v-if="line.message" data-redemption-input class="block text-foreground">{{ line.message }}</span>
    </p>
  </div>
</template>
