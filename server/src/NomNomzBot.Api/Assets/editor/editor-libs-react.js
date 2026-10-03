// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// Strict ambient types for a React widget, as TypeScript declaration text. Why a .js file and not a .d.ts: the
// editor folder is served by ASP.NET static files, whose content-type map knows ".ts" only as video/mp2t, so a
// .d.ts would reach the browser with the wrong type. A script that assigns a string is served as JavaScript and needs no
// server change. editor.js reads window.NNZ_EDITOR_LIBS.react and adds it to the language service for a react widget.
// No `any` appears in the text below: unknown and generics only.
window.NNZ_EDITOR_LIBS = {
    ...window.NNZ_EDITOR_LIBS,
    react: `
declare module "react" {
    export type Key = string | number;
    export type ReactText = string | number;
    export interface ReactElement<P = unknown> {
        type: unknown;
        props: P;
        key: Key | null;
    }
    export type ReactNode =
        | ReactElement
        | ReactText
        | boolean
        | null
        | undefined
        | Iterable<ReactNode>;
    export type FC<P = Record<string, never>> = (props: P) => ReactElement | null;
    export type FunctionComponent<P = Record<string, never>> = FC<P>;
    export type PropsWithChildren<P = unknown> = P & { children?: ReactNode };

    export type CSSProperties = {
        [K in keyof CSSStyleDeclaration as CSSStyleDeclaration[K] extends string
            ? K extends "length" | "parentRule" ? never : K
            : never]?: string | number;
    } & { [customProperty: \`--\${string}\`]: string | number };

    export type Dispatch<A> = (value: A) => void;
    export type SetStateAction<S> = S | ((previous: S) => S);
    export type EffectCallback = () => void | (() => void);
    export type DependencyList = readonly unknown[];

    export interface MutableRefObject<T> { current: T }
    export interface RefObject<T> { readonly current: T | null }
    export type RefCallback<T> = (instance: T | null) => void;
    export type Ref<T> = RefCallback<T> | RefObject<T> | null;

    export function useState<S>(initial: S | (() => S)): [S, Dispatch<SetStateAction<S>>];
    export function useState<S = undefined>(): [S | undefined, Dispatch<SetStateAction<S | undefined>>];
    export function useEffect(effect: EffectCallback, deps?: DependencyList): void;
    export function useLayoutEffect(effect: EffectCallback, deps?: DependencyList): void;
    export function useRef<T>(initial: T): MutableRefObject<T>;
    export function useRef<T>(initial: T | null): RefObject<T>;
    export function useRef<T = undefined>(): MutableRefObject<T | undefined>;
    export function useMemo<T>(factory: () => T, deps: DependencyList): T;
    export function useCallback<A extends readonly unknown[], R>(
        callback: (...args: A) => R,
        deps: DependencyList,
    ): (...args: A) => R;
    export function useReducer<S, A>(
        reducer: (state: S, action: A) => S,
        initial: S,
    ): [S, Dispatch<A>];
    export function useContext<T>(context: Context<T>): T;

    export interface Context<T> {
        Provider: FC<{ value: T; children?: ReactNode }>;
        Consumer: FC<{ children: (value: T) => ReactNode }>;
    }
    export function createContext<T>(defaultValue: T): Context<T>;

    export const Fragment: FC<{ children?: ReactNode }>;
    export function createElement(
        type: string | FC<never>,
        props?: Record<string, unknown> | null,
        ...children: ReactNode[]
    ): ReactElement;

    // For the "import React from 'react'" form (esModuleInterop).
    const React: {
        useState: typeof useState;
        useEffect: typeof useEffect;
        useLayoutEffect: typeof useLayoutEffect;
        useRef: typeof useRef;
        useMemo: typeof useMemo;
        useCallback: typeof useCallback;
        useReducer: typeof useReducer;
        useContext: typeof useContext;
        createContext: typeof createContext;
        createElement: typeof createElement;
        Fragment: typeof Fragment;
    };
    export default React;
}

declare module "react/jsx-runtime" {
    import type { ReactElement, Key } from "react";
    export const Fragment: unique symbol;
    export function jsx(type: unknown, props: unknown, key?: Key): ReactElement;
    export function jsxs(type: unknown, props: unknown, key?: Key): ReactElement;
}

declare module "react-dom/client" {
    import type { ReactNode } from "react";
    export interface Root {
        render(children: ReactNode): void;
        unmount(): void;
    }
    export function createRoot(container: Element | DocumentFragment): Root;
}

type NnzIfEquals<X, Y, A, B> = (<G>() => G extends X ? 1 : 2) extends <G>() => G extends Y ? 1 : 2
    ? A
    : B;

type NnzWritableKeys<T> = Extract<
    {
        [K in keyof T]-?: NnzIfEquals<
            { [Q in K]: T[K] },
            { -readonly [Q in K]: T[K] },
            T[K] extends (...args: never[]) => unknown ? never : K,
            never
        >;
    }[keyof T],
    keyof T
>;

type NnzReactHandlers<T extends Element> = {
    [K in keyof GlobalEventHandlersEventMap as \`on\${Capitalize<K>}\`]?: (
        event: GlobalEventHandlersEventMap[K] & { currentTarget: T },
    ) => void;
};

type NnzReactCommon<T extends Element> = {
    children?: import("react").ReactNode;
    style?: import("react").CSSProperties;
    key?: import("react").Key | null;
    ref?: import("react").Ref<T>;
};

type NnzHtmlProps<T extends HTMLElement> = Partial<
    Pick<T, Exclude<NnzWritableKeys<T>, \`on\${string}\` | "style" | "children">>
> &
    NnzReactHandlers<T> &
    NnzReactCommon<T>;

type NnzSvgProps<T extends SVGElement> = {
    className?: string;
    id?: string;
    viewBox?: string;
    width?: string | number;
    height?: string | number;
    fill?: string;
    stroke?: string;
    strokeWidth?: string | number;
    strokeLinecap?: "butt" | "round" | "square";
    strokeLinejoin?: "miter" | "round" | "bevel";
    fillRule?: "nonzero" | "evenodd";
    opacity?: string | number;
    transform?: string;
    xmlns?: string;
    preserveAspectRatio?: string;
    d?: string;
    points?: string;
    x?: string | number;
    y?: string | number;
    x1?: string | number;
    y1?: string | number;
    x2?: string | number;
    y2?: string | number;
    cx?: string | number;
    cy?: string | number;
    r?: string | number;
    rx?: string | number;
    ry?: string | number;
} & NnzReactHandlers<T> &
    NnzReactCommon<T>;

type NnzIntrinsicElements = {
    [K in keyof HTMLElementTagNameMap]: NnzHtmlProps<HTMLElementTagNameMap[K]>;
} & {
    [K in keyof SVGElementTagNameMap]: NnzSvgProps<SVGElementTagNameMap[K]>;
};

declare namespace JSX {
    interface Element extends import("react").ReactElement {}
    interface ElementChildrenAttribute {
        children: unknown;
    }
    interface IntrinsicAttributes {
        key?: import("react").Key | null;
    }
    interface IntrinsicElements extends NnzIntrinsicElements {}
}
`,
};
