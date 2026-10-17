<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The panel of a random-reply command: create a list by editing the lines here, or choose the
// list of another random-reply command to start from. A chosen list is copied, never shared.
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';

import RandomReplyList from './RandomReplyList.vue';

export interface ReplySet {
  name: string;
  replies: string[];
}

const replies = defineModel<string[]>({ required: true });

defineProps<{ sets: ReplySet[] }>();

function choose(set: ReplySet): void {
  replies.value = [...set.replies];
}
</script>

<template>
  <PanelCard data-random-panel pad :title="$t('dashboard.commands.editor.randomPanel')">
    <div class="grid gap-4">
      <p class="text-sm text-muted-foreground">{{ $t("dashboard.commands.editor.randomHelp") }}</p>
      <RandomReplyList v-model="replies" />
      <div v-if="sets.length > 0" data-reply-sets class="grid gap-2 border-t border-border pt-4">
        <h4 class="text-sm font-medium">{{ $t("dashboard.commands.editor.chooseSet") }}</h4>
        <div class="flex flex-wrap gap-2">
          <Button
            v-for="set in sets"
            :key="set.name"
            type="button"
            variant="outline"
            size="sm"
            @click="choose(set)"
          >
            {{ $t("dashboard.commands.editor.useSet", { name: set.name }) }}
          </Button>
        </div>
      </div>
    </div>
  </PanelCard>
</template>
