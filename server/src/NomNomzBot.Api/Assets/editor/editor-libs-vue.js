// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// Strict ambient types for a Vue widget's <script>, as TypeScript declaration text, including the
// <script setup> compiler macros. Shipped as a .js string for the reason given in editor-libs-react.js.
// editor.js reads window.NNZ_EDITOR_LIBS.vue. No `any` appears in the text below: unknown and generics only.
window.NNZ_EDITOR_LIBS = {
    ...window.NNZ_EDITOR_LIBS,
    vue: `
declare module "vue" {
    export interface Ref<T = unknown> {
        value: T;
    }
    export interface ComputedRef<T = unknown> {
        readonly value: T;
    }
    export type MaybeRef<T> = T | Ref<T>;
    export type WatchStopHandle = () => void;
    export interface WatchOptions {
        immediate?: boolean;
        deep?: boolean;
        flush?: "pre" | "post" | "sync";
    }

    export function ref<T>(value: T): Ref<T>;
    export function ref<T = undefined>(): Ref<T | undefined>;
    export function shallowRef<T>(value: T): Ref<T>;
    export function shallowRef<T = undefined>(): Ref<T | undefined>;
    export function reactive<T extends object>(target: T): T;
    export function computed<T>(getter: () => T): ComputedRef<T>;
    export function computed<T>(options: { get: () => T; set: (value: T) => void }): Ref<T>;

    export function watch<T>(
        source: Ref<T> | ComputedRef<T> | (() => T),
        callback: (value: T, previous: T) => void,
        options?: WatchOptions,
    ): WatchStopHandle;
    export function watchEffect(effect: () => void, options?: { flush?: "pre" | "post" | "sync" }): WatchStopHandle;

    export function onMounted(hook: () => void): void;
    export function onBeforeUnmount(hook: () => void): void;
    export function onUnmounted(hook: () => void): void;
    export function nextTick(callback?: () => void): Promise<void>;
    export function defineComponent<T extends object>(options: T): T;
}

declare function defineProps<T extends object>(): Readonly<T>;
declare function defineEmits<T extends object>(): T;
declare function defineExpose(exposed?: Record<string, unknown>): void;
declare function withDefaults<T extends object, D extends Partial<T>>(props: T, defaults: D): T;
`,
};
