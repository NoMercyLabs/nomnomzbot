<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { siKick, siTwitch, siX, siYoutube, type SimpleIcon } from 'simple-icons'
import { computed } from 'vue'

// mono: on a filled button the brand color fights the button color, so the logo takes the text color.
const props = defineProps<{ platform: string; mono?: boolean }>()

// 'twitter' is the key the bot sends in /auth/providers; 'x' is the name the pages use.
const logos: Readonly<Record<string, SimpleIcon>> = {
  twitch: siTwitch,
  kick: siKick,
  youtube: siYoutube,
  twitter: siX,
  x: siX,
}
const logo = computed(() => logos[props.platform])

// A black or white brand would vanish on one of the themes, so it takes the text color.
const fill = computed(() => {
  const hex = logo.value?.hex ?? '000000'

  return props.mono || hex === '000000' || hex === 'FFFFFF'
    ? 'currentColor'
    : `#${hex}`
})
</script>

<template>
  <!-- The text beside it names the platform, so the logo is decoration for screen readers. -->
  <svg v-if="logo" viewBox="0 0 24 24" :fill="fill" aria-hidden="true">
    <path :d="logo.path" />
  </svg>
</template>
