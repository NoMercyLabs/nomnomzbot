<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A person's name anywhere in the dashboard (owner rule: the profile is a dialog everywhere).
// It opens the one ProfileDialog of the frame by the Twitch user id; data-user carries that id.
// A name with no user id (Spotify, autoplay) is plain text: there is no profile to open.
// The color is the person's Twitch chat color, lifted until it reads on the page.
import { computed } from 'vue';

import { readableNameColor } from '@/lib/accent';
import { useProfileDialogStore } from '@/stores/profileDialog';

const props = withDefaults(
  defineProps<{
    userId?: string | null;
    name: string;
    color?: string | null;
  }>(),
  { userId: null, color: null },
);

const profile = useProfileDialogStore();

const shownColor = computed<string | undefined>(() => {
  if (!props.color)
    return undefined;

  const theme = document.documentElement.classList.contains('light')
    ? 'light'
    : 'dark';

  return readableNameColor(props.color, theme, 4.5) ?? undefined;
});

function open(event: MouseEvent): void {
  if (!props.userId) return;

  profile.open(
    props.userId,
    event.currentTarget instanceof HTMLElement
      ? event.currentTarget
      : null,
  );
}
</script>

<template>
  <button
    v-if="userId"
    type="button"
    data-slot="user-name"
    :data-user="userId"
    :aria-label="$t('profile.open', { name })"
    :style="{ color: shownColor }"
    class="touch-hit -mx-1.5 cursor-pointer rounded-md px-1.5 font-medium underline-offset-4 hover:underline focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:outline-none focus-visible:ring-inset"
    @click="open"
  >
    {{ name }}
  </button>
  <span v-else data-slot="user-name-plain">{{ name }}</span>
</template>
