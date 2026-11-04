<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import Megaphone from '~icons/nnz/megaphone';

import type { ChatLine } from '@/api/chat';
import { useTime } from '@/composables/useTime';
import { keysOf, oneOf } from '@/lib/array';
import type { ChatDisplay } from '../chatDisplay';

const props = defineProps<{ line: ChatLine; display?: ChatDisplay }>();
const { t, te } = useI18n();
const clock = useTime();

const isAnnouncement = computed(() =>
  props.line.kind === 'notice' &&
  (props.line.noticeType === 'announcement' || props.line.noticeType === 'shared_chat_announcement'));

const colorClasses = {
  PRIMARY: 'border-primary',
  BLUE: 'border-role-artist',
  GREEN: 'border-success',
  ORANGE: 'border-warning',
  PURPLE: 'border-secondary',
} as const satisfies Record<string, string>;

const announcementColor = computed(() =>
  colorClasses[oneOf(keysOf(colorClasses), props.line.announcementColor ?? undefined) ?? 'PRIMARY']);

const noticeTitle = computed(() => {
  if (props.line.kind === 'held') {
    const key = `dashboard.chat.native.held.${props.line.heldStatus ?? 'held'}`;

    return te(key)
      ? t(key)
      : t('dashboard.chat.native.held.unknown');
  }

  if (props.line.kind === 'moderation')
    return t(`dashboard.chat.native.moderation.${props.line.noticeType ?? 'chat_clear'}`, {
      name: props.line.displayName,
    });

  const type = props.line.noticeType?.replace(/^shared_chat_/, '') ?? 'unknown';
  const key = `dashboard.chat.native.notice.${type}`;

  return te(key)
    ? t(key)
    : t('dashboard.chat.native.notice.unknown');
});

const time = computed(() =>
  props.display?.timestamps === '12'
    ? clock.time12(props.line.timestamp)
    : clock.time24(props.line.timestamp));
</script>

<template>
  <div
    :data-line-id="line.id"
    :data-native-kind="line.kind"
    :data-announcement-color="isAnnouncement ? line.announcementColor ?? 'PRIMARY' : undefined"
    class="px-3 py-1 text-sm"
  >
    <div
      class="flex items-start gap-2"
      :class="isAnnouncement ? ['rounded-md border-l-4 bg-muted/40 px-3 py-2', announcementColor] : 'text-muted-foreground'"
    >
      <Megaphone v-if="isAnnouncement" class="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <span v-else class="mt-1 size-1.5 shrink-0 rounded-full bg-muted-foreground" aria-hidden="true" />
      <div class="min-w-0 flex-1">
        <div class="flex flex-wrap items-center gap-x-2 gap-y-0.5">
          <strong class="font-semibold text-foreground">{{ noticeTitle }}</strong>
          <span v-if="isAnnouncement && line.displayName" class="text-muted-foreground">
            {{ $t('dashboard.chat.native.by', { name: line.displayName }) }}
          </span>
          <span
            v-if="line.sourceChannelName"
            data-source-channel
            class="rounded-sm border border-border px-1.5 text-xs text-muted-foreground"
          >{{ $t('dashboard.chat.native.source', { name: line.sourceChannelName }) }}</span>
          <time
            v-if="display?.timestamps !== 'off'"
            class="text-xs tabular-nums text-muted-foreground"
            :datetime="line.timestamp"
          >{{ time }}</time>
        </div>
        <p v-if="line.kind === 'notice' && line.systemText && !isAnnouncement" class="wrap-anywhere">
          {{ line.systemText }}
        </p>
        <p v-if="line.message" class="wrap-anywhere text-foreground">
          {{ line.message }}
        </p>
      </div>
    </div>
  </div>
</template>
