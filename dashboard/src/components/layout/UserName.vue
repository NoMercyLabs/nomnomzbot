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
import { useProfileDialogStore } from '@/stores/profileDialog'

const props = defineProps<{ userId: string; name: string }>()

const profile = useProfileDialogStore()

function open(event: MouseEvent): void {
  profile.open(
    props.userId,
    event.currentTarget instanceof HTMLElement
      ? event.currentTarget
      : null,
  )
}
</script>

<template>
  <button
    type="button"
    data-slot="user-name"
    :data-user="userId"
    :aria-label="$t('profile.open', { name })"
    class="cursor-pointer rounded-sm font-medium underline-offset-4 hover:underline focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:outline-none"
    @click="open"
  >
    {{ name }}
  </button>
</template>
