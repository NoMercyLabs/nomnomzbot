<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The person's own pronouns. A pick saves at once; the shown value is what the bot holds after the save.
import type { AcceptableValue } from 'reka-ui';
import { computed } from 'vue';

import { useMyPronouns, usePronounCatalog, useSetMyPronouns } from '@/api/viewerAccount';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { usePanelState } from '@/composables/usePanelState';

const none = '0';

const catalog = usePronounCatalog();
const mine = useMyPronouns();
const save = useSetMyPronouns();
const state = usePanelState(catalog);

const primary = computed<string>(() => String(mine.data.value?.pronounId ?? none));
const alt = computed<string>(() => String(mine.data.value?.altPronounId ?? none));

function pickPrimary(value: AcceptableValue): void {
  save.mutate({ pronounId: Number(value) });
}

function pickAlt(value: AcceptableValue): void {
  save.mutate({ altPronounId: Number(value) });
}
</script>

<template>
  <PanelCard
    :title="$t('viewer.account.pronouns.title')"
    :description="$t('viewer.account.pronouns.description')"
    :state="state"
    :error-text="$t('viewer.account.pronouns.error')"
    data-viewer-pronouns
    @retry="catalog.refetch()"
  >
    <div class="grid gap-4 sm:grid-cols-2">
      <div class="grid gap-1.5">
        <Label for="viewer-pronoun-primary">{{ $t('viewer.account.pronouns.primary') }}</Label>
        <Select :model-value="primary" @update:model-value="pickPrimary">
          <SelectTrigger id="viewer-pronoun-primary" data-field-primary class="w-full">
            <SelectValue :placeholder="$t('viewer.account.pronouns.pick')" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem :value="none">{{ $t('viewer.account.pronouns.none') }}</SelectItem>
            <SelectItem
              v-for="option in catalog.data.value ?? []"
              :key="option.id"
              :value="String(option.id)"
            >
              {{ option.name }}
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div class="grid gap-1.5">
        <Label for="viewer-pronoun-alt">{{ $t('viewer.account.pronouns.alt') }}</Label>
        <Select :model-value="alt" @update:model-value="pickAlt">
          <SelectTrigger id="viewer-pronoun-alt" data-field-alt class="w-full">
            <SelectValue :placeholder="$t('viewer.account.pronouns.pick')" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem :value="none">{{ $t('viewer.account.pronouns.none') }}</SelectItem>
            <SelectItem
              v-for="option in catalog.data.value ?? []"
              :key="option.id"
              :value="String(option.id)"
            >
              {{ option.name }}
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
    </div>
  </PanelCard>
</template>
