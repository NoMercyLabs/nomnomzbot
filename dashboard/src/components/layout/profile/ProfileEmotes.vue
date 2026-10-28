<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The emotes a person's own channel offers, grouped by where they come from (the grouping is ProfileEmoteGroups).
import { usePersonEmotes, type PersonEmote } from '@/api/community';
import PanelCard from '@/components/layout/PanelCard.vue';
import ProfileEmoteGroups from '@/components/layout/profile/ProfileEmoteGroups.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

const props = defineProps<{
  // The internal user id: the emotes route takes the Guid, not the Twitch id.
  userId: string | null;
  // True when the bot has read the account and it is no affiliate or partner: it has no Twitch emotes.
  noTwitchEmotes: boolean;
}>();

const { channelId } = useCurrentChannel();

const emotes = usePersonEmotes(channelId, () => props.userId);
const state = usePanelState(emotes, (list: PersonEmote[]) =>
  list.length === 0);
</script>

<template>
  <PanelCard
    data-slot="profile-emotes"
    bare
    :title="$t('profile.emotes.title')"
    :state="state"
    :empty-text="$t('profile.emotes.empty')"
    :error-text="$t('profile.emotes.error')"
    @retry="emotes.refetch()"
  >
    <ProfileEmoteGroups :emotes="emotes.data.value ?? []" :no-twitch-emotes="noTwitchEmotes" />
  </PanelCard>
</template>
