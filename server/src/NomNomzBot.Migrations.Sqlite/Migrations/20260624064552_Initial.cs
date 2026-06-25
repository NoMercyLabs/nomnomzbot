using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NomNomzBot.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActionDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActionKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Plane = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DefaultLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    FloorLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    FloorTier = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsGrantableViaPermit = table.Column<bool>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: true
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionDefinitions", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "BillingTiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    PriceCents = table.Column<int>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    StripePriceId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: true
                    ),
                    StripeProductId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: true
                    ),
                    AllowsCustomBotName = table.Column<bool>(type: "INTEGER", nullable: false),
                    PrioritySupport = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPublic = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingTiers", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "BotAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdentityType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 10,
                        nullable: false
                    ),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    BotUserId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    BotUsername = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: false
                    ),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotAccounts", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "CatalogItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NameNormalized = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    SinkType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Cost = table.Column<long>(type: "INTEGER", nullable: false),
                    IconUrl = table.Column<string>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Permission = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PipelineId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CooldownSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    CooldownPerUser = table.Column<bool>(type: "INTEGER", nullable: false),
                    StockLimit = table.Column<int>(type: "INTEGER", nullable: true),
                    StockRemaining = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxPerViewerPerStream = table.Column<int>(type: "INTEGER", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogItems", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "CatalogPurchases",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatalogItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BuyerAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BuyerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CostPaid = table.Column<long>(type: "INTEGER", nullable: false),
                    ItemNameSnapshot = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LedgerEntryId = table.Column<long>(type: "INTEGER", nullable: true),
                    InputArgs = table.Column<string>(type: "TEXT", nullable: true),
                    IdempotencyKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: true
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogPurchases", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "ChannelActionOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActionDefinitionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OverrideLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    SetByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelActionOverrides", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "ChannelAnalyticsDailies",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActivityDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    UniqueChatters = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalMessages = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalWatchSeconds = table.Column<long>(type: "INTEGER", nullable: false),
                    NewFollowers = table.Column<int>(type: "INTEGER", nullable: false),
                    NewSubscribers = table.Column<int>(type: "INTEGER", nullable: false),
                    BitsCheered = table.Column<long>(type: "INTEGER", nullable: false),
                    CommandsRun = table.Column<long>(type: "INTEGER", nullable: false),
                    RedemptionsCount = table.Column<long>(type: "INTEGER", nullable: false),
                    SongRequests = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrencyEarnedTotal = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrencySpentTotal = table.Column<long>(type: "INTEGER", nullable: false),
                    GamesPlayed = table.Column<int>(type: "INTEGER", nullable: false),
                    PeakViewers = table.Column<int>(type: "INTEGER", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelAnalyticsDailies", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "ChannelCommunityStandings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Standing = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LevelValue = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SubTier = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelCommunityStandings", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "ChannelFederationOptIns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OptInType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnabledByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelFederationOptIns", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "ChannelMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ManagementRole = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    LevelValue = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelMemberships", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "CodeScripts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: true
                    ),
                    Language = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CurrentVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastRuntimeError = table.Column<string>(type: "TEXT", nullable: true),
                    LastRanAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeScripts", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "CodeScriptVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CodeScriptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceCode = table.Column<string>(type: "TEXT", nullable: false),
                    CompiledJs = table.Column<string>(type: "TEXT", nullable: true),
                    CompiledHash = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: true
                    ),
                    ValidationStatus = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    ValidationErrorsJson = table.Column<string>(type: "TEXT", nullable: true),
                    DeclaredCapabilitiesJson = table.Column<string>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AuthorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeScriptVersions", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "ConsentRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubjectUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectKeyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubjectIdHash = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    ConsentType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LawfulBasis = table.Column<string>(
                        type: "TEXT",
                        maxLength: 30,
                        nullable: false
                    ),
                    ConsentVersion = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: true
                    ),
                    Source = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    IpAddressCipher = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: true
                    ),
                    GrantedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WithdrawnAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentRecords", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "CurrencyAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    Balance = table.Column<long>(type: "INTEGER", nullable: false),
                    LifetimeEarned = table.Column<long>(type: "INTEGER", nullable: false),
                    LifetimeSpent = table.Column<long>(type: "INTEGER", nullable: false),
                    IsFrozen = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrencyAccounts", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "CurrencyConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CurrencyName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    CurrencyNamePlural = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: true
                    ),
                    IconUrl = table.Column<string>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartingBalance = table.Column<long>(type: "INTEGER", nullable: false),
                    MaxBalance = table.Column<long>(type: "INTEGER", nullable: true),
                    DecimalPlaces = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrencyConfigs", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "CurrencyLedgerEntries",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantPosition = table.Column<long>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    BalanceAfter = table.Column<long>(type: "INTEGER", nullable: false),
                    EntryType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RelatedEntryId = table.Column<long>(type: "INTEGER", nullable: true),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrencyLedgerEntries", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "DeletionAuditLogs",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 30,
                        nullable: false
                    ),
                    SubjectIdHash = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    RequestedBy = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    TablesAffected = table.Column<string>(type: "TEXT", nullable: false),
                    RowsDeleted = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeletionAuditLogs", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "DeploymentProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    WasAutoDetected = table.Column<bool>(type: "INTEGER", nullable: false),
                    DbProvider = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CacheProvider = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    EventSubTransport = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    CodeExecutor = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    TokenVault = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ExposureModel = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    RlsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultGuidanceLevel = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentProfiles", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "EarningRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Rate = table.Column<long>(type: "INTEGER", nullable: false),
                    UnitWindowSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    PerWindowCap = table.Column<long>(type: "INTEGER", nullable: true),
                    PerStreamCap = table.Column<long>(type: "INTEGER", nullable: true),
                    MinRoleLevel = table.Column<int>(type: "INTEGER", nullable: true),
                    ConfigSchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    BonusConfigJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EarningRules", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "EventJournals",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StreamPosition = table.Column<long>(type: "INTEGER", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    EventVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadIsEncrypted = table.Column<bool>(type: "INTEGER", nullable: false),
                    SubjectKeyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CausationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActorTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: true
                    ),
                    Metadata = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventJournals", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "EventSubConduits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ConduitId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ShardCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LastReconciledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventSubConduits", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "FeatureFlagOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FeatureFlagId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureFlagOverrides", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "FeatureFlags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: true
                    ),
                    IsEnabledGlobally = table.Column<bool>(type: "INTEGER", nullable: false),
                    RolloutPercentage = table.Column<int>(type: "INTEGER", nullable: false),
                    MinTierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MinTierKey = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    RequiresConsent = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: true
                    ),
                    DeploymentMode = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: true
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureFlags", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "FederationPeerKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PublicKey = table.Column<string>(type: "TEXT", nullable: false),
                    Algorithm = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    KeyId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ValidTo = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FederationPeerKeys", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "FederationPeers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    DisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: true
                    ),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    DeploymentMode = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    TrustState = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastHandshakeAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FederationPeers", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "FoundersBadges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    InviteCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoundersBadges", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "GameConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GameType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Requires18Plus = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinBet = table.Column<long>(type: "INTEGER", nullable: true),
                    MaxBet = table.Column<long>(type: "INTEGER", nullable: true),
                    HouseEdgePercent = table.Column<decimal>(
                        type: "TEXT",
                        precision: 5,
                        scale: 2,
                        nullable: true
                    ),
                    WinChancePercent = table.Column<decimal>(
                        type: "TEXT",
                        precision: 5,
                        scale: 2,
                        nullable: true
                    ),
                    PayoutMultiplier = table.Column<decimal>(
                        type: "TEXT",
                        precision: 8,
                        scale: 2,
                        nullable: true
                    ),
                    CooldownSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxPlaysPerStream = table.Column<int>(type: "INTEGER", nullable: true),
                    ConfigJson = table.Column<string>(type: "TEXT", nullable: true),
                    Permission = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameConfigs", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "GamePlays",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GameConfigId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BetAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PayoutAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    NetResult = table.Column<long>(type: "INTEGER", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    BetLedgerEntryId = table.Column<long>(type: "INTEGER", nullable: true),
                    PayoutLedgerEntryId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GamePlays", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "HttpEgressAllowlists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Fqdn = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxResponseBytes = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxRequestBytes = table.Column<int>(type: "INTEGER", nullable: false),
                    AllowRequestBody = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowQuery = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowedMethods = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: false
                    ),
                    PathPrefix = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HttpEgressAllowlists", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "IamAuditLogs",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PrincipalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrincipalType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    Permission = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    TargetBroadcasterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetResource = table.Column<string>(
                        type: "TEXT",
                        maxLength: 150,
                        nullable: true
                    ),
                    Justification = table.Column<string>(type: "TEXT", nullable: true),
                    BreakGlass = table.Column<bool>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SourceIpCipher = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: true
                    ),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IamAuditLogs", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "IamPermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsSensitive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: true
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IamPermissions", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "IamPrincipals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrincipalType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EmailCipher = table.Column<string>(
                        type: "TEXT",
                        maxLength: 512,
                        nullable: true
                    ),
                    SubjectKeyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ServiceAccountKeyHash = table.Column<string>(
                        type: "TEXT",
                        maxLength: 128,
                        nullable: true
                    ),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IamPrincipals", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "IamRoleAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrincipalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScopeChannelId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AssignedByPrincipalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IamRoleAssignments", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "IamRolePermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PermissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IamRolePermissions", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "IamRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: true
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IamRoles", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "IdempotencyKeys",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ResultHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyKeys", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "InboundWebhookEndpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Token = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AdapterKind = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    VerificationSecretEnvelope = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1024,
                        nullable: false
                    ),
                    EncryptionKeyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GenericConfigJson = table.Column<string>(type: "TEXT", nullable: true),
                    TargetPipelineId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetEventType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: true
                    ),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReceiveCount = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundWebhookEndpoints", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "InviteCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    MaxRedemptions = table.Column<int>(type: "INTEGER", nullable: false),
                    RedemptionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    GrantsFoundersBadge = table.Column<bool>(type: "INTEGER", nullable: false),
                    GrantsTierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InviteCodes", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StripeInvoiceId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: true
                    ),
                    Number = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AmountDueCents = table.Column<int>(type: "INTEGER", nullable: false),
                    AmountPaidCents = table.Column<int>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PeriodEnd = table.Column<DateTime>(type: "TEXT", nullable: true),
                    HostedInvoiceUrl = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2048,
                        nullable: true
                    ),
                    IssuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "JarContributions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JarId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceBroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContributorAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ContributorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    MovementType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    JarBalanceAfter = table.Column<long>(type: "INTEGER", nullable: false),
                    LedgerEntryId = table.Column<long>(type: "INTEGER", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JarContributions", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "LeaderboardConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    JarId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Metric = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Period = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsPublic = table.Column<bool>(type: "INTEGER", nullable: false),
                    TopN = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardConfigs", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "LeaderboardOptOuts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    OptedOutAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardOptOuts", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "LeaderboardSnapshots",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LeaderboardConfigId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PeriodKey = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    SubjectAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubjectUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubjectTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    DisplayNameSnapshot = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: false
                    ),
                    Value = table.Column<long>(type: "INTEGER", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaderboardSnapshots", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "MessageActivityDailies",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActivityDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    MessageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstMessageAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastMessageAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageActivityDailies", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "OutboundWebhookDeliveries",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EndpointId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WebhookMessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JournalEventId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    RenderedBody = table.Column<string>(type: "TEXT", nullable: false),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ResponseCode = table.Column<int>(type: "INTEGER", nullable: true),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: true),
                    NextRetryAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundWebhookDeliveries", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "OutboundWebhookEndpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Fqdn = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                    HttpEgressAllowlistId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    SubscribedEventTypesJson = table.Column<string>(type: "TEXT", nullable: false),
                    BodyTemplate = table.Column<string>(type: "TEXT", nullable: true),
                    CustomHeadersJson = table.Column<string>(type: "TEXT", nullable: true),
                    SigningSecretEnvelope = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1024,
                        nullable: false
                    ),
                    SecondarySigningSecretEnvelope = table.Column<string>(
                        type: "TEXT",
                        maxLength: 1024,
                        nullable: true
                    ),
                    EncryptionKeyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConsecutiveFailureCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DisabledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DisabledReason = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: true
                    ),
                    LastDeliveryAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSuccessAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundWebhookEndpoints", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "PermitGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GrantType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    GrantedRole = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ActionDefinitionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GrantedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitGrants", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "ProjectionCheckpoints",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectionName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 150,
                        nullable: false
                    ),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastPosition = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    LastProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectionCheckpoints", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "Pronouns",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Object = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Singular = table.Column<bool>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pronouns", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "SavingsJarMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JarId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MemberBroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ContributionCapPerStream = table.Column<long>(type: "INTEGER", nullable: true),
                    WithdrawalCap = table.Column<long>(type: "INTEGER", nullable: true),
                    InvitedByBroadcasterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavingsJarMemberships", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "SavingsJars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerBroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    GoalAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    Balance = table.Column<long>(type: "INTEGER", nullable: false),
                    IconUrl = table.Column<string>(type: "TEXT", nullable: true),
                    IsOpen = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxWithdrawalPerChannel = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavingsJars", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TierId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StripeCustomerIdCipher = table.Column<string>(
                        type: "TEXT",
                        maxLength: 512,
                        nullable: true
                    ),
                    StripeSubscriptionId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: true
                    ),
                    BillingEmailCipher = table.Column<string>(
                        type: "TEXT",
                        maxLength: 512,
                        nullable: true
                    ),
                    SubjectKeyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CurrentPeriodStart = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CurrentPeriodEnd = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TrialEndsAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    GracePeriodEndsAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelAtPeriodEnd = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanceledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsInviteOnlyGrant = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TenantSequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    NextValue = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSequences", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TierLimits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TierId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LimitKey = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    LimitValue = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TierLimits", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TtsCacheEntries",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContentHash = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: false
                    ),
                    AudioData = table.Column<string>(type: "TEXT", nullable: false),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    VoiceId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TtsCacheEntries", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TtsUsageRecords",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CharacterCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    VoiceId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TtsUsageRecords", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "TtsVoices",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: false
                    ),
                    Locale = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Gender = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    IsDefault = table.Column<bool>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: false
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TtsVoices", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "UsageRecords",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MetricKey = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Quantity = table.Column<long>(type: "INTEGER", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReportedToStripe = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageRecords", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "UserTtsVoices",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    VoiceId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTtsVoices", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "ViewerAgeConsents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    ConsentRecordId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Granted = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConfirmationMethod = table.Column<string>(
                        type: "TEXT",
                        maxLength: 30,
                        nullable: false
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ViewerAgeConsents", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "ViewerEngagementDailies",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActivityDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    WatchSeconds = table.Column<long>(type: "INTEGER", nullable: false),
                    MessageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CommandCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RedemptionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SongRequestCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrencyEarned = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrencySpent = table.Column<long>(type: "INTEGER", nullable: false),
                    GamesPlayed = table.Column<int>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ViewerEngagementDailies", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "ViewerProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerTwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    UsernameSnapshot = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: true
                    ),
                    DisplayNameSnapshot = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: true
                    ),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TotalWatchSeconds = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalMessages = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalCommandsUsed = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalRedemptions = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalSongRequests = table.Column<long>(type: "INTEGER", nullable: false),
                    IsFollower = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSubscriber = table.Column<bool>(type: "INTEGER", nullable: false),
                    SubTier = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    IsAnalyticsOptedOut = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ViewerProfiles", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "WatchSessions",
                columns: table => new
                {
                    Id = table
                        .Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StreamId = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DurationSeconds = table.Column<long>(type: "INTEGER", nullable: false),
                    PresenceConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    MessageCountInSession = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchSessions", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "EventSubConduitShards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConduitId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShardId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Transport = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CallbackUrl = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2048,
                        nullable: true
                    ),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventSubConduitShards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventSubConduitShards_EventSubConduits_ConduitId",
                        column: x => x.ConduitId,
                        principalTable: "EventSubConduits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TwitchUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    UsernameNormalized = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: false
                    ),
                    DisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: false
                    ),
                    NickName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    EmailCipher = table.Column<string>(
                        type: "TEXT",
                        maxLength: 512,
                        nullable: true
                    ),
                    SubjectKeyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Timezone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: true
                    ),
                    ProfileImageUrl = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2048,
                        nullable: true
                    ),
                    OfflineImageUrl = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2048,
                        nullable: true
                    ),
                    Color = table.Column<string>(type: "TEXT", maxLength: 7, nullable: true),
                    BroadcasterType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false,
                        defaultValue: ""
                    ),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AccountCreatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Enabled = table.Column<bool>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: true
                    ),
                    IsPlatformPrincipal = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsBot = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsAnonymized = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PronounId = table.Column<int>(type: "INTEGER", nullable: true),
                    PronounManualOverride = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Pronouns_PronounId",
                        column: x => x.PronounId,
                        principalTable: "Pronouns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TwitchChannelId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    Name = table.Column<string>(type: "TEXT", maxLength: 25, nullable: false),
                    NameNormalized = table.Column<string>(
                        type: "TEXT",
                        maxLength: 25,
                        nullable: false
                    ),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SuspendedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SuspendedReason = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: true
                    ),
                    DeploymentMode = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    BillingTierKey = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    Enabled = table.Column<bool>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: true
                    ),
                    ShoutoutTemplate = table.Column<string>(
                        type: "TEXT",
                        maxLength: 450,
                        nullable: true
                    ),
                    LastShoutout = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ShoutoutInterval = table.Column<int>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: 10
                    ),
                    UsernamePronunciation = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: true
                    ),
                    IsOnboarded = table.Column<bool>(type: "INTEGER", nullable: false),
                    BotJoinedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OverlayToken = table.Column<string>(
                        type: "TEXT",
                        maxLength: 36,
                        nullable: false
                    ),
                    SongRequestPageToken = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: true
                    ),
                    IsLive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    GameId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    GameName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    StreamDelay = table.Column<int>(type: "INTEGER", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    ContentLabels = table.Column<string>(type: "TEXT", nullable: false),
                    IsBrandedContent = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Channels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Channels_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "IpcDevModeKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    KeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IpcDevModeKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IpcDevModeKeys_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "AuthSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClientType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IpAddressCipher = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: true
                    ),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthSessions_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                    table.ForeignKey(
                        name: "FK_AuthSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "ChannelBotAuthorizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BotAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorizedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AuthorizedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BotJoinedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: true
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelBotAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelBotAuthorizations_BotAccounts_BotAccountId",
                        column: x => x.BotAccountId,
                        principalTable: "BotAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_ChannelBotAuthorizations_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "ChannelEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ChannelId = table.Column<Guid>(type: "TEXT", maxLength: 50, nullable: true),
                    UserId = table.Column<Guid>(type: "TEXT", maxLength: 50, nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelEvents_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                    table.ForeignKey(
                        name: "FK_ChannelEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "ChannelFeatures",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FeatureKey = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    IsEnabled = table.Column<bool>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: false
                    ),
                    EnabledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RequiredScopes = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelFeatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelFeatures_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "ChannelMissingScopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Feature = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ChatNotifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelMissingScopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelMissingScopes_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "ChannelModerators",
                columns: table => new
                {
                    ChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false,
                        defaultValue: "moderator"
                    ),
                    GrantedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelModerators", x => new { x.ChannelId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ChannelModerators_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_ChannelModerators_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "ChannelSubscriptions",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tier = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false,
                        defaultValue: "free"
                    ),
                    StripeCustomerId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: true
                    ),
                    StripeSubscriptionId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: true
                    ),
                    CurrentPeriodEnd = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false,
                        defaultValue: "active"
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelSubscriptions_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Commands",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Permission = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false,
                        defaultValue: "everyone"
                    ),
                    Type = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false,
                        defaultValue: "text"
                    ),
                    Response = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Responses = table.Column<string>(type: "TEXT", nullable: false),
                    PipelineJson = table.Column<string>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: true
                    ),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: true
                    ),
                    CooldownSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    CooldownPerUser = table.Column<bool>(type: "INTEGER", nullable: false),
                    Aliases = table.Column<string>(type: "TEXT", nullable: false),
                    IsPlatform = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Commands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Commands_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Configurations",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true),
                    SecureValue = table.Column<string>(
                        type: "TEXT",
                        maxLength: 4096,
                        nullable: true
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Configurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Configurations_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "DiscordGuildConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GuildId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    GuildName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    BotInstalled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ServerConsentStatus = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    ApprovedByDiscordUserId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: true
                    ),
                    ApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StreamerEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscordGuildConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscordGuildConnections_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "EventResponses",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResponseType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    PipelineJson = table.Column<string>(type: "TEXT", nullable: true),
                    Metadata = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventResponses_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "EventSubSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Condition = table.Column<string>(type: "TEXT", nullable: false),
                    Transport = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TwitchSubscriptionId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: true
                    ),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    ConduitId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    ShardId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Cost = table.Column<int>(type: "INTEGER", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventSubSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventSubSubscriptions_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "IntegrationConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ProviderAccountId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: true
                    ),
                    ProviderAccountName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: true
                    ),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Scopes = table.Column<string>(type: "TEXT", nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    IsByok = table.Column<bool>(type: "INTEGER", nullable: false),
                    Settings = table.Column<string>(type: "TEXT", nullable: true),
                    ConnectedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ConnectedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastRefreshedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastErrorAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConsecutiveFailureCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntegrationConnections_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 10,
                        nullable: false
                    ),
                    SubjectId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ResourceType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    PermissionValue = table.Column<string>(
                        type: "TEXT",
                        maxLength: 5,
                        nullable: false
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Permissions_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Pipelines",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: true
                    ),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    GraphJson = table.Column<string>(type: "TEXT", nullable: false),
                    TriggerCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastTriggeredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pipelines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pipelines_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Quotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    QuotedDisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: true
                    ),
                    ContextGame = table.Column<string>(
                        type: "TEXT",
                        maxLength: 100,
                        nullable: true
                    ),
                    QuotedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Quotes_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Records",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecordType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Records_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Rewards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Response = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Permission = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false,
                        defaultValue: "everyone"
                    ),
                    IsEnabled = table.Column<bool>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: true
                    ),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: true
                    ),
                    PipelineJson = table.Column<string>(type: "TEXT", nullable: true),
                    IsPlatform = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwitchRewardId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: true
                    ),
                    Cost = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rewards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rewards_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Services",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Enabled = table.Column<bool>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: true
                    ),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ClientSecret = table.Column<string>(
                        type: "TEXT",
                        maxLength: 512,
                        nullable: true
                    ),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Scopes = table.Column<string>(type: "TEXT", nullable: false),
                    AccessToken = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2048,
                        nullable: true
                    ),
                    RefreshToken = table.Column<string>(
                        type: "TEXT",
                        maxLength: 2048,
                        nullable: true
                    ),
                    TokenExpiry = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Services", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Services_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Storages",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true),
                    SecureValue = table.Column<string>(
                        type: "TEXT",
                        maxLength: 4096,
                        nullable: true
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Storages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Storages_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Streams",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ChannelId = table.Column<Guid>(type: "TEXT", maxLength: 50, nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    GameId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    GameName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Delay = table.Column<int>(type: "INTEGER", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    ContentLabels = table.Column<string>(type: "TEXT", nullable: false),
                    IsBrandedContent = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Streams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Streams_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Timers",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Messages = table.Column<string>(type: "TEXT", nullable: false),
                    IntervalMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    MinChatActivity = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastFiredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextMessageIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Timers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Timers_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "WatchStreaks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    UserDisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: true
                    ),
                    CurrentStreak = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxStreak = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSeenDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchStreaks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WatchStreaks_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Widgets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Description = table.Column<string>(
                        type: "TEXT",
                        maxLength: 500,
                        nullable: true
                    ),
                    Version = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false,
                        defaultValue: "1.0.0"
                    ),
                    Framework = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false,
                        defaultValue: "vanilla"
                    ),
                    IsEnabled = table.Column<bool>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: true
                    ),
                    TemplateId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    EventSubscriptions = table.Column<string>(type: "TEXT", nullable: false),
                    Settings = table.Column<string>(type: "TEXT", nullable: false),
                    CustomCode = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Widgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Widgets_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PreviousTokenHash = table.Column<string>(
                        type: "TEXT",
                        maxLength: 64,
                        nullable: true
                    ),
                    IssuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedReason = table.Column<string>(
                        type: "TEXT",
                        maxLength: 30,
                        nullable: true
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_AuthSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AuthSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "DiscordNotificationRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GuildConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DiscordRoleId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    RoleName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    SelfAssignEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ButtonMessageId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: true
                    ),
                    ButtonChannelId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: true
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscordNotificationRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscordNotificationRoles_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_DiscordNotificationRoles_DiscordGuildConnections_GuildConnectionId",
                        column: x => x.GuildConnectionId,
                        principalTable: "DiscordGuildConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "IntegrationTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TokenType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CipherText = table.Column<string>(type: "TEXT", nullable: false),
                    Nonce = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    EncryptionKeyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RotatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntegrationTokens_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                    table.ForeignKey(
                        name: "FK_IntegrationTokens_IntegrationConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: false
                    ),
                    UserType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ColorHex = table.Column<string>(type: "TEXT", maxLength: 7, nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    Fragments = table.Column<string>(type: "TEXT", nullable: false),
                    Badges = table.Column<string>(type: "TEXT", nullable: false),
                    MessageType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false,
                        defaultValue: "text"
                    ),
                    IsCommand = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsCheer = table.Column<bool>(type: "INTEGER", nullable: false),
                    BitsAmount = table.Column<int>(type: "INTEGER", nullable: true),
                    IsHighlighted = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReplyToMessageId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 255,
                        nullable: true
                    ),
                    StreamId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_ChatMessages_Streams_StreamId",
                        column: x => x.StreamId,
                        principalTable: "Streams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "DiscordMemberOptIns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NotificationRoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DiscordMemberId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    OptInSource = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: false
                    ),
                    OptedInAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OptedOutAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscordMemberOptIns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscordMemberOptIns_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_DiscordMemberOptIns_DiscordNotificationRoles_NotificationRoleId",
                        column: x => x.NotificationRoleId,
                        principalTable: "DiscordNotificationRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "DiscordNotificationConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GuildConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TriggerType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 30,
                        nullable: false
                    ),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    TargetChannelId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: false
                    ),
                    PingRoleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MessageTemplate = table.Column<string>(type: "TEXT", nullable: true),
                    EmbedConfig = table.Column<string>(type: "TEXT", nullable: true),
                    MilestoneType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 20,
                        nullable: true
                    ),
                    MilestoneThreshold = table.Column<int>(type: "INTEGER", nullable: true),
                    ConfigSchemaVersion = table.Column<int>(
                        type: "INTEGER",
                        nullable: false,
                        defaultValue: 1
                    ),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscordNotificationConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscordNotificationConfigs_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_DiscordNotificationConfigs_DiscordGuildConnections_GuildConnectionId",
                        column: x => x.GuildConnectionId,
                        principalTable: "DiscordGuildConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_DiscordNotificationConfigs_DiscordNotificationRoles_PingRoleId",
                        column: x => x.PingRoleId,
                        principalTable: "DiscordNotificationRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "DiscordNotificationDispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BroadcasterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NotificationConfigId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TriggerType = table.Column<string>(
                        type: "TEXT",
                        maxLength: 30,
                        nullable: false
                    ),
                    DedupeKey = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    StreamId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PostedMessageId = table.Column<string>(
                        type: "TEXT",
                        maxLength: 50,
                        nullable: true
                    ),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    DispatchedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscordNotificationDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscordNotificationDispatches_Channels_BroadcasterId",
                        column: x => x.BroadcasterId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_DiscordNotificationDispatches_DiscordNotificationConfigs_NotificationConfigId",
                        column: x => x.NotificationConfigId,
                        principalTable: "DiscordNotificationConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ActionDefinitions_ActionKey",
                table: "ActionDefinitions",
                column: "ActionKey",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_BroadcasterId",
                table: "AuthSessions",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_UserId_RevokedAt",
                table: "AuthSessions",
                columns: new[] { "UserId", "RevokedAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_BillingTiers_Key",
                table: "BillingTiers",
                column: "Key",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_BotAccounts_BotUserId",
                table: "BotAccounts",
                column: "BotUserId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_BotAccounts_Platform_IdentityType",
                table: "BotAccounts",
                columns: new[] { "Platform", "IdentityType" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_BroadcasterId_NameNormalized",
                table: "CatalogItems",
                columns: new[] { "BroadcasterId", "NameNormalized" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CatalogPurchases_BroadcasterId_BuyerUserId",
                table: "CatalogPurchases",
                columns: new[] { "BroadcasterId", "BuyerUserId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CatalogPurchases_BroadcasterId_CatalogItemId",
                table: "CatalogPurchases",
                columns: new[] { "BroadcasterId", "CatalogItemId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CatalogPurchases_BroadcasterId_IdempotencyKey",
                table: "CatalogPurchases",
                columns: new[] { "BroadcasterId", "IdempotencyKey" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelActionOverrides_BroadcasterId_ActionDefinitionId",
                table: "ChannelActionOverrides",
                columns: new[] { "BroadcasterId", "ActionDefinitionId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelAnalyticsDailies_BroadcasterId_ActivityDate",
                table: "ChannelAnalyticsDailies",
                columns: new[] { "BroadcasterId", "ActivityDate" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelBotAuthorization_Broadcaster_BotAccount",
                table: "ChannelBotAuthorizations",
                columns: new[] { "BroadcasterId", "BotAccountId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelBotAuthorizations_BotAccountId",
                table: "ChannelBotAuthorizations",
                column: "BotAccountId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelCommunityStandings_BroadcasterId_UserId",
                table: "ChannelCommunityStandings",
                columns: new[] { "BroadcasterId", "UserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelEvent_ChannelId_CreatedAt",
                table: "ChannelEvents",
                columns: new[] { "ChannelId", "CreatedAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelEvent_ChannelId_Type",
                table: "ChannelEvents",
                columns: new[] { "ChannelId", "Type" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelEvents_UserId",
                table: "ChannelEvents",
                column: "UserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelFeatures_BroadcasterId_FeatureKey",
                table: "ChannelFeatures",
                columns: new[] { "BroadcasterId", "FeatureKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelFederationOptIns_BroadcasterId",
                table: "ChannelFederationOptIns",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelFederationOptIns_BroadcasterId_PeerId_OptInType",
                table: "ChannelFederationOptIns",
                columns: new[] { "BroadcasterId", "PeerId", "OptInType" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelFederationOptIns_IsEnabled",
                table: "ChannelFederationOptIns",
                column: "IsEnabled"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelFederationOptIns_OptInType",
                table: "ChannelFederationOptIns",
                column: "OptInType"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelMemberships_BroadcasterId_UserId",
                table: "ChannelMemberships",
                columns: new[] { "BroadcasterId", "UserId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelMissingScopes_BroadcasterId_Scope",
                table: "ChannelMissingScopes",
                columns: new[] { "BroadcasterId", "Scope" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelModerators_UserId",
                table: "ChannelModerators",
                column: "UserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Channel_NameNormalized",
                table: "Channels",
                column: "NameNormalized"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Channel_OverlayToken",
                table: "Channels",
                column: "OverlayToken",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Channel_OwnerUserId",
                table: "Channels",
                column: "OwnerUserId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Channel_SongRequestPageToken",
                table: "Channels",
                column: "SongRequestPageToken",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Channel_TwitchChannelId",
                table: "Channels",
                column: "TwitchChannelId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChannelSubscription_BroadcasterId",
                table: "ChannelSubscriptions",
                column: "BroadcasterId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessage_BroadcasterId_CreatedAt",
                table: "ChatMessages",
                columns: new[] { "BroadcasterId", "CreatedAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessage_UserId",
                table: "ChatMessages",
                column: "UserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_StreamId",
                table: "ChatMessages",
                column: "StreamId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CodeScripts_AuthorUserId",
                table: "CodeScripts",
                column: "AuthorUserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CodeScripts_BroadcasterId",
                table: "CodeScripts",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CodeScripts_BroadcasterId_Name",
                table: "CodeScripts",
                columns: new[] { "BroadcasterId", "Name" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CodeScripts_CurrentVersionId",
                table: "CodeScripts",
                column: "CurrentVersionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CodeScripts_IsEnabled",
                table: "CodeScripts",
                column: "IsEnabled"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CodeScriptVersions_BroadcasterId",
                table: "CodeScriptVersions",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CodeScriptVersions_CodeScriptId_Version",
                table: "CodeScriptVersions",
                columns: new[] { "CodeScriptId", "Version" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_CodeScriptVersions_CompiledHash",
                table: "CodeScriptVersions",
                column: "CompiledHash"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CodeScriptVersions_ValidationStatus",
                table: "CodeScriptVersions",
                column: "ValidationStatus"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Command_BroadcasterId_IsEnabled",
                table: "Commands",
                columns: new[] { "BroadcasterId", "IsEnabled" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Command_Name_BroadcasterId",
                table: "Commands",
                columns: new[] { "Name", "BroadcasterId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Configurations_BroadcasterId",
                table: "Configurations",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ConsentRecords_BroadcasterId_SubjectUserId_ConsentType",
                table: "ConsentRecords",
                columns: new[] { "BroadcasterId", "SubjectUserId", "ConsentType" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyAccounts_BroadcasterId_ViewerUserId",
                table: "CurrencyAccounts",
                columns: new[] { "BroadcasterId", "ViewerUserId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyConfigs_BroadcasterId",
                table: "CurrencyConfigs",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyLedgerEntries_BroadcasterId_AccountId_Id",
                table: "CurrencyLedgerEntries",
                columns: new[] { "BroadcasterId", "AccountId", "Id" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_CurrencyLedgerEntries_BroadcasterId_TenantPosition",
                table: "CurrencyLedgerEntries",
                columns: new[] { "BroadcasterId", "TenantPosition" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentProfiles_InstanceId",
                table: "DeploymentProfiles",
                column: "InstanceId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordGuildConnections_BroadcasterId_GuildId",
                table: "DiscordGuildConnections",
                columns: new[] { "BroadcasterId", "GuildId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordGuildConnections_GuildId",
                table: "DiscordGuildConnections",
                column: "GuildId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordMemberOptIns_BroadcasterId",
                table: "DiscordMemberOptIns",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordMemberOptIns_DiscordMemberId",
                table: "DiscordMemberOptIns",
                column: "DiscordMemberId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordMemberOptIns_NotificationRoleId",
                table: "DiscordMemberOptIns",
                column: "NotificationRoleId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordMemberOptIns_NotificationRoleId_DiscordMemberId",
                table: "DiscordMemberOptIns",
                columns: new[] { "NotificationRoleId", "DiscordMemberId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordNotificationConfigs_BroadcasterId",
                table: "DiscordNotificationConfigs",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordNotificationConfigs_GuildConnectionId",
                table: "DiscordNotificationConfigs",
                column: "GuildConnectionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordNotificationConfigs_GuildConnectionId_TriggerType",
                table: "DiscordNotificationConfigs",
                columns: new[] { "GuildConnectionId", "TriggerType" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordNotificationConfigs_PingRoleId",
                table: "DiscordNotificationConfigs",
                column: "PingRoleId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordNotificationConfigs_TriggerType",
                table: "DiscordNotificationConfigs",
                column: "TriggerType"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordNotificationDispatches_BroadcasterId",
                table: "DiscordNotificationDispatches",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordNotificationDispatches_DedupeKey",
                table: "DiscordNotificationDispatches",
                column: "DedupeKey"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordNotificationDispatches_DispatchedAt",
                table: "DiscordNotificationDispatches",
                column: "DispatchedAt"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordNotificationDispatches_NotificationConfigId",
                table: "DiscordNotificationDispatches",
                column: "NotificationConfigId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordNotificationDispatches_NotificationConfigId_DedupeKey",
                table: "DiscordNotificationDispatches",
                columns: new[] { "NotificationConfigId", "DedupeKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordNotificationDispatches_StreamId",
                table: "DiscordNotificationDispatches",
                column: "StreamId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordNotificationRoles_BroadcasterId",
                table: "DiscordNotificationRoles",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordNotificationRoles_DiscordRoleId",
                table: "DiscordNotificationRoles",
                column: "DiscordRoleId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordNotificationRoles_GuildConnectionId",
                table: "DiscordNotificationRoles",
                column: "GuildConnectionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_DiscordNotificationRoles_GuildConnectionId_DiscordRoleId",
                table: "DiscordNotificationRoles",
                columns: new[] { "GuildConnectionId", "DiscordRoleId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_EarningRules_BroadcasterId_Source",
                table: "EarningRules",
                columns: new[] { "BroadcasterId", "Source" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventJournal_ActorUserId",
                table: "EventJournals",
                column: "ActorUserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventJournal_BroadcasterId",
                table: "EventJournals",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventJournal_CorrelationId",
                table: "EventJournals",
                column: "CorrelationId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventJournal_EventId",
                table: "EventJournals",
                column: "EventId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventJournal_EventType",
                table: "EventJournals",
                column: "EventType"
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventJournal_OccurredAt",
                table: "EventJournals",
                column: "OccurredAt"
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventJournal_SubjectKeyId",
                table: "EventJournals",
                column: "SubjectKeyId"
            );

            migrationBuilder.CreateIndex(
                name: "UX_EventJournal_BroadcasterId_StreamPosition",
                table: "EventJournals",
                columns: new[] { "BroadcasterId", "StreamPosition" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventResponses_BroadcasterId",
                table: "EventResponses",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "UX_EventSubConduit_ConduitId",
                table: "EventSubConduits",
                column: "ConduitId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "UX_EventSubConduitShard_ConduitId_ShardId",
                table: "EventSubConduitShards",
                columns: new[] { "ConduitId", "ShardId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_EventSubSubscription_TwitchSubscriptionId",
                table: "EventSubSubscriptions",
                column: "TwitchSubscriptionId"
            );

            migrationBuilder.CreateIndex(
                name: "UX_EventSubSubscription_Broadcaster_Provider_Type_Version",
                table: "EventSubSubscriptions",
                columns: new[] { "BroadcasterId", "Provider", "EventType", "Version" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlagOverrides_BroadcasterId",
                table: "FeatureFlagOverrides",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlagOverrides_FeatureFlagId_BroadcasterId",
                table: "FeatureFlagOverrides",
                columns: new[] { "FeatureFlagId", "BroadcasterId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlags_Key",
                table: "FeatureFlags",
                column: "Key",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlags_MinTierId",
                table: "FeatureFlags",
                column: "MinTierId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FederationPeerKeys_IsActive",
                table: "FederationPeerKeys",
                column: "IsActive"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FederationPeerKeys_KeyId",
                table: "FederationPeerKeys",
                column: "KeyId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FederationPeerKeys_PeerId",
                table: "FederationPeerKeys",
                column: "PeerId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FederationPeerKeys_PeerId_KeyId",
                table: "FederationPeerKeys",
                columns: new[] { "PeerId", "KeyId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_FederationPeers_InstanceId",
                table: "FederationPeers",
                column: "InstanceId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_FederationPeers_TrustState",
                table: "FederationPeers",
                column: "TrustState"
            );

            migrationBuilder.CreateIndex(
                name: "IX_FoundersBadges_BroadcasterId",
                table: "FoundersBadges",
                column: "BroadcasterId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_FoundersBadges_InviteCode",
                table: "FoundersBadges",
                column: "InviteCode"
            );

            migrationBuilder.CreateIndex(
                name: "IX_GameConfigs_BroadcasterId_GameType",
                table: "GameConfigs",
                columns: new[] { "BroadcasterId", "GameType" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_GamePlays_BroadcasterId_GameConfigId",
                table: "GamePlays",
                columns: new[] { "BroadcasterId", "GameConfigId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_GamePlays_BroadcasterId_PlayerUserId",
                table: "GamePlays",
                columns: new[] { "BroadcasterId", "PlayerUserId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_HttpEgressAllowlists_BroadcasterId",
                table: "HttpEgressAllowlists",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_HttpEgressAllowlists_BroadcasterId_Fqdn",
                table: "HttpEgressAllowlists",
                columns: new[] { "BroadcasterId", "Fqdn" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_HttpEgressAllowlists_IsEnabled",
                table: "HttpEgressAllowlists",
                column: "IsEnabled"
            );

            migrationBuilder.CreateIndex(
                name: "IX_IamAuditLogs_OccurredAt",
                table: "IamAuditLogs",
                column: "OccurredAt"
            );

            migrationBuilder.CreateIndex(
                name: "IX_IamAuditLogs_PrincipalId",
                table: "IamAuditLogs",
                column: "PrincipalId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_IamPermissions_Key",
                table: "IamPermissions",
                column: "Key",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_IamPrincipals_UserId",
                table: "IamPrincipals",
                column: "UserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_IamRoleAssignments_PrincipalId_RoleId",
                table: "IamRoleAssignments",
                columns: new[] { "PrincipalId", "RoleId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_IamRolePermissions_RoleId_PermissionId",
                table: "IamRolePermissions",
                columns: new[] { "RoleId", "PermissionId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_IamRoles_Name",
                table: "IamRoles",
                column: "Name"
            );

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyKey_ExpiresAt",
                table: "IdempotencyKeys",
                column: "ExpiresAt"
            );

            migrationBuilder.CreateIndex(
                name: "UX_IdempotencyKey_Scope_Key_Broadcaster",
                table: "IdempotencyKeys",
                columns: new[] { "Scope", "Key", "BroadcasterId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_InboundWebhookEndpoints_BroadcasterId_Name",
                table: "InboundWebhookEndpoints",
                columns: new[] { "BroadcasterId", "Name" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_InboundWebhookEndpoints_Token",
                table: "InboundWebhookEndpoints",
                column: "Token",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationConnection_Broadcaster_Provider_Account",
                table: "IntegrationConnections",
                columns: new[] { "BroadcasterId", "Provider", "ProviderAccountId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationToken_Connection_TokenType",
                table: "IntegrationTokens",
                columns: new[] { "ConnectionId", "TokenType" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationTokens_BroadcasterId",
                table: "IntegrationTokens",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_InviteCodes_Code",
                table: "InviteCodes",
                column: "Code",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_IssuedAt",
                table: "Invoices",
                column: "IssuedAt"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_StripeInvoiceId",
                table: "Invoices",
                column: "StripeInvoiceId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_IpcDevModeKeys_CreatedByUserId",
                table: "IpcDevModeKeys",
                column: "CreatedByUserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_IpcDevModeKeys_KeyHash",
                table: "IpcDevModeKeys",
                column: "KeyHash",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_JarContributions_JarId",
                table: "JarContributions",
                column: "JarId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_JarContributions_SourceBroadcasterId",
                table: "JarContributions",
                column: "SourceBroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardConfigs_BroadcasterId",
                table: "LeaderboardConfigs",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardConfigs_JarId",
                table: "LeaderboardConfigs",
                column: "JarId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardOptOuts_BroadcasterId_ViewerUserId",
                table: "LeaderboardOptOuts",
                columns: new[] { "BroadcasterId", "ViewerUserId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_LeaderboardSnapshots_LeaderboardConfigId_PeriodKey",
                table: "LeaderboardSnapshots",
                columns: new[] { "LeaderboardConfigId", "PeriodKey" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_MessageActivityDailies_BroadcasterId_ViewerUserId_ActivityDate",
                table: "MessageActivityDailies",
                columns: new[] { "BroadcasterId", "ViewerUserId", "ActivityDate" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_MessageActivityDailies_ViewerUserId",
                table: "MessageActivityDailies",
                column: "ViewerUserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookDeliveries_EndpointId_CreatedAt",
                table: "OutboundWebhookDeliveries",
                columns: new[] { "EndpointId", "CreatedAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookDeliveries_Status_NextRetryAt",
                table: "OutboundWebhookDeliveries",
                columns: new[] { "Status", "NextRetryAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookEndpoints_BroadcasterId_Name",
                table: "OutboundWebhookEndpoints",
                columns: new[] { "BroadcasterId", "Name" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Permission_BroadcasterId_Subject_ResourceType",
                table: "Permissions",
                columns: new[] { "BroadcasterId", "SubjectType", "SubjectId", "ResourceType" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_PermitGrants_BroadcasterId_UserId",
                table: "PermitGrants",
                columns: new[] { "BroadcasterId", "UserId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Pipelines_BroadcasterId",
                table: "Pipelines",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ProjectionCheckpoint_BroadcasterId",
                table: "ProjectionCheckpoints",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ProjectionCheckpoint_ProjectionName",
                table: "ProjectionCheckpoints",
                column: "ProjectionName"
            );

            migrationBuilder.CreateIndex(
                name: "UX_ProjectionCheckpoint_ProjectionName_BroadcasterId",
                table: "ProjectionCheckpoints",
                columns: new[] { "ProjectionName", "BroadcasterId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_BroadcasterId_Number",
                table: "Quotes",
                columns: new[] { "BroadcasterId", "Number" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Record_BroadcasterId_RecordType",
                table: "Records",
                columns: new[] { "BroadcasterId", "RecordType" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Record_UserId",
                table: "Records",
                column: "UserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_SessionId",
                table: "RefreshTokens",
                column: "SessionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId_RevokedAt",
                table: "RefreshTokens",
                columns: new[] { "UserId", "RevokedAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Rewards_BroadcasterId_TwitchRewardId",
                table: "Rewards",
                columns: new[] { "BroadcasterId", "TwitchRewardId" },
                unique: true,
                filter: "\"TwitchRewardId\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SavingsJarMemberships_JarId_MemberBroadcasterId",
                table: "SavingsJarMemberships",
                columns: new[] { "JarId", "MemberBroadcasterId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SavingsJarMemberships_MemberBroadcasterId",
                table: "SavingsJarMemberships",
                column: "MemberBroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SavingsJars_OwnerBroadcasterId",
                table: "SavingsJars",
                column: "OwnerBroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Service_Name_BroadcasterId",
                table: "Services",
                columns: new[] { "Name", "BroadcasterId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Services_BroadcasterId",
                table: "Services",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Storage_Key_BroadcasterId",
                table: "Storages",
                columns: new[] { "Key", "BroadcasterId" },
                unique: true,
                filter: "\"BroadcasterId\" IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Storages_BroadcasterId",
                table: "Storages",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Streams_ChannelId",
                table: "Streams",
                column: "ChannelId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_BroadcasterId",
                table: "Subscriptions",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_StripeSubscriptionId",
                table: "Subscriptions",
                column: "StripeSubscriptionId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_TenantSequence_BroadcasterId",
                table: "TenantSequences",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "UX_TenantSequence_BroadcasterId_SequenceName",
                table: "TenantSequences",
                columns: new[] { "BroadcasterId", "SequenceName" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_TierLimits_TierId_LimitKey",
                table: "TierLimits",
                columns: new[] { "TierId", "LimitKey" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Timers_BroadcasterId",
                table: "Timers",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_TtsCacheEntry_ContentHash",
                table: "TtsCacheEntries",
                column: "ContentHash",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_UsageRecords_BroadcasterId_MetricKey_PeriodStart",
                table: "UsageRecords",
                columns: new[] { "BroadcasterId", "MetricKey", "PeriodStart" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_UsageRecords_PeriodStart",
                table: "UsageRecords",
                column: "PeriodStart"
            );

            migrationBuilder.CreateIndex(
                name: "IX_User_TwitchUserId",
                table: "Users",
                column: "TwitchUserId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_User_UsernameNormalized",
                table: "Users",
                column: "UsernameNormalized"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Users_PronounId",
                table: "Users",
                column: "PronounId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserTtsVoice_BroadcasterId_UserId",
                table: "UserTtsVoices",
                columns: new[] { "BroadcasterId", "UserId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_ViewerAgeConsents_BroadcasterId_ViewerUserId",
                table: "ViewerAgeConsents",
                columns: new[] { "BroadcasterId", "ViewerUserId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ViewerEngagementDailies_BroadcasterId_ViewerUserId_ActivityDate",
                table: "ViewerEngagementDailies",
                columns: new[] { "BroadcasterId", "ViewerUserId", "ActivityDate" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_ViewerEngagementDailies_ViewerUserId",
                table: "ViewerEngagementDailies",
                column: "ViewerUserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ViewerProfiles_BroadcasterId_ViewerUserId",
                table: "ViewerProfiles",
                columns: new[] { "BroadcasterId", "ViewerUserId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ViewerProfiles_LastSeenAt",
                table: "ViewerProfiles",
                column: "LastSeenAt"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ViewerProfiles_ViewerTwitchUserId",
                table: "ViewerProfiles",
                column: "ViewerTwitchUserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WatchSessions_BroadcasterId_ViewerUserId",
                table: "WatchSessions",
                columns: new[] { "BroadcasterId", "ViewerUserId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_WatchSessions_CreatedAt",
                table: "WatchSessions",
                column: "CreatedAt"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WatchSessions_StreamId",
                table: "WatchSessions",
                column: "StreamId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_WatchStreaks_BroadcasterId",
                table: "WatchStreaks",
                column: "BroadcasterId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Widgets_BroadcasterId",
                table: "Widgets",
                column: "BroadcasterId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ActionDefinitions");

            migrationBuilder.DropTable(name: "BillingTiers");

            migrationBuilder.DropTable(name: "CatalogItems");

            migrationBuilder.DropTable(name: "CatalogPurchases");

            migrationBuilder.DropTable(name: "ChannelActionOverrides");

            migrationBuilder.DropTable(name: "ChannelAnalyticsDailies");

            migrationBuilder.DropTable(name: "ChannelBotAuthorizations");

            migrationBuilder.DropTable(name: "ChannelCommunityStandings");

            migrationBuilder.DropTable(name: "ChannelEvents");

            migrationBuilder.DropTable(name: "ChannelFeatures");

            migrationBuilder.DropTable(name: "ChannelFederationOptIns");

            migrationBuilder.DropTable(name: "ChannelMemberships");

            migrationBuilder.DropTable(name: "ChannelMissingScopes");

            migrationBuilder.DropTable(name: "ChannelModerators");

            migrationBuilder.DropTable(name: "ChannelSubscriptions");

            migrationBuilder.DropTable(name: "ChatMessages");

            migrationBuilder.DropTable(name: "CodeScripts");

            migrationBuilder.DropTable(name: "CodeScriptVersions");

            migrationBuilder.DropTable(name: "Commands");

            migrationBuilder.DropTable(name: "Configurations");

            migrationBuilder.DropTable(name: "ConsentRecords");

            migrationBuilder.DropTable(name: "CurrencyAccounts");

            migrationBuilder.DropTable(name: "CurrencyConfigs");

            migrationBuilder.DropTable(name: "CurrencyLedgerEntries");

            migrationBuilder.DropTable(name: "DeletionAuditLogs");

            migrationBuilder.DropTable(name: "DeploymentProfiles");

            migrationBuilder.DropTable(name: "DiscordMemberOptIns");

            migrationBuilder.DropTable(name: "DiscordNotificationDispatches");

            migrationBuilder.DropTable(name: "EarningRules");

            migrationBuilder.DropTable(name: "EventJournals");

            migrationBuilder.DropTable(name: "EventResponses");

            migrationBuilder.DropTable(name: "EventSubConduitShards");

            migrationBuilder.DropTable(name: "EventSubSubscriptions");

            migrationBuilder.DropTable(name: "FeatureFlagOverrides");

            migrationBuilder.DropTable(name: "FeatureFlags");

            migrationBuilder.DropTable(name: "FederationPeerKeys");

            migrationBuilder.DropTable(name: "FederationPeers");

            migrationBuilder.DropTable(name: "FoundersBadges");

            migrationBuilder.DropTable(name: "GameConfigs");

            migrationBuilder.DropTable(name: "GamePlays");

            migrationBuilder.DropTable(name: "HttpEgressAllowlists");

            migrationBuilder.DropTable(name: "IamAuditLogs");

            migrationBuilder.DropTable(name: "IamPermissions");

            migrationBuilder.DropTable(name: "IamPrincipals");

            migrationBuilder.DropTable(name: "IamRoleAssignments");

            migrationBuilder.DropTable(name: "IamRolePermissions");

            migrationBuilder.DropTable(name: "IamRoles");

            migrationBuilder.DropTable(name: "IdempotencyKeys");

            migrationBuilder.DropTable(name: "InboundWebhookEndpoints");

            migrationBuilder.DropTable(name: "IntegrationTokens");

            migrationBuilder.DropTable(name: "InviteCodes");

            migrationBuilder.DropTable(name: "Invoices");

            migrationBuilder.DropTable(name: "IpcDevModeKeys");

            migrationBuilder.DropTable(name: "JarContributions");

            migrationBuilder.DropTable(name: "LeaderboardConfigs");

            migrationBuilder.DropTable(name: "LeaderboardOptOuts");

            migrationBuilder.DropTable(name: "LeaderboardSnapshots");

            migrationBuilder.DropTable(name: "MessageActivityDailies");

            migrationBuilder.DropTable(name: "OutboundWebhookDeliveries");

            migrationBuilder.DropTable(name: "OutboundWebhookEndpoints");

            migrationBuilder.DropTable(name: "Permissions");

            migrationBuilder.DropTable(name: "PermitGrants");

            migrationBuilder.DropTable(name: "Pipelines");

            migrationBuilder.DropTable(name: "ProjectionCheckpoints");

            migrationBuilder.DropTable(name: "Quotes");

            migrationBuilder.DropTable(name: "Records");

            migrationBuilder.DropTable(name: "RefreshTokens");

            migrationBuilder.DropTable(name: "Rewards");

            migrationBuilder.DropTable(name: "SavingsJarMemberships");

            migrationBuilder.DropTable(name: "SavingsJars");

            migrationBuilder.DropTable(name: "Services");

            migrationBuilder.DropTable(name: "Storages");

            migrationBuilder.DropTable(name: "Subscriptions");

            migrationBuilder.DropTable(name: "TenantSequences");

            migrationBuilder.DropTable(name: "TierLimits");

            migrationBuilder.DropTable(name: "Timers");

            migrationBuilder.DropTable(name: "TtsCacheEntries");

            migrationBuilder.DropTable(name: "TtsUsageRecords");

            migrationBuilder.DropTable(name: "TtsVoices");

            migrationBuilder.DropTable(name: "UsageRecords");

            migrationBuilder.DropTable(name: "UserTtsVoices");

            migrationBuilder.DropTable(name: "ViewerAgeConsents");

            migrationBuilder.DropTable(name: "ViewerEngagementDailies");

            migrationBuilder.DropTable(name: "ViewerProfiles");

            migrationBuilder.DropTable(name: "WatchSessions");

            migrationBuilder.DropTable(name: "WatchStreaks");

            migrationBuilder.DropTable(name: "Widgets");

            migrationBuilder.DropTable(name: "BotAccounts");

            migrationBuilder.DropTable(name: "Streams");

            migrationBuilder.DropTable(name: "DiscordNotificationConfigs");

            migrationBuilder.DropTable(name: "EventSubConduits");

            migrationBuilder.DropTable(name: "IntegrationConnections");

            migrationBuilder.DropTable(name: "AuthSessions");

            migrationBuilder.DropTable(name: "DiscordNotificationRoles");

            migrationBuilder.DropTable(name: "DiscordGuildConnections");

            migrationBuilder.DropTable(name: "Channels");

            migrationBuilder.DropTable(name: "Users");

            migrationBuilder.DropTable(name: "Pronouns");
        }
    }
}
