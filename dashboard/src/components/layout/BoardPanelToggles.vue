<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The show/hide switch of every optional panel on the page's board, drawn once by the page header. The board
// that owns the panels registers them (useBoard); a page with no optional panel draws nothing.
import { boardToggles } from './board';
import LabeledSwitch from './LabeledSwitch.vue';
</script>

<template>
  <LabeledSwitch
    v-for="toggle in boardToggles"
    :key="toggle.id"
    :data-board-toggle="toggle.id"
    :label="toggle.label"
    :model-value="toggle.shown"
    @update:model-value="toggle.set"
  />
</template>
