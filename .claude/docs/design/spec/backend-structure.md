# Backend Structure — the placement rulebook

**Area:** where every backend artifact lives, how it is discovered, and the line between the protected engine and editable content. There is exactly **one** home for each thing — never "it could also go here." This is the rulebook the cleanup fleet and every contributor follow.

**Applies to:** `server/src` (the five .NET projects: the four layers plus `NomNomzBot.Migrations.Sqlite`). Frontend layout is owned by `frontend-structure.md`.

**Conventions:** file-scoped namespaces; `Nullable` enabled; async all the way; `Result<T>` over exceptions/null; one public type per file; **namespace == folder path**; AGPL license header on every file. Namespace root is `NomNomzBot.*` everywhere.

---

## 0. Decisions (binding)

- **D1 — Project = layer.** `Domain ← Application ← Infrastructure ← Api`; dependencies point **inward only**. Domain references nothing outward.
- **D2 — Module-first inside every layer.** Organize by domain module — `<Module>/<ArtifactType>/` — never by provenance. **Banned folder names:** `General`, `Application` (as a folder under Infrastructure), `Misc`, `Helpers`, `Utils`, `Common` dumping-grounds, `Stubs`, `Generated`.
- **D3 — Closed artifact taxonomy (§2).** Every file is one of the listed artifact types; each type has exactly one folder pattern and one base type/marker. Learn the table once → you always know where a thing goes and where to find it.
- **D4 — Engine vs feature, by location.** `Infrastructure/Platform/` is the engine (internal, rarely changes); the named domain modules are user-facing. The split is legible from the path — **no markers, no annotations, no separate assembly**.
- **D5 — Auto-discovery.** Every pluggable artifact self-registers by **assembly scan** at startup. No manual DI list, no `switch`, no hand-maintained registry.
- **D6 — Content is data.** Shipped-but-editable defaults (built-in commands, event responses, platform-content templates, first-party widgets, reference catalogues) live in `Infrastructure/Content/` as `ISeeder` definitions, never hard-coded in the engine.
- **D7 — One pipeline.** Pipeline **contracts** in `Application/Abstractions/Pipeline/`; the **single** `PipelineEngine` and **every** concrete action in Infrastructure. The current duplicate engine + two-layer action split is deleted.
- **D8 — Scaffolds, not Roslyn.** `dotnet new` templates (one per artifact type) drop a new file in its correct home with header + base type pre-filled.

---

## 1. The five projects

| Project | Holds | Depends on |
|---|---|---|
| `NomNomzBot.Domain` | Entities, value objects, domain events, enums, domain-owned interfaces. No external references. | — |
| `NomNomzBot.Application` | Service interfaces, DTOs, abstraction contracts (pipeline, eventing). Use-case orchestration. | Domain |
| `NomNomzBot.Infrastructure` | Engine (`Platform/`) + feature module implementations + `Content/`. EF, Twitch, transports, jobs, projections. | Application, Domain |
| `NomNomzBot.Api` | Controllers, hubs, middleware, request/response contracts. | Infrastructure, Application, Migrations.Sqlite |
| `NomNomzBot.Migrations.Sqlite` | The **SQLite** EF migration set (`self_host_lite`): `Migrations/` + `SqliteDesignTimeDbContextFactory`. It is a separate assembly so the Postgres set and the SQLite set never share a snapshot. | Infrastructure |

**Two migration sets, always both.** The PostgreSQL set lives in `Infrastructure/Platform/Persistence/Migrations/`; the SQLite set lives in `NomNomzBot.Migrations.Sqlite/Migrations/`. Every model change lands in **both** — a SQLite-only migration passes local tests and breaks `self_host_full`/`saas`. Prove it with `scripts/migration-check.ps1`; the rule and the exact `dotnet ef` commands are in `.claude/docs/reference/backend-architecture.md` (*Migrations (two sets)*), and `PendingModelChangesGuardTests` guards the Postgres set.

---

## 2. The artifact taxonomy

Every `.cs` file is one of these. `Folder` is relative to its project; `<Module>` is a domain module from §3.

| Artifact | Layer | Folder | Base type / marker | Auto-discovered |
|---|---|---|---|---|
| Entity | Domain | `<Module>/Entities/` | `BaseEntity` | no (EF maps) |
| Value object | Domain | `<Module>/ValueObjects/` | `readonly record struct` | no |
| Enum | Domain | `<Module>/Enums/` | `enum` | no |
| Domain event | Domain | `<Module>/Events/` | `DomainEventBase` | via bus |
| Domain interface | Domain | `<Module>/Interfaces/` | `I…` (repo/abstraction the domain owns) | no |
| Service interface | Application | `<Module>/Services/` | `I<X>Service` | no |
| DTO | Application | `<Module>/Dtos/` | `record` | no |
| Abstraction contract | Application | `Abstractions/<area>/` | interface (e.g. `ICommandAction`) | no |
| Service impl | Infrastructure | `<Module>/` | `: I<X>Service` | **yes** (by interface) |
| Repository | Infrastructure | `<Module>/Persistence/` | `: I<X>Repository` | **yes** |
| EF configuration | Infrastructure | `<Module>/Persistence/` | `IEntityTypeConfiguration<T>` | **yes** (scan) |
| Bot command | Infrastructure | `<Module>/Commands/` | `IBotCommand` | **yes** |
| Pipeline action | Infrastructure | `<Module>/PipelineActions/` (core → `Platform/Pipeline/CoreActions/`) | `ICommandAction` | **yes** |
| Condition evaluator | Infrastructure | `<Module>/Conditions/` (core → `Platform/Pipeline/`) | `IConditionEvaluator` | **yes** |
| Event handler | Infrastructure | `<Module>/EventHandlers/` | `IDomainEventHandler<T>` | **yes** |
| Background job | Infrastructure | `<Module>/Jobs/` | `IJob` / `IHostedService` | **yes** |
| Projection | Infrastructure | `<Module>/Projections/` | `IProjection` | **yes** |
| Seeder / content pack | Infrastructure | `Content/<Module>/` | `ISeeder` | **yes** |
| Migration (PostgreSQL) | Infrastructure | `Platform/Persistence/Migrations/` | EF migration | no |
| Migration (SQLite) | Migrations.Sqlite | `Migrations/` | EF migration | no |
| Controller | Api | `Controllers/V1/` (grouped by module) | `ControllerBase` | MVC |
| Hub | Api | `Hubs/` | `Hub` | no |
| Hub broadcaster | Api | `Hubs/Broadcasters/` | `IDomainEventHandler<T>` (transport) | **yes** |
| Middleware | Api | `Middleware/` | `IMiddleware` | pipeline order |

> A **domain event handler** (reacts to a fact, in Infrastructure) is distinct from a **hub broadcaster** (pushes a fact to SignalR clients, in Api). Both subscribe to events; they live in different layers because one is logic and one is transport.

> **The cache-invalidation broadcaster (frontend-cache coherence).** One canonical hub broadcaster, `CacheInvalidationBroadcaster` (`Api/Hubs/Broadcasters/`), subscribes to mutation / projection-completed domain events and pushes `Invalidate{ key: string[], exact: bool }` to the affected channel's `DashboardHub` group. The `key` is the **frontend `QueryKey` vocabulary** — controller-route-aligned (`["commands","list",channelId]`, …), the same keys the dashboard's `<x>Keys` factories produce — so the client invalidates exactly the cache entry that changed (the consuming `QueryInvalidationBridge` is `frontend-data-layer.md` §8). It owns no domain logic; a cross-cutting test asserts the emitted keys and the client's `<x>Keys` agree. This is the one broadcaster with a cross-module contract — the rest are per-module.

---

## 3. Module map

**Modules (user-facing):** `Chat` · `Commands` · `Rewards` · `Music` · `Economy` · `Moderation` · `Analytics` · `Tts` · `Discord` · `Widgets` · `Stream` · `Integrations` · `Identity` · `Community` · `Dashboard`.
**Engine (internal):** `Platform`.
> `Community` and `Dashboard` are thin **read-only aggregator** modules — they own no schema or events; each is a typed service layer (`ICommunityService` / `IDashboardService`) over read models owned by other modules (see `community-dashboard.md`).

Every module repeats the same shape across layers. Worked example — **Rewards**:

```text
Domain/Rewards/            Entities/  ValueObjects/  Events/  Enums/  Interfaces/
Application/Rewards/       Services/(IRewardService.cs)  Dtos/
Infrastructure/Rewards/    RewardService.cs
                           Commands/  PipelineActions/  EventHandlers/  Jobs/  Projections/
                           Persistence/(RewardConfiguration.cs, RewardRepository.cs)
Api/Controllers/V1/        RewardsController.cs
```

The engine and the editable content sit in their own homes — you can tell at a glance which is which:

```text
Infrastructure/Platform/   Pipeline/  Eventing/  Transport/  Scheduling/
                           RateLimiting/  Auth/  Persistence/(DbContext, base repo, interceptors, Migrations/)
Infrastructure/Content/    Billing/  Commands/  Identity/  Music/  Platform/  PlatformContent/  Tts/  Widgets/
                           (ISeeder content packs + backfills, plus the platform-content engine — see §5)
```

`Application/Abstractions/` holds cross-layer contracts (`Pipeline/`, `Eventing/`) that both Application and Infrastructure depend on.

`Domain/Platform/` is the Domain layer's **engine area + shared kernel** — it mirrors `Infrastructure/Platform/`. It holds the base types (`BaseEntity`, `DomainEventBase`), core markers (`IDomainEvent`, `IEventBus`, `IEventHandler`), and engine-grade entities that belong to no feature module (`Configuration`, `Storage`, `Service`, `EventSubscription`, `ChannelFeature`, `DeletionAuditLog`, `Record`) under `Entities/` `Enums/` `Interfaces/`. There is **no** `Domain/Common/` dumping-ground; the shared kernel lives in `Domain/Platform/`.

---

## 4. Auto-discovery (D5)

No file is ever added to a DI list by hand. There are **two installers**, both called from `Api/Program.cs`: `AddApplication` (`Application/DependencyInjection.cs` — the FluentValidation validator scan only, because Application holds contracts) and `AddInfrastructure` (`Infrastructure/DependencyInjection.cs` — every concrete artifact). There is no `AddDomain` (Domain registers nothing), no `AddApi`, and **no per-module `AddX()` installer** — a module's services, handlers, actions and workers are bound by `AddInfrastructure`'s scans. The scans are the helpers in `Infrastructure/Platform/AssemblyScanExtensions.cs`:

```csharp
// AddInfrastructure — one call per artifact kind; lifetimes by convention below.
services.AddImplementationsOf<ICommandAction>(infrastructure, ServiceLifetime.Transient, typeof(CapturingCommandAction));
services.AddImplementationsOf<ICommandCondition>(infrastructure, ServiceLifetime.Transient);
services.AddImplementationsOf<ISeeder>(infrastructure, ServiceLifetime.Scoped);
services.AddOpenGenericHandlers(infrastructure, typeof(IEventHandler<>), ServiceLifetime.Scoped);
services.AddServicesByConvention(infrastructure, ServiceLifetime.Scoped, /* explicitly registered interfaces */);
services.AddRepositoriesByConvention<GenericRepository<object>>(/* … */);
services.AddHostedWorkers(infrastructure, /* excluded special-construction workers */);
```

| Helper | Binds |
|---|---|
| `AddImplementationsOf<TMarker>` | every concrete implementor, as a multi-binding of `TMarker` (actions, conditions, providers, projections, seeders, `ILiveGame`, …) |
| `AddOpenGenericHandlers` | every `IEventHandler<T>` implementor, once per closed interface |
| `AddServicesByConvention` | every `I<X>Service` impl to its interface; ambiguity throws at build time |
| `AddRepositoriesByConvention` | every repository, self-registered by concrete type |
| `AddHostedWorkers` | every `IHostedService` / `BackgroundService` as a singleton hosted worker |

- **Lifetimes (convention):** stateless strategies (`ICommandAction`, `ICommandCondition`) = transient; per-request services/event handlers/repositories/projections/seeders/configurations = scoped; long-lived workers (`IHostedService`/`BackgroundService`) + singletons (crypto primitives, in-memory caches, registries) = singleton.
- **Service impls** bind by their `I<X>Service` interface; ambiguity (two impls of one interface) is a build-time failure, resolved by a profile decorator (`DeploymentProfile`), never a `switch`.
- **EF configurations** are picked up by `modelBuilder.ApplyConfigurationsFromAssembly(...)`.
- Adding a new command / action / handler / job = drop the file; it is live next boot. The hand-maintained `CommandActionRegistry` / `ConditionEvaluatorRegistry` are deleted.
- **`ISeeder` is scanned like the rest but *run* in a defined order**, not registration order — see §5 for the ordering contract and canonical seed order.

Mechanism (decided, as built): **hand-rolled reflection** in `AssemblyScanExtensions` — zero new dependency, keeps the lite binary clean. No Scrutor.

---

## 5. Content layer (D6)

`Infrastructure/Content/` holds the **shipped-but-editable** defaults and the machinery that keeps them current. It is organized by domain module, one folder per module:

| Folder | Holds |
|---|---|
| `Billing/` | `BillingTierSeeder` — the global billing tiers + quota limits |
| `Commands/` | built-in command, event-response and raid-flow seeders (`DefaultCommandsSeeder`, `EventResponseDefaultsSeeder`, `PlatformEventResponseDefaultsSeeder`, `RaidFlowSeeder`, `RaidStartFlowSeeder`, `RaidCommitFlowSeeder`, `PipelinePlatformContentSeeder`), `CommandFlowImporter`, and `EventHandlers/` that seed a newly-onboarded channel |
| `Identity/` | `ActionDefinitionSeeder`, `IamCatalogSeeder`, `PronounSeeder`, and identity backfills (`IamPrincipalBackfillSeeder`, `TwitchIdentityBackfillSeeder`, `YouTubeServiceConnectionBackfillSeeder`) |
| `Music/` | `MusicProviderServiceBackfillSeeder` |
| `Platform/` | `ConfigSeeder`, `TemplateSyntaxBackfillSeeder` |
| `PlatformContent/` | the platform-content engine: `PlatformContentDefinitionSeeder`, `PlatformContentService`, `PlatformDefaultRestoreService`, its EF configurations (`Persistence/`) and the per-kind template installers/payloads (`Templates/`) |
| `Tts/` | `TtsVoiceSeeder` |
| `Widgets/` | `FirstPartyWidgetCatalogue` + `FirstPartyWidgetCatalogueSeeder`, `WidgetSettingsSchemaProvider`, the embedded first-party widget sources (`Assets/*.vue`) and vendored scripts (`Vendor/`) |

The engine never hard-codes shipped defaults; on first run a seeder writes them as ordinary rows the streamer then edits (or disables) through the dashboard. A default command is therefore just a pre-seeded pipeline — same `PipelineEngine`, no special path. Not every `ISeeder` seeds defaults: the *backfill* seeders migrate stored data (they run late, ≥ 900) instead of shipping content.

### 5.1 Seed ordering contract

Reference data has real FK dependencies (a child table's rows can't be written before the parent's), so seeding is **ordered**, never registration-order. Each `ISeeder` declares its own position via an `int Order` member; the marker stays the discovery hook (§4) and `Order` stays the execution hook — no attribute, consistent with the scan-by-interface convention:

```csharp
public interface ISeeder
{
    int Order { get; }                                  // ascending; ties run in any order
    Task SeedAsync(CancellationToken ct = default);     // idempotent: upsert by natural key
}
```

The seed runner discovers every `ISeeder` (§4 scan), sorts by `Order` ascending, and runs them **sequentially inside one `IUnitOfWork` transaction** — all-or-nothing, rollback on any failure. Every seeder is **idempotent** (upsert by natural key), so re-runs and the single-fire startup seed are both safe. **Rule:** a seeder MUST order *after* every seeder whose rows it FK-references.

### 5.2 Canonical seed order (low → high)

Generated from the `int Order` value of every `ISeeder` in `Infrastructure/Content/` (regenerate it when a seeder is added or re-ordered; ties run in registration order, which the runner treats as unordered).

| `Order` | Seeder | Seeds | FK depends on |
|---|---|---|---|
| 5 | `ActionDefinitionSeeder` | `ActionDefinitions` (B.3) | — global reference |
| 6 | `BillingTierSeeder` | `BillingTier` + its quota limits | — global reference |
| 6 | `IamCatalogSeeder` | `IamPermissions` (C.1), `IamRoles` (C.2), `IamRolePermissions` (C.3) | — (roles + permissions in one pass) |
| 7 | `IamPrincipalBackfillSeeder` | mints the missing `IamPrincipal` for accounts already flagged platform principal | `IamRoles` (`IamCatalogSeeder`) |
| 10 | `ConfigSeeder` | global `Configuration` defaults (`BroadcasterId == null`) | — global reference |
| 10 | `PronounSeeder` | `Pronoun` (live alejo.io set + bundled fallback) | — global reference |
| 10 | `TtsVoiceSeeder` | `TtsVoice` (P.2) | — global reference |
| 10 | `FirstPartyWidgetCatalogueSeeder` | first-party `WidgetGalleryItem` rows (P.8) | — global reference |
| 12 | `PlatformEventResponseDefaultsSeeder` | `PlatformEventResponseDefault`, one per event preset | — global reference |
| 80 | `DefaultCommandsSeeder` | built-in music `ChannelBuiltinCommand` rows per channel | `Channels` |
| 81 | `EventResponseDefaultsSeeder` | one disabled `EventResponse` per event preset per channel | `Channels` |
| 81 | `PlatformContentDefinitionSeeder` | `PlatformContentDefinition`/`Version` v1 for the system commands + provenance backfill | `DefaultCommandsSeeder` rows |
| 82 | `RaidFlowSeeder` | the `!raid` command + its pipeline per channel | `Channels` |
| 83 | `RaidCommitFlowSeeder` | the `channel.raid.out` response + pipeline | `Channels`, `EventResponse` rows (`EventResponseDefaultsSeeder`) |
| 84 | `RaidStartFlowSeeder` | the `channel.raid.start` response + pipeline | `Channels`, `EventResponse` rows (`EventResponseDefaultsSeeder`) |
| 85 | `PipelinePlatformContentSeeder` | `pipeline`-kind `PlatformContentDefinition`/`Version` for the three raid flows + provenance backfill | the three raid-flow seeders |
| 900 | `TwitchIdentityBackfillSeeder` | primary `twitch` `UserIdentity` per existing `User` | `Users` |
| 910 | `MusicProviderServiceBackfillSeeder` | mirrors vaulted music-provider grants into the legacy `Service` store | `IntegrationConnection` |
| 910 | `TemplateSyntaxBackfillSeeder` | rewrites stored `${var}` templates to `{var}` | stored templates |
| 920 | `YouTubeServiceConnectionBackfillSeeder` | vaults legacy YouTube `Service` tokens into `IntegrationConnection` | `Service` |

> `DeploymentProfile` (P.12) is **not** a seeder — do not add one. It is boot-detected and persisted by `IDeploymentProfileService.DetectAndPersistAsync` (`platform-conventions.md` §3.3), which runs as part of boot, not the seed pass.

---

## 6. Pipeline placement (D7) — resolves the current duplication

| Piece | Home |
|---|---|
| Contracts (`ICommandAction`, `IConditionEvaluator`, `PipelineDefinition`, `PipelineContext`) | `Application/Abstractions/Pipeline/` |
| The **one** `PipelineEngine` + registry | `Infrastructure/Platform/Pipeline/` |
| Core actions (`SetVariable`, `Stop`, `Wait`, `RandomResponse`) | `Infrastructure/Platform/Pipeline/CoreActions/` |
| Side-effecting actions | `Infrastructure/<Module>/PipelineActions/` — `SendMessage`→Chat, `Ban`/`Timeout`/`DeleteMessage`→Moderation, `Music*`→Music, `Shoutout`→Stream |

The duplicate `Application/Services/Pipeline/PipelineEngine` and the duplicated `SendMessage`/`SetVariable`/`Stop` actions are **deleted**; one engine, one home per action.

---

## 7. `make:` scaffolds (D8)

A `dotnet new` template per artifact type (`nomnomz-command`, `nomnomz-action`, `nomnomz-handler`, `nomnomz-job`, `nomnomz-projection`, `nomnomz-service`, `nomnomz-entity`). Each takes `--module` and emits the file in its canonical folder with the license header, file-scoped namespace, and base type filled in. No Roslyn — plain templating.

---

## 8. Migration map (current → target) — the cleanup fleet's input

The code is currently **type-first**; the target is **module-first**. Each row is a tightly-scoped agent job (see the cleanup-fleet plan).

| Current | Target | Note |
|---|---|---|
| `Domain/Entities/*` (29), `Domain/Events/*` (53), `Domain/Enums/*`, `Domain/Interfaces/*` | `Domain/<Module>/{Entities,Events,Enums,Interfaces}/` | split by module |
| `Application/Services/*` (15 interfaces) | `Application/<Module>/Services/` | one interface per module |
| `Application/DTOs/<X>/` | `Application/<Module>/Dtos/` | already semi-module — rename + relocate |
| `Application/Features/**` (CQRS handlers) + `Features/Features/` bug | **delete** → fold into `I<X>Service` + Infrastructure impl | no MediatR; removes the double-nest bug |
| `Application/Pipeline/**`, `Application/Services/Pipeline/PipelineEngine` | contracts → `Application/Abstractions/Pipeline/`; engine **deleted** (dup) | §6 |
| `Infrastructure/Pipeline/**` | `Platform/Pipeline/` (engine, core actions) + `<Module>/PipelineActions/` | §6 |
| `Infrastructure/Services/Application/*` (15), `Services/General/*` (7) | `Infrastructure/<Module>/` | de-provenance; orphan `FairQueue`/`TrustService` dupes in `General` **deleted** (music-sr reconciliation) |
| `Infrastructure/Services/{Music,Twitch,Tts,Identity,Moderation,Security,Caching,Trust,Registry,Migration}` | `Infrastructure/<Module>/` | `Collections/FairQueue` → `Music/`; `Services/Trust` → `Music/` |
| `Infrastructure/EventHandlers/*` (16) | `Infrastructure/<Module>/EventHandlers/` | by module |
| `Api/Hubs/EventHandlers/*` (12) | `Api/Hubs/Broadcasters/` | transport stays in Api |
| `Infrastructure/Persistence/Configurations/*` (25) | `Infrastructure/<Module>/Persistence/` | co-locate EF config with its module |
| `Infrastructure/Migrations/*` | `Platform/Persistence/Migrations/` | — |
| `Infrastructure/BackgroundServices/**` | `<Module>/Jobs/` + `Platform/Scheduling/` | — |
| `Infrastructure/Stubs/*` (4) | implement or **delete** | no placeholders in main |
| `*/DependencyInjection.cs`, `CommandActionRegistry`, `ConditionEvaluatorRegistry` | one assembly-scan installer per layer | §4 |

**Cleanup fleet** (one job each, on `/loop`): `structure-mapper` (read-only move-list) → `file-relocator` (one file → home + fix namespace/refs) → `dedup-collapser` (merge one duplicate pair) → `handler-to-service` (one `Features/` handler → service method) → `auto-register-converter` (one manual wiring → scan) → `content-extractor` (one default → `Content/`) → `build-gate` (build + test per batch) → `taxonomy-linter` (flag any artifact in the wrong home). The mapper reads this spec as its rulebook.

---

## 9. Linting the rule

`taxonomy-linter` (read-only, also a CI gate) fails the build when a file violates the taxonomy: a banned folder name (§D2), an artifact whose folder doesn't match its base type (§2), a second `PipelineEngine`, a manual DI registration of a scannable type, or a namespace that doesn't equal its folder path. The rulebook is therefore enforced, not just documented.
