<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// How the bot treats a person in this channel: normal, muted, shadowbanned or blacklisted. It never touches
// Twitch; ban and timeout stay the Twitch punishments. Each choice says what it does before it is saved.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useChannelAccess } from '@/api/access';
import { useStanding, type StandingLevel } from '@/api/moderation';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { shortDate } from '@/lib/time';

import ProfileStandingEdit from './ProfileStandingEdit.vue';

const props = defineProps<{ userId: string; name: string }>();

const { locale } = useI18n();
const { channelId } = useCurrentChannel();
const access = useChannelAccess(channelId);

const canRead = computed(() => access.can('moderation:usercontext:read'));
const canWrite = computed(() => access.can('moderation:suspicioususer:write'));

const standing = useStanding(channelId, () =>
  canRead.value
    ? props.userId
    : null);
const readState = usePanelState(standing, () => false);
const state = computed(() =>
  canRead.value
    ? readState.value
    : 'empty');

const level = computed<StandingLevel | null>(
  () => (standing.data.value?.standing as StandingLevel | undefined) ?? null,
);
const levelKey = computed(() => level.value ?? 'normal');

// The reason and date of a standing someone set; the default standing has neither.
const note = computed<string | null>(() => {
  const current = standing.data.value;

  if (!current?.reason)
    return null;

  return `${current.reason} · ${shortDate(current.updatedAt, locale.value)}`;
});

const editOpen = ref(false);
</script>

<template>
  <!-- The value of the Settings list row, like the TTS voice: the level, what it does, and Change. -->
  <div data-slot="profile-standing">
    <p v-if="state === 'empty'" class="text-sm text-muted-foreground">
      {{ $t("profile.botStanding.noAccess") }}
    </p>
    <Skeleton v-else-if="state === 'loading'" class="h-10 rounded-lg" />
    <div v-else-if="state === 'error'" role="alert" class="flex items-center gap-3 text-sm">
      <span class="flex-1 text-destructive">{{ $t("profile.botStanding.error") }}</span>
      <Button type="button" variant="outline" size="sm" @click="standing.refetch()">
        {{ $t("common.panel.retry") }}
      </Button>
    </div>
    <div v-else class="flex flex-wrap items-center gap-x-3 gap-y-2">
      <div class="grid min-w-48 flex-1 text-sm">
        <span data-profile-standing-name class="font-medium">{{
          $t(`profile.botStanding.levels.${levelKey}.name`)
        }}</span>
        <span data-profile-standing-effect class="text-muted-foreground">{{
          $t(`profile.botStanding.levels.${levelKey}.effect`)
        }}</span>
        <span v-if="note" data-profile-standing-note class="text-muted-foreground wrap-anywhere">{{
          note
        }}</span>
      </div>
      <Button
        data-profile-standing-change
        type="button"
        variant="outline"
        size="sm"
        :disabled="!canWrite"
        :title="canWrite ? undefined : $t('profile.botStanding.noWrite')"
        @click="editOpen = true"
      >
        {{ $t("profile.botStanding.change") }}
      </Button>
    </div>
  </div>
  <ProfileStandingEdit
    v-model:open="editOpen"
    :user-id="userId"
    :name="name"
    :level="level"
    :saved-reason="standing.data.value?.reason ?? null"
  />
</template>
