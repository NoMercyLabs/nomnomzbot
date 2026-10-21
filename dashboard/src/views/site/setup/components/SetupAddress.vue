<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A web address inside a sentence. An address to open is a plain link; an address to paste somewhere else is
// text with a small copy button beside it; "both" is a link with the copy button, for a link the person may
// open here or take to another browser profile.
import CopyAddressButton from './CopyAddressButton.vue';

defineProps<{
  url: string;
  use: 'open' | 'paste' | 'both';
}>();
</script>

<template>
  <span>
    <a
      v-if="use !== 'paste'"
      :href="url"
      target="_blank"
      rel="noopener noreferrer"
      class="break-all text-foreground underline underline-offset-4"
    >{{ url }}</a>
    <code v-else class="font-mono text-xs break-all text-foreground">{{ url }}</code>
    <CopyAddressButton v-if="use !== 'open'" :url="url" />
  </span>
</template>
