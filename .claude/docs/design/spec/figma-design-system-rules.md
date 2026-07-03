# NomNomzBot — Figma Design System Rules

> Rules for implementing Figma designs in the KMP + Compose Multiplatform dashboard.
> Figma files are a **reference for direction, not a pixel-perfect spec** — the
> shadcn/ui (new-york) design system ported into Kotlin is the single source of truth.

---

## 1. Token System (Colors, Radii)

### Where tokens live

`app/composeApp/src/commonMain/kotlin/bot/nomnomz/dashboard/core/designsystem/theme/`

| File | Purpose |
|------|---------|
| `Tokens.kt` | 28-color `Tokens` data class + `Radii`; `LightTokens`, `DarkTokens`, `withAccent()` |
| `Spacing.kt` | `Spacing` data class — 12 spacing steps |
| `Typography.kt` | `Typography` data class — 8 type steps |
| `NomNomzTheme.kt` | Composition root; provides all four `CompositionLocal`s |
| `color/Oklch.kt` | `fun oklch(l, c, h, a)` OKLCH→sRGB converter |

### Color tokens (28)

```kotlin
data class Tokens(
    val background: Color,        // page background
    val foreground: Color,        // body text on background
    val card: Color,              // card/panel background
    val cardForeground: Color,    // text inside cards
    val popover: Color,           // popovers / dropdowns
    val popoverForeground: Color,
    val primary: Color,           // primary action fill
    val primaryForeground: Color, // text on primary
    val secondary: Color,         // secondary action fill
    val secondaryForeground: Color,
    val muted: Color,             // muted surfaces
    val mutedForeground: Color,   // muted / placeholder text
    val accent: Color,            // dynamic: 15% Twitch chat-color blend
    val accentForeground: Color,
    val destructive: Color,       // red: delete, ban, clear
    val destructiveForeground: Color,
    val border: Color,            // default border
    val input: Color,             // text-field border
    val ring: Color,              // focus ring
    val sidebarBackground: Color,
    val sidebarForeground: Color,
    val sidebarBorder: Color,
    val sidebarAccent: Color,     // hovered / focused nav item background
    val sidebarAccentForeground: Color, // icon + label on sidebarAccent
    val sidebarPrimary: Color,
    val sidebarPrimaryForeground: Color,
    val sidebarRing: Color,
    val radius: Radii,
)
```

Dark scheme key values (OKLCH):

```
background      oklch(0.145 0 0)       — near-black
card            oklch(0.205 0 0)       — card surface
primary         oklch(0.922 0 0)       — bright white
muted           oklch(0.269 0 0)       — subtle surface
mutedForeground oklch(0.708 0 0)       — secondary text
border          oklch(1 0 0 / 10%)     — hairline separator
ring            oklch(0.556 0 0)       — focus ring
sidebarBackground oklch(0.205 0 0)
sidebarAccent   oklch(0.269 0 0)
```

### Radii

```kotlin
data class Radii(val sm: Dp, val md: Dp, val lg: Dp, val xl: Dp)
// sm=6dp  md=8dp  lg=10dp  xl=14dp
```

Use `RoundedCornerShape(tokens.radius.lg)` for cards, dialogs.
Use `tokens.radius.md` for buttons, chips.
Use `tokens.radius.sm` for small elements (badges, tags).

### Dynamic accent

```kotlin
// In NomNomzTheme: Twitch hex chat color → 15% alpha blend onto background
fun Tokens.withAccent(hex: String): Tokens
```

The user's Twitch chat color tints the accent. Never hardcode an accent color.

### Accessing tokens in a Composable

```kotlin
val tokens = LocalTokens.current
val spacing = LocalSpacing.current
val typography = LocalTypography.current
```

**Never** read raw hex (`0xFF...`), `Color(...)` literals, or raw `dp` values inside a Composable.
**Always** read from the four `CompositionLocal`s above.

---

## 2. Spacing

```kotlin
data class Spacing(
    val s0: Dp,    // 0
    val s0_5: Dp,  // 2dp
    val s1: Dp,    // 4dp
    val s1_5: Dp,  // 6dp
    val s2: Dp,    // 8dp
    val s3: Dp,    // 12dp
    val s4: Dp,    // 16dp
    val s6: Dp,    // 24dp
    val s8: Dp,    // 32dp
    val s12: Dp,   // 48dp
    val s16: Dp,   // 64dp
    val s24: Dp,   // 96dp
)
```

### Mapping Figma spacing → Kotlin tokens

| Figma px | Token | dp |
|----------|-------|----|
| 4 | `s1` | 4 |
| 8 | `s2` | 8 |
| 12 | `s3` | 12 |
| 16 | `s4` | 16 |
| 24 | `s6` | 24 |
| 32 | `s8` | 32 |
| 48 | `s12` | 48 |

Column/Row padding: use `spacing.s4` (16dp) for page-level padding, `spacing.s2` (8dp) for card internal padding, `spacing.s1` (4dp) for tight gaps.

---

## 3. Typography

```kotlin
data class Typography(
    val xs:  TextStyle,  // 12sp / 16sp line-height / Normal
    val sm:  TextStyle,  // 14sp / 20sp / Normal
    val base:TextStyle,  // 16sp / 24sp / Normal
    val lg:  TextStyle,  // 18sp / 28sp / Normal
    val xl:  TextStyle,  // 20sp / 28sp / Medium
    val xl2: TextStyle,  // 24sp / 32sp / SemiBold
    val xl3: TextStyle,  // 30sp / 36sp / SemiBold
    val xl4: TextStyle,  // 36sp / 40sp / Bold
)
```

### Mapping Figma type → token

| Use case | Token |
|----------|-------|
| Page title / H1 | `xl2` or `xl3` |
| Section heading | `xl` or `xl2` |
| Card heading | `lg` |
| Body / table rows | `sm` or `base` |
| Muted caption, timestamp | `xs` |
| Stat tile number | `xl4` |
| Badge / chip label | `xs` |
| Button label | `sm` |

```kotlin
Text(text = title, style = typography.xl2, color = tokens.cardForeground)
Text(text = subtitle, style = typography.sm, color = tokens.mutedForeground)
```

---

## 4. Icon System

### Three icon libraries

| Library | File | Purpose | Stroke |
|---------|------|---------|--------|
| CommonGlyphs | `core/designsystem/icon/CommonGlyphs.kt` | Generic action icons (add, edit, trash…) | 1.5px |
| NavGlyphs | `feature/shell/ui/NavGlyphs.kt` | Navigation / feature icons | 1.5px |
| ShellGlyphs | `feature/shell/ui/ShellGlyphs.kt` | Accordion chevrons | 2px |

All icons: 24×24dp viewport, round caps + round joins, transparent fill (stroke-only), Line style.

### Naming convention

`{FeatureName}Glyph` — e.g. `DashboardGlyph`, `AddGlyph`, `TrashGlyph`, `SettingsGlyph`.

### Available CommonGlyphs (action icons)

```
AddGlyph         EditGlyph         EditLineGlyph    TrashGlyph
RemoveGlyph      CheckGlyph        CheckCircleGlyph PowerGlyph
CopyGlyph        PlayCircleGlyph   ArrowUpGlyph     ArrowDownGlyph
RefreshGlyph     DotsHorizontalGlyph DotsVerticalGlyph ChevronDownGlyph
```

### Available NavGlyphs (feature/nav icons)

```
DashboardGlyph   ChatGlyph         CommandsGlyph    EventResponsesGlyph
PipelinesGlyph   TimersGlyph       QuotesGlyph      CodeScriptsGlyph
ModerationGlyph  RewardsGlyph      EconomyGlyph     GamesGlyph
MusicGlyph       SongRequestsGlyph TtsGlyph         WidgetsGlyph
AlertsGlyph      AnalyticsGlyph    CommunityGlyph   DiscordGlyph
IntegrationsGlyph RolesGlyph       FeaturesGlyph    WebhooksGlyph
FederationGlyph  SettingsGlyph     AdminGlyph
```

### Defining a new icon

```kotlin
// In CommonGlyphs.kt
private fun icon(name: String, build: ImageVector.Builder.() -> Unit): ImageVector =
    ImageVector.Builder(name, 24.dp, 24.dp, 24f, 24f).apply(build).build()

val MyFeatureGlyph: ImageVector = icon("MyFeature") {
    strokePath(strokeLineWidth = 1.5f) {
        // SVG path commands
    }
}
```

### Using icons

```kotlin
Icon(
    imageVector = TrashGlyph,
    contentDescription = null,        // use GlyphButton for accessible icon-only buttons
    tint = tokens.destructive,
    modifier = Modifier.size(spacing.s4),
)
```

Tint = `tokens.sidebarAccentForeground` for selected nav items; `tokens.sidebarForeground` for idle.

---

## 5. Component Library

All components live in `core/designsystem/component/`. **Never create one-off styled elements when a component exists.**

### Component catalogue

| Component | File | Purpose |
|-----------|------|---------|
| `PageHeader` | `PageHeader.kt` | Screen title + optional subtitle + trailing slot + divider |
| `AppTextField` | `AppTextField.kt` | Single-line form input, all color slots from tokens |
| `GlyphButton` | `GlyphButton.kt` | Icon-only button with tooltip + accessible touch target |
| `ConfirmDialog` | `ConfirmDialog.kt` | Single dialog for all destructive confirms |
| `ManageGate` | `ManageGate.kt` | Write permission gate; disables controls with a11y reason |
| `ActionErrorBanner` | `ActionErrorBanner.kt` | Full-width error pill (destructive bg) |
| `CopyButton` | `CopyButton.kt` | Read-only value chip + clipboard copy with "copied" feedback |
| `Stepper` | `Stepper.kt` | Multi-step wizard progress indicator |
| `LinkedText` | `LinkedText.kt` | Auto-linked URLs inside body text |

### PageHeader

```kotlin
PageHeader(
    title = stringResource(Res.string.commands_title),
    subtitle = stringResource(Res.string.commands_subtitle),   // optional
    trailing = { /* action buttons */ },                       // optional
)
```

### AppTextField

```kotlin
AppTextField(
    value = name,
    onValueChange = { name = it },
    label = stringResource(Res.string.commands_field_name),
    isError = nameError != null,
    errorText = nameError,
    modifier = Modifier.fillMaxWidth(),
)
```

### GlyphButton

```kotlin
GlyphButton(
    imageVector = TrashGlyph,
    label = stringResource(Res.string.common_delete),  // tooltip + a11y name
    onClick = { showConfirm = true },
    tint = tokens.destructive,
)
```

### ConfirmDialog

```kotlin
if (showConfirm) {
    ConfirmDialog(
        title = stringResource(Res.string.common_confirm_delete_title),
        message = stringResource(Res.string.commands_delete_confirm_message),
        confirmLabel = stringResource(Res.string.common_delete),
        dismissLabel = stringResource(Res.string.common_cancel),
        onConfirm = { viewModel.delete(item.id) },
        onDismiss = { showConfirm = false },
        destructive = true,
    )
}
```

### ManageGate

```kotlin
ManageGate(decision = manage) { enabled ->
    Button(onClick = { viewModel.create() }, enabled = enabled) {
        Text(stringResource(Res.string.common_new))
    }
}
```

Every write control (create / edit / delete / toggle) must be wrapped in `ManageGate`. The `decision` comes from `ManageGate.decide(role, route, action)` in the shell layer.

---

## 6. Project / Feature Structure

```
app/composeApp/src/commonMain/kotlin/bot/nomnomz/dashboard/
├── core/
│   ├── designsystem/
│   │   ├── color/        Oklch.kt
│   │   ├── icon/         CommonGlyphs.kt
│   │   ├── component/    AppTextField.kt, GlyphButton.kt, …
│   │   └── theme/        Tokens.kt, Spacing.kt, Typography.kt, NomNomzTheme.kt
│   ├── network/          DashboardApi.kt, CommandsApi.kt, … (all API DTOs)
│   └── realtime/         HubEvent.kt, HubConnectionManager.kt
├── feature/
│   ├── shell/
│   │   ├── nav/          ShellNav.kt (9 NavGroups, 26 ShellRoutes)
│   │   └── ui/           ShellScreen.kt, NavGlyphs.kt, ShellGlyphs.kt
│   ├── home/ui/          HomeScreen.kt
│   ├── commands/ui/      CommandsScreen.kt
│   ├── chat/ui/          ChatScreen.kt
│   ├── moderation/ui/    ModerationScreen.kt
│   └── …                 one folder per feature
└── App.kt
```

### Feature screen pattern

Every feature screen follows the same structure:

```kotlin
// 1. Controller (state holder)
class CommandsController(private val api: CommandsApi) {
    val state: StateFlow<CommandsState> = …
    fun load() { … }
    fun create(dto: CreateCommandDto) { … }
    // …
}

// 2. State
sealed interface CommandsState {
    data object Loading : CommandsState
    data class Ready(val commands: List<CommandListItem>) : CommandsState
    data class Error(val message: String) : CommandsState
}

// 3. Screen (Composable)
@Composable
fun CommandsScreen(controller: CommandsController) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val state by controller.state.collectAsState()
    // …
}
```

---

## 7. Implementing a Figma Frame

When a Figma frame is provided as a **reference**:

1. **Identify the feature route** in `ShellNav.kt` — which `ShellRoute` owns this screen.
2. **Check the spec** (`frontend-ia.md`, domain spec) — what data does the screen show; what actions does it expose; what permission floor applies.
3. **Check the API** — which `{Feature}Api.kt` methods exist; what DTOs are returned.
4. **Map Figma → tokens** — never copy a Figma hex; find the nearest token. Figma colors are a DIRECTION, tokens are the LAW.
5. **Map Figma spacing** — use the table in §2 above.
6. **Map Figma type** — use the table in §3 above.
7. **Gate writes** — every button/form that creates/updates/deletes must be inside `ManageGate`.
8. **Gate destructive confirms** — every irreversible action routes through `ConfirmDialog`.
9. **Add i18n strings** — add keys to both `values/strings.xml` (en) and `values-nl/strings.xml` (nl). Never hardcode visible text.

### Figma → Compose mapping quick-ref

| Figma element | Compose primitive |
|--------------|-------------------|
| Frame / section | `Column` or `Row` with `Modifier.padding(spacing.s4)` |
| Card | `Card(colors = CardDefaults.cardColors(containerColor = tokens.card))` |
| Divider | `HorizontalDivider(color = tokens.border)` |
| Chip / badge | `Surface(shape = RoundedCornerShape(tokens.radius.sm), color = tokens.muted)` |
| Primary button | `Button(colors = ButtonDefaults.buttonColors(containerColor = tokens.primary))` |
| Secondary button | `OutlinedButton` or `TextButton` |
| Destructive button | `Button(colors = …(containerColor = tokens.destructive))` |
| Text input | `AppTextField(…)` — always, never raw `TextField` |
| Icon button | `GlyphButton(…)` — always, never raw `IconButton` |
| Alert / error | `ActionErrorBanner(…)` |
| Dialog | `AlertDialog` with `containerColor = tokens.card` |
| Confirm dialog | `ConfirmDialog(destructive = true/false)` |

---

## 8. Hub (Real-Time) Events

Screens that show live data subscribe via the `HubEvent` sealed hierarchy:

```kotlin
// In a Screen or Controller
LaunchedEffect(Unit) {
    hubManager.events.collect { event ->
        when (event) {
            is HubEvent.ChatMessage       -> { /* append to feed */ }
            is HubEvent.StreamStatusChanged -> { /* update live badge */ }
            is HubEvent.MusicStateChanged  -> { /* refresh now-playing */ }
            is HubEvent.CommandExecuted   -> { /* increment use count */ }
            is HubEvent.RewardRedeemed    -> { /* add to queue */ }
            is HubEvent.ModAction         -> { /* prepend to mod log */ }
            is HubEvent.ChannelEvent      -> { /* update activity feed */ }
            is HubEvent.AlertTriggered    -> { /* shell-level toast */ }
            is HubEvent.PermissionChanged -> { /* reload role */ }
            is HubEvent.Unknown           -> Unit
        }
    }
}
```

---

## 9. Enforcement Checklist (per screen)

Before committing any new screen or component, verify:

- [ ] Zero raw hex, zero raw `dp`, zero hardcoded strings
- [ ] All colors from `LocalTokens.current`
- [ ] All spacing from `LocalSpacing.current`
- [ ] All type from `LocalTypography.current`
- [ ] Every write control wrapped in `ManageGate`
- [ ] Every destructive action guarded by `ConfirmDialog(destructive = true)`
- [ ] Every icon-only button wrapped in `GlyphButton` (tooltip + a11y name)
- [ ] All user-visible strings in both `strings.xml` (en) and `values-nl/strings.xml` (nl)
- [ ] Screen subscribes to relevant `HubEvent` types if it shows live data
- [ ] AGPL license header at top of every new `.kt` file
