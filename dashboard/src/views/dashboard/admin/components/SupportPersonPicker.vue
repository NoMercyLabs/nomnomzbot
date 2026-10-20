<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Picks one person on the platform by name. The desk reads are audited, so the search waits for the reason.
// The operator never types an id: the pick carries the id.
import Cross from '~icons/nnz/cross';
import { computed, ref } from 'vue';

import { useSupportPeople, type SupportPerson } from '@/api/adminSupport';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';

const props = defineProps<{
  person: SupportPerson | null;
  inputId: string;
  reason: string;
  waitHint: string;
}>();

const emit = defineEmits<{ pick: [person: SupportPerson | null] }>();

const search = ref('');
const found = useSupportPeople(search, () => props.reason);
const waiting = computed(() => props.reason.trim() === '');
const options = computed<SupportPerson[]>(() => found.data.value?.data ?? []);
const asking = computed(() => search.value.trim() !== '' && !waiting.value);

function choose(person: SupportPerson): void {
  search.value = '';
  emit('pick', person);
}
</script>

<template>
  <div class="relative">
    <div
      v-if="person"
      data-person-picked
      class="flex min-h-9 items-center justify-between gap-2 rounded-md border border-input px-3 text-sm"
    >
      <span class="truncate">{{ person.displayName }}</span>
      <Button
        variant="ghost"
        size="icon"
        data-person-clear
        :aria-label="$t('dashboard.admin.person.clear')"
        @click="emit('pick', null)"
      >
        <Cross class="size-5" />
      </Button>
    </div>
    <template v-else>
      <Input
        :id="inputId"
        v-model="search"
        data-person-search
        autocomplete="off"
        :disabled="waiting"
        :placeholder="$t('dashboard.admin.person.placeholder')"
      />
      <p v-if="waiting" class="mt-1.5 text-sm text-muted-foreground">{{ waitHint }}</p>
      <p v-else-if="found.isError.value" role="alert" class="mt-1.5 text-sm text-destructive">
        {{ $t('dashboard.admin.person.failed') }}
      </p>
      <ul
        v-else-if="asking && options.length > 0"
        class="absolute z-10 mt-1 max-h-56 w-full overflow-auto rounded-md border bg-popover p-1 text-popover-foreground shadow-md"
      >
        <li v-for="option in options" :key="option.userId">
          <button
            type="button"
            data-person-option
            class="flex w-full flex-col rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
            @click="choose(option)"
          >
            <span>{{ option.displayName }}</span>
            <span class="text-xs text-muted-foreground">{{ option.username }}</span>
          </button>
        </li>
      </ul>
      <p
        v-else-if="asking && found.isSuccess.value"
        class="mt-1.5 text-sm text-muted-foreground"
      >
        {{ $t('dashboard.admin.person.none') }}
      </p>
    </template>
  </div>
</template>
