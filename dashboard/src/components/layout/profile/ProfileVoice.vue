<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The voice text to speech reads this person's messages with: their own, or the channel voice.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useChannelAccess } from '@/api/access';
import { usePersonVoice } from '@/api/tts';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import PlaySampleButton from '@/views/dashboard/tts/PlaySampleButton.vue';
import { localeLabel } from '@/views/dashboard/tts/voiceGroups';

import ProfileVoiceEdit from './ProfileVoiceEdit.vue';

const props = defineProps<{ userId: string; name: string }>();

const { t, locale } = useI18n();
const { channelId } = useCurrentChannel();
const access = useChannelAccess(channelId);

const canRead = computed(() => access.can('tts:voice:read'));
const canWrite = computed(() => access.can('tts:uservoice:write'));

const personVoice = usePersonVoice(channelId, () =>
  canRead.value
    ? props.userId
    : null);
const readState = usePanelState(personVoice, () =>
  false);
const state = computed(() =>
  canRead.value
    ? readState.value
    : 'empty');

const voice = computed(() => personVoice.data.value ?? null);

// A voice the catalog no longer lists still shows by its id.
const title = computed(() => {
  if (!voice.value)
    return t('profile.voice.channel');

  return voice.value.voice?.displayName ?? voice.value.voiceId;
});
const detail = computed(() => {
  if (!voice.value)
    return t('profile.voice.channelDetail');

  return voice.value.voice
    ? localeLabel(voice.value.voice.locale, locale.value)
    : t('profile.voice.gone');
});

const editOpen = ref(false);
</script>

<template>
  <!-- The value of the Settings list row: the current voice and its actions on one line; the row label names it. -->
  <div data-slot="profile-voice">
    <p v-if="state === 'empty'" class="text-sm text-muted-foreground">{{ $t('profile.voice.noAccess') }}</p>
    <Skeleton v-else-if="state === 'loading'" class="h-10 rounded-lg" />
    <div v-else-if="state === 'error'" role="alert" class="flex items-center gap-3 text-sm">
      <span class="flex-1 text-destructive">{{ $t('profile.voice.error') }}</span>
      <Button type="button" variant="outline" size="sm" @click="personVoice.refetch()">
        {{ $t('common.panel.retry') }}
      </Button>
    </div>
    <div v-else class="flex items-center gap-3">
      <div class="grid min-w-0 flex-1 text-sm">
        <span data-profile-voice-name class="truncate font-medium">{{ title }}</span>
        <span class="truncate text-muted-foreground">{{ detail }}</span>
      </div>
      <PlaySampleButton v-if="voice" :voice-id="voice.voiceId" compact />
      <Button
        v-if="canWrite"
        data-profile-voice-change
        type="button"
        variant="outline"
        size="sm"
        @click="editOpen = true"
      >
        {{ $t('profile.voice.change') }}
      </Button>
    </div>
  </div>
  <ProfileVoiceEdit
    v-model:open="editOpen"
    :user-id="userId"
    :name="name"
    :voice-id="voice?.voiceId ?? null"
  />
</template>
