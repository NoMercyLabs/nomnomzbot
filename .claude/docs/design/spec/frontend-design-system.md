# Frontend Design System — the style guide (zero improvisation)

**Status:** Implementable. This is the binding style guide for the NomNomzBot dashboard. Every visual
decision is decided here; a contributor never invents a color, radius, spacing, variant, or component.

**Area:** the design layer of the KMP + Compose Multiplatform dashboard — tokens, theming, the
component catalogue, and the rules that make UI generation **mechanical, not creative**. Sits under
`core/designsystem/`. Companion to `frontend.md` (locked interface spec), `frontend-structure.md`
(placement rulebook), and `frontend-data-layer.md` (query engine).

**Conventions:** `commonMain`-first with full `wasmJs` parity; explicit types; one public type per
file; package == folder path; AGPL header on every `.kt`. Components read **tokens only** — never a
raw hex, `Color(0x…)`, or `.dp` literal (linter-enforced, §8).

---

## 0. Decisions (binding)

- **DS1 — shadcn is the design source of truth.** The previous Figma file is discarded; we do **not**
  treat Figma as canonical. We port **shadcn/ui** to Compose 1:1: its token contract, its component
  set, its variant/size/state model. A fresh Figma may later be minted *from* this spec, never the
  reverse. (Supersedes CLAUDE.md's "Figma is the source of truth" for the dashboard.)
- **DS2 — Style = New York.** shadcn's `default` style is deprecated; we use **new-york** (tighter
  padding, smaller radii, subtler shadows) — the right density for a data dashboard.
- **DS3 — Closed token contract, copied verbatim from shadcn.** The token set in §1 is exactly
  shadcn's (Tailwind v4 / **OKLCH**). No token is added, renamed, or dropped without editing this
  spec. Encoded as one immutable Compose `Tokens` object behind `LocalTokens`.
- **DS4 — Neutral base.** Static tokens use shadcn's published **Neutral** theme (achromatic,
  `oklch(L 0 0)`), generated from shadcn's canonical CSS and committed — never hand-tuned (§1.2).
- **DS5 — The accent is dynamic *and contextual*, derived from the current theme subject's Twitch chat
  color.** The **theme subject** is whoever the screen is about — the signed-in user by default, the
  viewed **broadcaster/viewer** on their page. A deterministic OKLCH function maps the subject's chat
  color onto the accent token family **app-wide** (the whole shell), kept a **subtle delight** by the
  chroma clamp; it **crossfades** on subject change and **reverts to the signed-in user on leave**.
  Pure function, same input → same output, fixed fallback (§2, §3).
- **DS6 — Light + dark, both, now.** Same token names overridden per scheme; runtime switch without
  restart; System / Light / Dark, persisted (§3).
- **DS7 — Component base chosen for correctness, per component.** Each component is built on whichever
  primitive is *most correct* — a themed **Material3** wrapper or raw **Compose Foundation** — and the
  choice is recorded in the catalogue manifest (§4). Visuals are bespoke via tokens; **accessibility
  and interaction correctness (focus, keyboard, semantics) are non-negotiable** and never traded for
  visual purity.
- **DS8 — Variants are data, not conditionals.** Every component exposes shadcn's exact variant/size
  enums; a **pure resolver** `resolve(variant, size, state, tokens) → Style` returns the exact token
  set. No `if (variant == …)` styling at call sites (§4.2).
- **DS9 — Closed catalogue, as-needed growth.** Components live in one folder, named exactly as
  shadcn. New components are added when a screen needs them (Rule of Three over speculative ports),
  each faithful to shadcn's spec. One component = one file (§4).
- **DS10 — Icons are the designer's pack, set-agnostic at the call site.** Components reference a typed
  `IconKey` resolved by an injected `IconSet`. The **delivered designer pack** (4 styles × ~1,574
  24×24 stroke glyphs) is the primary set, **style = Line**; **Lucide** stays a fallback for any
  semantic gap. No component hardcodes an icon path; switching style is one registry line (§7).
- **DS11 — The linter enforces all of the above** (§8): raw color/`dp`, off-catalogue components,
  off-token reads, and hardcoded strings fail the build.

---

## 1. Tokens — the contract

### 1.1 The closed token set (verbatim shadcn, Tailwind v4 / OKLCH)

Every token below exists in **both** `:root` (light) and `.dark`. Encoded as a Compose immutable
holder; components read them through `LocalTokens.current`.

| Group | Tokens | Role |
|---|---|---|
| Surface | `background` · `foreground` | app canvas + default text |
| Card | `card` · `card-foreground` | raised surfaces |
| Popover | `popover` · `popover-foreground` | overlays/menus |
| **Accent family (dynamic, §2)** | `primary` · `primary-foreground` · `ring` | primary actions + focus ring |
| Secondary | `secondary` · `secondary-foreground` | secondary buttons/badges |
| Muted | `muted` · `muted-foreground` | subdued surfaces + hint text |
| Accent (hover) | `accent` · `accent-foreground` | hover/active **surface** (stays neutral) |
| Destructive | `destructive` · `destructive-foreground` | dangerous actions |
| Lines | `border` · `input` | dividers + field outlines |
| Charts | `chart-1` … `chart-5` | data-viz series |
| Sidebar | `sidebar` · `sidebar-foreground` · `sidebar-primary` · `sidebar-primary-foreground` · `sidebar-accent` · `sidebar-accent-foreground` · `sidebar-border` · `sidebar-ring` | left-nav shell |
| Radius | `radius` | base corner radius |

> **Dynamic vs static.** The **accent family** (`primary`, `primary-foreground`, `ring`,
> `sidebar-primary`, `sidebar-primary-foreground`, `sidebar-ring`) is produced by the chat-color
> function in §2. Everything else is **static neutral** (§1.2). The neutral `accent`/`accent-foreground`
> hover tokens deliberately stay neutral so color reads as an accent, not a wash (DS5).

### 1.2 Token values — generated, never transcribed

Static neutral values are **generated** from shadcn's published **Neutral** theme at the version pinned in
`gradle/libs.versions.toml` (key `shadcn`) — the `neutral` block of shadcn's `index.css` at that tag —
by a Gradle task **`generateDesignTokens`** into a committed `TokensNeutral.kt` (`// <auto-generated />`, **never hand-edited**) —
the same "generated from the canonical source, committed, not transcribed" rule the backend uses for
the Helix client. Representative values from shadcn (illustrative, not the authority):
`background: oklch(1 0 0)` (light) / `oklch(0.145 0 0)` (dark); `foreground: oklch(0.145 0 0)` /
`oklch(0.985 0 0)`; `primary: oklch(0.205 0 0)` ← **overridden at runtime by §2**.

OKLCH is not a native Compose color space, so a small pure util `oklch(l, c, h, a) → Color` converts
OKLCH → sRGB once at theme-build (OKLab → linear-sRGB → gamma). It lives in `core/designsystem/color/Oklch.kt` and **pins the exact pipeline** (so two implementers
produce identical colors): **Björn Ottosson's published OKLab↔linear-sRGB matrices** (the canonical
`M1`/`M2` constants), the **standard sRGB piecewise transfer function**
(`x ≤ 0.0031308 ? 12.92·x : 1.055·x^(1/2.4) − 0.055` — *not* a 2.2 approximation), an explicit
**out-of-gamut rule** (after `L`/`C`/`H` are set, if any linear-sRGB channel ∉ `[0,1]`, reduce `C`
toward 0 by bisection to tolerance `ε = 1e-4` until in gamut), and **8-bit channel rounding**.

### 1.3 Radius, spacing, typography — also pinned (no Tailwind to lean on)

shadcn ships only color + `--radius`; Tailwind supplied spacing/type. Since we have no Tailwind, those
scales are pinned here too, so nothing is improvised:

- **Radius:** base `radius = 0.625rem (10dp)`; derived `sm = radius−4`, `md = radius−2`, `lg = radius`,
  `xl = radius+4` (shadcn v4 calc set; offsets in **dp** — sm -4, md -2, lg +0, xl +4). Exposed as `Tokens.radius.{sm,md,lg,xl}`.
- **Spacing:** a fixed 4dp-based scale `Space.{s0,s0_5,s1,s1_5,s2,s3,s4,s6,s8,s12,s16,s24}` (legal Kotlin names; resolved dp: 0,2,4,6,8,12,16,24,32,48,64,96) (Tailwind's numbers
  ×4dp). No raw `.dp` in components — only `Space.*`.
- **Typography:** a fixed type scale (`xs 12/16`, `sm 14/20`, `base 16/24`, `lg 18/28`, `xl 20/28`,
  `2xl 24/32`, `3xl 30/36`, `4xl 36/40` — size & line-height both in `sp`, letter-spacing `0`, default weight `normal 400`) with weights `{normal 400, medium 500,
  semibold 600, bold 700}`. **Font = Inter** (bundled, open-source, wasm-safe) as the `FontFamily`
  token — swapped in one place only if the designer ships a brand font. Exposed as `Typography.*`; no
  inline `TextStyle`.

---

## 2. Dynamic accent — the chat-color algorithm

The signature feature: the app quietly wears the **current theme subject's Twitch chat color** (§3) —
your own by default, a broadcaster's or viewer's on their page. Deterministic, accessible, subtle.
The function itself is **subject-agnostic** — it maps a hex to tokens; §3 decides whose hex feeds it.

```
accentFor(chatColorHex: String?, scheme: Scheme): AccentTokens
```

**Inputs.** The active theme subject's Twitch name color (Helix `GET chat/color`, resolved by user id
through a cached `useChatColor(userId)` query, §3) as a hex; `null` when the subject has no color set
(the §3 fallback chain handles it).

**Algorithm (pure, deterministic):**
1. Parse hex → sRGB → **OKLCH** `(L, C, H)`. Accept exactly `#?[0-9a-fA-F]{6}`
   (case-insensitive, optional leading `#`, trimmed); anything else (incl. `null`, empty, `#RGB` short
   form) is a **parse-fail**, resolved **upstream** by §3.1's fallback chain to a non-null hex *before*
   this function runs — `accentFor`'s own last-resort guard on a still-bad input is the Twitch-purple
   seed `#9146FF` (DS5 fallback).
2. **Subtlety clamp:** `C' = min(C, C_MAX)` — chroma ceiling keeps it elegant, never neon.
3. **Role + scheme lightness (fixed constants, not derived from input):** set `L` per token/scheme
   from the constants table so brand hue varies but contrast/brightness never wanders:

   Output hue **`H` = the input's `H`, unchanged** (the fallback path uses the hue of `#9146FF`); `H` is
   never clamped or quantized.

   | Token | Light `L` | Dark `L` | Chroma |
   |---|---|---|---|
   | `primary` | `L_PRIMARY_LIGHT` | `L_PRIMARY_DARK` | `C'` |
   | `ring` · `sidebar-ring` | `L_RING_LIGHT` | `L_RING_DARK` | `C'` |
   | `sidebar-primary` | = primary | = primary | `C'` |

4. **Foregrounds by contrast, not by guess:** the two candidates are **`oklch(0.985 0 0)`** (near-white)
   and **`oklch(0.205 0 0)`** (near-black). Compute the WCAG 2.x contrast ratio `(L1+0.05)/(L2+0.05)`
   (sRGB relative luminance) of each against the resolved `primary`; pick the side that clears
   **≥ 4.5:1**. If **both** clear → prefer near-white; if **neither** does (possible after the clamps) →
   pick the **higher-contrast** side. `primary-foreground` and `sidebar-primary-foreground` take this result.
5. Emit `AccentTokens`; the theme overlays them onto the §1 neutral set.

**Constants — locked defaults** (one table in `AccentConstants.kt`; the *algorithm* is fixed and these
are the canonical values — adjusting a number in that one file is a tuning, not a design change):
`C_MAX = 0.12`, `L_PRIMARY_LIGHT = 0.55`, `L_PRIMARY_DARK = 0.62`, `L_RING_LIGHT = 0.55`,
`L_RING_DARK = 0.62`, fallback seed `#9146FF`, WCAG contrast floor `4.5`.

**Determinism guarantees:** no randomness, no time; identical `(hex, scheme)` → identical tokens; the
function is unit-tested with a fixed table of input colors → expected OKLCH outputs and a contrast
assertion (the foreground always clears the floor).

**Recompute trigger:** the theme recomputes when **`ThemeSubjectStore.subject` changes, when the resolved subject color (`useChatColor`) settles, or when the scheme changes**
— a recomposition, not a restart.

---

## 3. Theming, the theme subject & scheme switching

- **`NomNomzTheme { content }`** is the single theme root. It builds the active `Tokens` from
  `(scheme, accentFor(subjectColor, scheme))` and provides `LocalTokens`, `LocalScheme`,
  `LocalSpacing`, `LocalTypography`. Screens never build a theme — the **whole app** wears one accent
  at a time.

### 3.1 The theme subject (contextual accent)

The accent's source is the **active theme subject** — whoever the current screen is about. Held by a
`ThemeSubjectStore` (global Koin `Store` singleton, `core/designsystem/theme/`) exposing
`val subject: StateFlow<String?>` (the subject user id; `null` = default), `fun push(userId)`, and
`fun clearIf(userId)`. The signed-in user id (the default subject + the fallback rung) comes from
`SessionStore.userId` (`core/connection`):

- **Default subject = the signed-in user.** Your dashboard, settings, and lists wear your color.
- **A subject-bearing screen overrides it.** A broadcaster page / viewer profile uses the
  `ThemeSubject(userId)` composable effect: on enter it `push(userId)`; on dispose it
  **`clearIf(userId)`** — resets only if the current subject still equals the one it set (avoids the
  enter-B-before-dispose-A nav race) → the
  accent **reverts to the signed-in user**. **One subject app-wide at a time** — a list of users does
  **not** per-item theme; opening an individual profile is what sets the subject.
- **Resolution.** `useChatColor(userId): Query<String?>` is a thin query hook (lives in
  `feature/community/data/` — it backs profiles) keyed `QueryKey.of("chat-color", userId)` with
  `QueryDefaults.static`, fetching Helix `GET chat/color` via the channel facade; it rides the
  QueryClient (`frontend-data-layer.md`), so revisiting a profile is instant.
- **Fallback chain** when a subject has no color: → the **signed-in user's** color → the Twitch-purple
  seed (§2). Never an abrupt neutral.
- **Whole-app scope, kept subtle.** The subject color flows into the accent family **including the
  sidebar accent tokens** (`primary`, `ring`, `sidebar-primary`, `sidebar-ring`); neutral surface
  tokens are unchanged (no surface tint — neutral surfaces stay neutral). The chroma clamp (§2) keeps even an app-wide reskin a *subtle delight*, not a flood.
- **Crossfade.** On subject change the accent token **colors animate** (`animateColorAsState`,
  **300 ms `FastOutSlowInEasing`** — one constant) so the app gently shifts hue; **reduced-motion → instant snap**
  (accessibility) — detected via an `expect fun prefersReducedMotion(): Boolean` seam (web:
  `matchMedia('(prefers-reduced-motion)')`; desktop: OS query, default `false`) in `core/designsystem`.
  The `*-foreground` tokens **snap** rather than fade (avoids low-contrast mid-colors).

### 3.2 Scheme switching

- **Scheme source:** `System` (follows OS) by default, overridable to `Light`/`Dark`; persisted in app
  prefs, applied at runtime (recompose, no restart) — verified on desktop **and** web (no
  `Locale.setDefault`-style global mutation; wasm-safe).
- **Material3 interop (DS7 wrapped components):** the theme derives an M3 `ColorScheme` from our tokens
  and wraps content in `MaterialTheme` so Material-based components inherit our tokens. Feature code
  still reads `LocalTokens`, never `MaterialTheme.colorScheme` directly (§8).

---

## 4. Component catalogue & taxonomy

### 4.1 Placement & faithfulness

- One component = **one file**, `core/designsystem/component/<Name>.kt`, **named exactly as shadcn**
  (`Button`, `Select`, `Dialog`, …). AGPL header, explicit types, one public composable + its enums.
- Each component mirrors shadcn's **variants, sizes, and states** verbatim. The catalogue manifest
  (`catalogue.md` beside the components, also the linter's input) is the closed list:

| Component | Variants | Sizes | States | Base (DS7) |
|---|---|---|---|---|
| Button | default · destructive · outline · secondary · ghost · link | sm · default · lg · icon | default · hover · focus-visible · active · disabled · loading | Foundation |
| Input | (single) | sm · default · lg | default · focus · disabled · invalid | Foundation (`BasicTextField`) |
| Checkbox · Switch · RadioGroup | (single) | default | default · checked · focus · disabled | M3-wrapped (a11y) |
| Select · DropdownMenu · Combobox¹ | (single) | default | open · focus · disabled | M3-wrapped (menu semantics) |
| Dialog · Sheet · Popover · Tooltip | (single) | — | open · closed | Foundation + `Popup`/`Dialog` |
| Card · Badge · Alert · Separator · Skeleton · Avatar · Progress | shadcn's | — | — | Foundation |
| Tabs · Table · ScrollArea · Slider · Toast² | shadcn's | — | per shadcn | mixed (recorded per row) |

> **`catalogue.md` is the authority** (ships with this spec as `frontend-design-system.catalogue.md`);
> the table above is a readable summary — the full per-component variant/size/state/base enumeration is
> in `catalogue.md`. ¹ `Combobox` = `Popover` + `Command` composite (modeled on shadcn — no 1:1
> primitive). ² `Toast` is modeled on shadcn's Sonner (no JS lib ported).
>
> **No ambiguity in the shorthand.** "shadcn's" = that component's **exact** published variant/size set
> in shadcn (a deterministic external source, transcribed verbatim, version-pinned); a base of
> "mixed / recorded per row" means the DS7 base (Foundation vs M3-wrapped) is **decided and written into
> `catalogue.md` before the component is built** — the linter rejects a component whose row is
> incomplete. This is the **as-needed first batch** (covers setup, dashboard, commands, pipeline,
> community, moderation, rewards, timers, widgets, integrations, settings); rows are added — never
> invented ad hoc in a screen — and each grouped component gets its own fully-filled row on creation.

### 4.2 Variants as data (the CVA equivalent)

```kotlin
enum class ButtonVariant { Default, Destructive, Outline, Secondary, Ghost, Link }
enum class ButtonSize { Sm, Default, Lg, Icon }

// Pure lookup — variant/size/state in, exact token set out. No call-site conditionals.
internal object ButtonStyles {
    fun resolve(variant: ButtonVariant, size: ButtonSize, state: InteractionState, tokens: Tokens): ButtonStyle
}
```

`InteractionState` is the closed set derived from Compose's `InteractionSource` plus component flags —
`data class InteractionState(val hovered: Boolean = false, val focused: Boolean = false, val pressed: Boolean = false, val enabled: Boolean = true, val loading: Boolean = false, val invalid: Boolean = false, val selected: Boolean = false, val open: Boolean = false)`; a component passes only the flags it has. `ButtonStyle` (and each component's `*Style`) is a `data class` of resolved values (container/content/border colors, radius, padding, height).

The component composable consumes `ButtonStyle` and renders; **all** styling decisions live in the
resolver, so two call sites with the same `(variant, size)` are pixel-identical by construction.

### 4.3 Component contract (every component obeys)

- Stateless and token-driven: no data access, no Store/QueryClient reads, no business logic.
- Data in via params, events out via lambdas (UDF). A `Modifier` parameter, last, defaulted.
- Exposes only shadcn's surface; no bonus props "just in case" (YAGNI).

---

## 5. Composition layering — components vs composables vs screens

Three tiers, one home each — the reuse/maintainability backbone the rest of the app builds on:

| Tier | Home | Knows about | Reused |
|---|---|---|---|
| **Primitive** (the catalogue) | `core/designsystem/component/` | tokens only | everywhere, all targets |
| **Pattern** (composites) | `core/designsystem/pattern/` | primitives + tokens | across features (e.g. `FormField` = Label+Input+error, `DataTable`, `EmptyState`) |
| **Feature composable** (screens/sections) | `feature/<x>/ui/` | patterns + primitives + **data** (query hooks, Stores, state-holders) | within its feature |

**Rules:** primitives and patterns **never** touch data — they receive it as parameters and raise
events as lambdas. Only feature composables (or their state-holders, per `frontend.md`'s no-ViewModel
model) read server state (`useQuery`) / global state (Stores) / local state (`remember`). This is what
makes the entire primitive + pattern layer **identical on desktop, web, and mobile**.

---

## 6. Responsive / multi-form-factor (the mobile-reuse guarantee)

- Primitives and patterns carry **no** form-factor assumptions (no fixed widths, no "phone vs
  desktop" branches). They size to their constraints.
- Screens branch layout on a **`WindowSizeClass`** with fixed width breakpoints — **Compact `< 600 dp`,
  Medium `600–839 dp`, Expanded `≥ 840 dp`** (Material's standard) — provided once at the shell. A
  screen picks a layout per size class; the components inside are unchanged.
- Consequence: shipping mobile later reuses 100% of the primitive + pattern layer and only adds
  `Compact` layouts to screens — no component rewrite. This is the "reusable for mobile" promise made
  concrete.

---

## 7. Icons

**The pack (delivered).** The designer's pack — `4 styles × 1,574 glyphs each (≈6,300 SVGs; the committed pack's file list is the exact authority — the `IconKey` enum is generated from it), all **24×24,
stroke 1.5, round caps/joins** (lucide/shadcn-class), across 59 semantic categories (`System`, `UI`,
`Arrows`, `User`, `Chat`, `Code`, `Charts`, `Player`, `Music`, `Emotes`, …) with kebab names
(`user-add`, `calendar-trash`). Source asset is committed at `app/composeApp/icons/svg/` (relocated from the delivered
`.claude/docs/design/icons.zip`); the `generateIcons` Gradle task reads it.

- **Style = Line** (the lucide/shadcn match). The four styles share one `IconKey` namespace, so
  switching to Solid/Duo/Sharp is a single line in the active `IconSet` — no call-site change. **All four
  styles expose the identical key set** — the `generateIcons` task validates parity and red-builds on a
  mismatch; any semantic gap falls through to the Lucide fallback.
- **Build-time pipeline (deterministic, committed) — the decided mechanism.** A Gradle codegen step
  converts the chosen style's SVGs → Compose **`ImageVector`**s into `core/designsystem/icon/generated/`
  (`// <auto-generated />`, never hand-edited), and **generates the `IconKey` enum from the pack's file
  list** — typo-proof and exhaustive. Codegen is the path on every target; CMP-native SVG resources are
  **not** relied upon.
- **Tinting.** Components render via the design-system `Icon(key, tint = <token>)`; the tint is applied
  as a render-time color filter, so the SVGs' **baked stroke color is overridden** — no per-file edits.
  Default tint is `foreground`; `muted-foreground` for de-emphasis; `primary` (the dynamic accent) only
  on active/selected affordances. Duo's two-path opacity renders as a one-color two-tone (the tint plus the SVG's per-path opacity);
  mapping its two layers to separate tokens is **out of scope**.
- **Resolution & fallback.** `IconSet.vector(key: IconKey): ImageVector`; the primary (designer/Line)
  set is composed over a **`LucideIconSet`** fallback so any unmapped key still resolves. **The fallback
  is built by running Lucide's open-source SVGs through the *same* codegen** (Lucide is 24×24 stroke —
  identical pipeline), so it needs **no runtime dependency** and is wasm-safe by construction. (The
  ready-made `com.composables:icons-lucide` CMP artifact is the off-the-shelf alternative; the codegen
  path is the decided one, per minimize-deps.) Components reference `IconKey` only — never a raw
  drawable or path.

---

## 8. Linting — the rule is enforced, not just written

A detekt ruleset (+ CI gate), mirroring the backend `taxonomy-linter`, fails the build on:

- a raw color (`Color(0x…)`, hex string) or raw `.dp`/`.sp` literal inside
  `core/designsystem/component/`, `core/designsystem/pattern/`, or `feature/**` — must use
  `LocalTokens` / `Space.*` / `Typography.*` / `radius.*`;
- a `@Composable` whose name collides with a `catalogue.md` entry but is declared **outside**
  `core/designsystem/component/`, **or** a reusable styled control under `feature/**` that reads
  `LocalTokens` yet is neither a screen nor a section (it belongs in the catalogue);
- a direct `MaterialTheme.colorScheme.*` / `MaterialTheme.typography.*` read in feature code;
- a **string literal passed as the `text` arg of `Text`/`BasicText`, or as a `contentDescription`/label, not wrapped in `stringResource(...)`** (the one enforceable definition — shared with the structure linter);
- an icon referenced by raw drawable/path instead of `IconKey`.

---

## 9. Decisions (resolved)

All settled and binding:
- shadcn (new-york) is the design source of truth; Figma is not canonical (DS1, DS2).
- Closed OKLCH token contract copied verbatim from shadcn; neutral base generated + committed, never
  transcribed (DS3, DS4).
- Accent is a pure, deterministic, accessible function of the **current theme subject's** chat color,
  applied subtly **app-wide**, crossfading on subject change and reverting to the signed-in user on
  leave; fallback chain (DS5, §2, §3.1).
- Light + dark, runtime switch, System default (DS6, §3).
- Per-component base chosen for correctness and recorded; a11y/interaction non-negotiable (DS7).
- Variants are data via pure resolvers (DS8); closed catalogue, as-needed growth (DS9).
- Three-tier composition (primitive → pattern → feature); data only in the feature tier (§5).
- Form-factor-agnostic primitives + `WindowSizeClass` screens = mobile reuse (§6).
- Set-agnostic icons; designer pack primary, Lucide fallback (DS10, §7).
- Everything enforced by a detekt ruleset + CI gate (DS11, §8).
