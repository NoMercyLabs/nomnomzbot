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
import KeyValueList, { type KeyValueItem } from '@/components/layout/KeyValueList.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { shortDate } from '@/lib/time';

import ProfileStandingEdit from './ProfileStandingEdit.vue';

const props = defineProps<{ userId: string; name: string }>();

const { t, locale } = useI18n();
const { channelId } = useCurrentChannel();
const access = useChannelAccess(channelId);

const canRead = computed(() => access.can('moderation:usercontext:read'));
const canWrite = computed(() => access.can('moderation:suspicioususer:write'));

const standing = useStanding(channelId, () =>
  canRead.value
    ? props.userId
    : null);
const readState = usePanelState(standing, () =>
  false);
const state = computed(() =>
  canRead.value
    ? readState.value
    : 'empty');

const level = computed<StandingLevel | null>(() =>
  (standing.data.value?.standing as StandingLevel | undefined) ?? null);
const levelKey = computed(() => level.value ?? 'normal');

const facts = computed<KeyValueItem[]>(() => {
  const current = standing.data.value;

  return [
    { label: t('profile.botStanding.now'), value: t(`profile.botStanding.levels.${levelKey.value}.name`) },
    { label: t('profile.botStanding.reason'), value: current?.reason || null },
    {
      label: t('profile.botStanding.since'),
      value: current
        ? shortDate(current.updatedAt, locale.value)
        : null,
    },
  ];
});

const editOpen = ref(false);
</script>

<template>
  <PanelCard
    data-slot="profile-standing"
    pad
    :title="$t('profile.botStanding.title')"
    :state="state"
    :empty-text="$t('profile.botStanding.noAccess')"
    :error-text="$t('profile.botStanding.error')"
    @retry="standing.refetch()"
  >
    <div class="grid gap-3">
      <p data-profile-standing-effect class="text-sm text-muted-foreground">
        {{ $t(`profile.botStanding.levels.${levelKey}.effect`) }}
      </p>
      <KeyValueList :items="facts" />
    </div>

    <template #footer>
      <Button
        data-profile-standing-change
        type="button"
        variant="outline"
        size="sm"
        :disabled="!canWrite"
        :title="canWrite ? undefined : $t('profile.botStanding.noWrite')"
        @click="editOpen = true"
      >
        {{ $t('profile.botStanding.change') }}
      </Button>
    </template>
  </PanelCard>
  <ProfileStandingEdit
    v-model:open="editOpen"
    :user-id="userId"
    :name="name"
    :level="level"
    :saved-reason="standing.data.value?.reason ?? null"
  />
</template>
