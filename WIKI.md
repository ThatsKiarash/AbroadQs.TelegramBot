# AbroadQs Telegram Bot - Project Wiki

> Last Updated: February 2026

---

## Table of Contents

1. [Overview](#1-overview)
2. [Architecture](#2-architecture)
3. [Project Structure](#3-project-structure)
4. [Layer-by-Layer Breakdown](#4-layer-by-layer-breakdown)
   - [4.1 Contracts Layer](#41-contracts-layer-abraboradbotcontracts)
   - [4.2 Data Layer](#42-data-layer-abroadqsbotdata)
   - [4.3 Application Layer](#43-application-layer-abroadqsbotapplication)
   - [4.4 Telegram Layer](#44-telegram-layer-abroadqsbottelegram)
   - [4.5 Modules Layer](#45-modules-layer-abroadqsbotmodulescommon)
   - [4.6 Host/Webhook Layer](#46-hostwebhook-layer-abroadqsbothostwebhook)
   - [4.7 Tunnel System](#47-tunnel-system)
5. [Database Schema](#5-database-schema)
6. [Bot Features & Handlers](#6-bot-features--handlers)
7. [Services & Integrations](#7-services--integrations)
8. [Dashboard (Admin Panel)](#8-dashboard-admin-panel)
9. [Configuration](#9-configuration)
10. [Docker & Infrastructure](#10-docker--infrastructure)
11. [Deployment Guide](#11-deployment-guide)
12. [Troubleshooting](#12-troubleshooting)

---

## 1. Overview

**AbroadQs** is a comprehensive Telegram bot platform for Iranian students abroad, providing:

- **Currency Exchange** - Buy/sell/exchange currencies with other users
- **Student Projects** - Post and bid on student projects
- **International Q&A** - Ask/answer questions about living abroad (with bounties)
- **Financial Sponsorship** - Request/provide financial sponsorship for student projects
- **Support Tickets** - User support system
- **Digital Wallet** - Internal wallet with payment gateway (BitPay)
- **Crypto Wallets** - Manage cryptocurrency wallet addresses
- **Identity Verification (KYC)** - Multi-step verification: name, phone, email, country, photo
- **Admin Dashboard** - Web-based management panel

### Tech Stack

| Component | Technology |
|-----------|-----------|
| **Runtime** | .NET 8 (ASP.NET Core) |
| **Language** | C# |
| **Database** | SQL Server 2022 (EF Core) |
| **Cache/State** | Redis 7 |
| **Message Queue** | RabbitMQ 3 |
| **Bot API** | Telegram.Bot library |
| **SMS** | sms.ir API |
| **Email** | HTTP Relay + MailKit (SMTP fallback) |
| **Payments** | BitPay.ir |
| **Exchange Rates** | Navasan API |
| **Containerization** | Docker Compose |
| **Reverse Proxy** | Nginx + Certbot (SSL) |
| **Dashboard** | HTML/JS + Bootstrap 5 + SweetAlert2 |

---

## 2. Architecture

```
                    ┌─────────────────┐
                    │   Telegram API   │
                    └────────┬────────┘
                             │
                    ┌────────▼────────┐
                    │     Nginx       │
                    │  (SSL + Proxy)  │
                    └────────┬────────┘
                             │
              ┌──────────────┼──────────────┐
              │              │              │
     ┌────────▼───────┐     │    ┌────────▼────────┐
     │ Tunnel Server   │     │    │   Bot Host      │
     │ (WebSocket)     │────►│    │  (Port 5252)    │
     │ (Port 9080)     │     │    │                 │
     └────────────────┘     │    └────────┬────────┘
                             │             │
              ┌──────────────┼─────────────┤
              │              │             │
     ┌────────▼──────┐ ┌────▼─────┐ ┌────▼──────┐
     │   RabbitMQ    │ │  Redis   │ │ SQL Server │
     │ (Port 5672)   │ │ (6379)   │ │  (1433)    │
     └───────────────┘ └──────────┘ └───────────┘
```

### Request Flow

1. **Telegram** sends a webhook POST to `https://webhook.abroadqs.com/webhook`
2. **Nginx** terminates SSL, forwards to **Tunnel Server** (port 9080)
3. **Tunnel Server** forwards via WebSocket to **Bot Host** (port 5252)
4. **Bot Host** receives the update:
   - Publishes to **RabbitMQ** (for logging/audit)
   - Extracts `BotUpdateContext` from the raw `Update`
   - **UpdateDispatcher** iterates through registered `IUpdateHandler`s
   - First matching handler processes the update
   - Handler uses **IResponseSender** to reply via Telegram API
5. State is managed in **Redis**, persistent data in **SQL Server**

### Design Patterns

| Pattern | Usage |
|---------|-------|
| **Dependency Injection** | All services registered in DI container |
| **Repository Pattern** | All database access through interfaces |
| **Handler Chain** | `IUpdateHandler` chain with priority ordering |
| **No-Op Pattern** | Optional services have NoOp fallbacks |
| **Scoped Context** | `IProcessingContext` tracks per-request state |
| **Module Pattern** | Handlers grouped into modules via `IModuleMarker` |

---

## 3. Project Structure

```
AbroadQs.TelegramBot/
├── docker-compose.yml              # Local development (infra only)
├── docker-compose.server.yml       # Production (all services)
├── .gitignore
├── AbroadQs.TelegramBot.slnx       # Solution file
├── scripts/
│   ├── deploy-server.sh            # Server-side deploy script
│   ├── setup-webhook-ssl.sh        # Nginx + SSL setup
│   └── update-nginx-tunnel.sh      # Update Nginx for tunnel
│
└── src/
    ├── AbroadQs.Bot.Contracts/      # Interfaces, DTOs, abstractions
    ├── AbroadQs.Bot.Data/           # EF Core entities, repos, migrations
    ├── AbroadQs.Bot.Application/    # Update dispatcher, DI extensions
    ├── AbroadQs.Bot.Telegram/       # Telegram API implementation
    ├── AbroadQs.Bot.Modules.Common/ # All bot handlers (business logic)
    ├── AbroadQs.Bot.Modules.Example/# Example/echo handler
    ├── AbroadQs.Bot.Host.Webhook/   # Main app: Program.cs, services, dashboard
    ├── AbroadQs.TunnelServer/       # WebSocket tunnel server
    ├── AbroadQs.TunnelClient/       # WebSocket tunnel client
    └── AbroadQs.ReverseProxy/       # YARP reverse proxy (legacy)

ServerSetup/ (Parent directory)
├── _deploy.py                       # Python deploy script (from local PC)
├── email_relay.php                  # PHP email relay for SMTP bypass
└── *.py                             # Various debug/helper scripts
```

---

## 4. Layer-by-Layer Breakdown

### 4.1 Contracts Layer (`AbroadQs.Bot.Contracts`)

> **Purpose**: Defines ALL interfaces, DTOs, and shared types. No implementations. All other projects reference this.

#### Core Interfaces

| File | Interface | Purpose |
|------|-----------|---------|
| `IUpdateHandler.cs` | `IUpdateHandler` | Base handler interface. `CanHandle()` + `HandleAsync()` |
| `IModuleMarker.cs` | `IModuleMarker` | Marker for module registration (assembly scanning) |
| `IResponseSender.cs` | `IResponseSender` | Abstraction for sending Telegram responses |
| `IProcessingContext.cs` | `IProcessingContext` | Per-request tracking (Redis, SQL, RabbitMQ usage) |
| `BotUpdateContext.cs` | `BotUpdateContext` | Parsed update context (ChatId, UserId, Text, Callbacks, etc.) |
| `BilingualHelper.cs` | `BilingualHelper` | `L(fa, en, lang)` helper for bilingual text |

#### `IResponseSender` Methods (Full List)

```
SendTextMessageAsync(chatId, text)
SendTextMessageAsync(chatId, text, disableWebPagePreview)
SendTextMessageWithInlineKeyboardAsync(chatId, text, keyboard)
SendTextMessageWithReplyKeyboardAsync(chatId, text, keyboard)
EditMessageTextWithInlineKeyboardAsync(chatId, messageId, text, keyboard)
EditMessageTextAsync(chatId, messageId, text)
UpdateReplyKeyboardSilentAsync(chatId, keyboard)
DeleteMessageAsync(chatId, messageId)
AnswerCallbackQueryAsync(callbackQueryId, message?)
SendContactRequestAsync(chatId, text, buttonLabel, cancelLabel?)
RemoveReplyKeyboardAsync(chatId, text)
RemoveReplyKeyboardSilentAsync(chatId)                    # Zero-width space trick
SendLoadingWithRemoveReplyKbAsync(chatId)
SendPhotoAsync(chatId, photoPath, caption?)
SendPhotoWithInlineKeyboardAsync(chatId, photoUrl, caption, keyboard?)
```

#### State Management Interfaces

| File | Interface | Purpose |
|------|-----------|---------|
| `IUserConversationStateStore.cs` | `IUserConversationStateStore` | Multi-step conversation state (Redis) |
| `IUserLastCommandStore.cs` | `IUserLastCommandStore` | Anti-spam deduplication (Redis) |
| `IUserMessageStateRepository.cs` | `IUserMessageStateRepository` | Last bot message tracking (SQL) |

**IUserConversationStateStore Methods:**

```
SetStateAsync / GetStateAsync / ClearStateAsync     # Conversation state (TTL: 1h)
SetReplyStageAsync / GetReplyStageAsync              # Current menu stage (TTL: 24h)
AddFlowMessageIdAsync / GetAndClearFlowMessageIdsAsync  # Flow message cleanup (TTL: 2h)
SetFlowDataAsync / GetFlowDataAsync / ClearAllFlowDataAsync  # Flow key-value data (TTL: 2h)
```

#### Repository Interfaces

| File | Interface | Purpose |
|------|-----------|---------|
| `ITelegramUserRepository.cs` | `ITelegramUserRepository` | User CRUD, KYC, profile |
| `IMessageRepository.cs` | `IMessageRepository` | Message logging (incoming/outgoing) |
| `ISettingsRepository.cs` | `ISettingsRepository` | Key-value settings |
| `IBotStageRepository.cs` | `IBotStageRepository` | Bot stages and buttons |
| `IPermissionRepository.cs` | `IPermissionRepository` | Permissions system |
| `IExchangeRepository.cs` | `IExchangeRepository` | Exchange requests and rates |
| `IGroupRepository.cs` | `IGroupRepository` | Exchange groups |
| `IBidRepository.cs` | `IBidRepository` | Exchange ad bids |
| `IWalletRepository.cs` | `IWalletRepository` | Digital wallet and payments |
| `ICryptoWalletRepository.cs` | `ICryptoWalletRepository` | Crypto wallets and purchases |
| `ITicketRepository.cs` | `ITicketRepository` | Support tickets |
| `IStudentProjectRepository.cs` | `IStudentProjectRepository` + `IProjectBidRepository` | Projects and bids |
| `IInternationalQuestionRepository.cs` | `IInternationalQuestionRepository` | Q&A with bounties |
| `ISponsorshipRepository.cs` | `ISponsorshipRepository` | Sponsorship requests |
| `ISystemMessageRepository.cs` | `ISystemMessageRepository` | System notifications |

#### External Service Interfaces

| File | Interface | Purpose |
|------|-----------|---------|
| `ISmsService.cs` | `ISmsService` | SMS OTP sending |
| `IEmailService.cs` | `IEmailService` | Email OTP sending |
| `IPaymentGatewayService.cs` | `IPaymentGatewayService` | Payment gateway abstraction |

#### Key DTOs

**TelegramUserDto** (User Profile):
```
TelegramUserId, Username, FirstName, LastName, PreferredLanguage,
IsRegistered, CleanChatMode, PhoneNumber, PhoneVerified, PhoneVerificationMethod,
IsVerified, VerificationPhotoFileId, Email, EmailVerified, Country,
KycStatus, KycRejectionData, RegisteredAt, FirstSeenAt, LastSeenAt,
Bio, GitHubUrl, LinkedInUrl, InstagramUrl
```

**ExchangeRequestDto** (Exchange):
```
Id, RequestNumber, TelegramUserId, Currency, TransactionType, DeliveryMethod,
AccountType, Country, Amount, ProposedRate, Description, FeePercent, FeeAmount,
TotalAmount, Status, ChannelMessageId, AdminNote, UserDisplayName,
DestinationCurrency, City, MeetingPreference, PaypalEmail, Iban, BankName,
CreatedAt, UpdatedAt
```

**BotStageDto** (Menu Stage):
```
Id, StageKey, TextFa, TextEn, IsEnabled, RequiredPermission, ParentStageKey, SortOrder
```

**BotStageButtonDto** (Menu Button):
```
Id, StageId, TextFa, TextEn, ButtonType, CallbackData, TargetStageKey,
Url, Row, Column, IsEnabled, RequiredPermission
```

---

### 4.2 Data Layer (`AbroadQs.Bot.Data`)

> **Purpose**: EF Core entities, DbContext, repositories, and migrations.

#### Database Tables (27 Total)

| Entity | Table | Purpose |
|--------|-------|---------|
| `TelegramUserEntity` | `TelegramUsers` | User profiles and KYC data |
| `SettingEntity` | `Settings` | Key-value application settings |
| `MessageEntity` | `Messages` | Telegram message log (in/out) |
| `UserMessageStateEntity` | `UserMessageStates` | Last bot message tracking per user |
| `BotStageEntity` | `BotStages` | Bot menu stages (dynamic) |
| `BotStageButtonEntity` | `BotStageButtons` | Buttons within stages |
| `PermissionEntity` | `Permissions` | Permission definitions |
| `UserPermissionEntity` | `UserPermissions` | User-permission assignments |
| `ExchangeRequestEntity` | `ExchangeRequests` | Currency exchange requests |
| `ExchangeRateEntity` | `ExchangeRates` | Cached exchange rates |
| `ExchangeGroupEntity` | `ExchangeGroups` | Telegram exchange groups |
| `AdBidEntity` | `AdBids` | Bids on exchange ads |
| `WalletEntity` | `Wallets` | User digital wallets |
| `WalletTransactionEntity` | `WalletTransactions` | Wallet transaction history |
| `PaymentEntity` | `Payments` | Payment gateway records |
| `CryptoWalletEntity` | `CryptoWallets` | Saved crypto wallet addresses |
| `CurrencyPurchaseEntity` | `CurrencyPurchases` | Currency purchase orders |
| `TicketEntity` | `Tickets` | Support tickets |
| `TicketMessageEntity` | `TicketMessages` | Ticket conversation messages |
| `StudentProjectEntity` | `StudentProjects` | Student project listings |
| `ProjectBidEntity` | `ProjectBids` | Project proposals/bids |
| `InternationalQuestionEntity` | `InternationalQuestions` | International Q&A questions |
| `QuestionAnswerEntity` | `QuestionAnswers` | Answers to questions |
| `SponsorshipRequestEntity` | `SponsorshipRequests` | Sponsorship requests |
| `SponsorshipEntity` | `Sponsorships` | Active sponsorships |
| `SystemMessageEntity` | `SystemMessages` | System notifications to users |

#### Entity Relationships

```
TelegramUserEntity (1) ──► (N) MessageEntity
TelegramUserEntity (1) ──► (1) UserMessageStateEntity
TelegramUserEntity (1) ──► (N) UserPermissionEntity
MessageEntity (1) ──► (1) MessageEntity (self-ref: ReplyToMessage)
BotStageEntity (1) ──► (N) BotStageButtonEntity
ExchangeRequestEntity (1) ──► (N) AdBidEntity
WalletEntity (1) ──► (N) WalletTransactionEntity
TicketEntity (1) ──► (N) TicketMessageEntity
StudentProjectEntity (1) ──► (N) ProjectBidEntity
InternationalQuestionEntity (1) ──► (N) QuestionAnswerEntity
SponsorshipRequestEntity (1) ──► (N) SponsorshipEntity
```

#### Key Entity: `TelegramUserEntity`

```csharp
Id                      // int (PK)
TelegramUserId          // long (Unique) - Telegram user ID
Username                // string? - @username
FirstName / LastName    // string? - Display name
PreferredLanguage       // string? - "fa" or "en"
IsRegistered            // bool - Completed registration
CleanChatMode           // bool (default: true) - Auto-delete messages
PhoneNumber             // string? - Phone number
PhoneVerified           // bool - Phone verified via OTP
PhoneVerificationMethod // string? - "sms_otp" or "manual"
IsVerified              // bool - Full KYC verification complete
VerificationPhotoFileId // string? - Telegram file ID of selfie
Email                   // string?
EmailVerified           // bool
Country                 // string? - Country of residence
KycStatus               // string? - "none", "pending_review", "approved", "rejected"
KycRejectionData        // string? - JSON with rejected fields and reasons
Bio                     // string?
GitHubUrl / LinkedInUrl / InstagramUrl  // string? - Social links
FirstSeenAt / LastSeenAt               // DateTimeOffset
RegisteredAt                            // DateTimeOffset?
```

#### Key Entity: `ExchangeRequestEntity`

```csharp
Id                  // int (PK)
RequestNumber       // int (Unique) - Human-readable number
TelegramUserId      // long
Currency            // string - e.g., "USD", "EUR"
TransactionType     // string - "buy", "sell", "exchange"
DeliveryMethod      // string - "bank", "cash", "paypal"
AccountType         // string? - Account type for bank transfers
Country             // string?
Amount              // decimal
ProposedRate        // decimal
Description         // string?
FeePercent          // decimal
FeeAmount           // decimal
TotalAmount         // decimal
Status              // string - "pending_approval", "approved", "rejected", "completed"
ChannelMessageId    // int? - Telegram channel message ID
AdminNote           // string?
UserDisplayName     // string?
DestinationCurrency // string? - For exchange type
City                // string? - For cash delivery
MeetingPreference   // string? - For cash delivery
PaypalEmail         // string? - For PayPal delivery
Iban / BankName     // string? - For bank delivery
```

#### Migrations History

| Migration | Date | Changes |
|-----------|------|---------|
| `Initial` | 2026-01-31 | Core tables (Users, Settings, Messages) |
| `AddSettings` | 2026-01-31 | Settings table |
| `AddMessagesAndUserMessageStates` | 2026-02-03 | Message tracking |
| `AddUserPreferredLanguage` | 2026-02-03 | Bilingual support |
| `AddBotStagesAndPermissions` | 2026-02-07 | Dynamic stages & permissions |
| `AddCleanChatMode` | 2026-02-08 | Clean chat toggle |
| `AddKycFields` | 2026-02-09 | Phone, verification fields |
| `AddKycExtraFields` | 2026-02-09 | Email, country, KYC status |
| `AddPhoneVerification` | 2026-02-11 | Phone verification method |
| `AddExchangeTables` | 2026-02-12 | Exchange requests, rates, groups |
| `AddGroupAdminNote` | 2026-02-12 | Admin notes for groups |
| `ComprehensiveUpgradePhases0to8` | 2026-02-12 | Wallets, tickets, projects, Q&A, sponsorships |
| `AddExchangeFlowFields` | 2026-02-13 | Extended exchange fields (city, IBAN, etc.) |
| `AddWalletsPaymentsBids` | 2026-02-14 | Wallets, payments, bids refinements |

#### `ApplicationDbContext.cs`

- Configures all 27 `DbSet<>` properties
- Defines relationships, indexes, and constraints in `OnModelCreating`
- Uses `decimal(18,4)` precision for financial amounts
- Delete behaviors: Cascade for owned entities, SetNull for references

#### `DesignTimeDbContextFactory.cs`

- Creates `ApplicationDbContext` for EF Core CLI tools
- Used by `dotnet ef migrations add` and `dotnet ef database update`

---

### 4.3 Application Layer (`AbroadQs.Bot.Application`)

> **Purpose**: Core routing/dispatching logic.

#### `UpdateDispatcher.cs`

The central routing engine:

1. **Receives** a raw `Telegram.Bot.Types.Update` object
2. **Builds** a `BotUpdateContext` (extracts ChatId, UserId, Text, CallbackData, etc.)
3. **Anti-spam**: Checks Redis for duplicate `update_id` and callback query locks
4. **Saves user**: Calls `ITelegramUserRepository.SaveOrUpdateAsync()`
5. **Saves message**: Logs incoming message to `IMessageRepository`
6. **Iterates handlers**: Loops through all registered `IUpdateHandler` instances
   - Calls `handler.CanHandle(context)` on each
   - First handler that returns `true` from `HandleAsync()` wins
   - Records handler name in `ProcessingContext`
7. **Priority**: Command-specific handlers run before generic ones

#### `ServiceCollectionExtensions.cs`

```csharp
services.AddBotApplication();              // Registers UpdateDispatcher
services.AddUpdateHandler<StartHandler>(); // Registers individual handlers
```

---

### 4.4 Telegram Layer (`AbroadQs.Bot.Telegram`)

> **Purpose**: Telegram Bot API implementation.

#### `TelegramResponseSender.cs`

Implements `IResponseSender`:

- **Sends** text messages, photos, inline/reply keyboards
- **Edits** existing messages (for inline keyboard updates)
- **Deletes** messages (for clean chat mode)
- **Silent keyboard removal**: Sends a zero-width space (`\u200B`) message with `ReplyKeyboardRemove`, then immediately deletes it - making keyboard removal invisible
- **Silent keyboard update**: Sends a Braille pattern char (`\u2800`) with new keyboard, deletes old message
- **Logs** all outgoing messages to `IMessageRepository`
- **Updates** user message state in `IUserMessageStateRepository`
- **Tracks** `ProcessingContext.ResponseSent`

#### `ServiceCollectionExtensions.cs`

```csharp
services.AddTelegramBot(botToken); // Registers ITelegramBotClient + IResponseSender
```

---

### 4.5 Modules Layer (`AbroadQs.Bot.Modules.Common`)

> **Purpose**: All bot business logic handlers.

#### Handler Registration Order (in `CommonModule.cs`)

Handlers are registered in priority order. The first matching handler wins:

```
1.  KycStateHandler           # Multi-step KYC (highest priority for state-based flows)
2.  ProfileStateHandler       # Profile editing flows
3.  ExchangeStateHandler      # Exchange request flows
4.  FinanceHandler            # Wallet, payments, crypto
5.  CurrencyPurchaseHandler   # Direct currency purchase
6.  GroupStateHandler          # Exchange groups
7.  BidStateHandler            # Exchange ad bidding
8.  InternationalQuestionHandler # International Q&A
9.  StudentProjectHandler      # Student projects
10. SponsorshipHandler         # Sponsorship
11. TicketHandler              # Support tickets
12. MyMessagesHandler          # System messages
13. MyProposalsHandler         # User's bids/proposals
14. StartHandler               # /start command
15. HelpHandler                # /help command
16. DynamicStageHandler        # Dynamic stage navigation (catch-all for buttons)
17. UnknownCommandHandler      # Fallback (last resort)
```

#### Handler Details

##### `StartHandler.cs`
- **Triggers**: `/start` command
- **Deep links**: `/start bid_{requestId}` delegates to `BidStateHandler`
- **New users**: Shows welcome message + language selection (inline buttons: `lang:fa`, `lang:en`)
- **Returning users**: Shows main menu (reply keyboard)
- **Side effects**: Grants default permission, tracks reply stage

##### `DynamicStageHandler.cs` (The Core Router)
- **Triggers**: Stage callbacks, language changes, settings toggles, text button presses
- **Callbacks handled**:
  - `stage:{key}` - Navigate to a stage from database
  - `lang:{code}` - Change language
  - `toggle:clean_chat` - Toggle clean chat mode
  - `start_kyc` - Begin KYC flow
  - `exc_hist:*` - Exchange history navigation
  - `exc_rates:*` - Exchange rates display
  - `exc_grp:*` - Exchange groups
  - `noop` - No operation (placeholder buttons)
- **Text handling**: Matches reply keyboard button text against stage buttons
- **Delegates to**: Other handlers based on conversation state (`exc_`, `kyc_`, etc.)
- **Message transitions**: Handles inline-to-reply, reply-to-inline, reply-to-reply transitions cleanly

##### `KycStateHandler.cs` (Identity Verification)
- **Flow**: Name -> Phone -> Email -> Country -> Photo -> Submit for Review
- **Phone verification**:
  - Iranian numbers (+98): SMS OTP via sms.ir
  - Non-Iranian: Manual verification via support
- **Email verification**: Collects email (OTP currently skipped due to SMTP port blocking)
- **Country selection**: Top 12 countries as inline buttons + "Other" (free text)
- **Photo**: Selfie with "AbroadQs" paper, sample photo sent first
- **Controls**: Cancel button (`cancel_kyc`), Skip buttons (`skip_email`, `skip_country`)
- **Cleanup**: Deletes messages at each step transition
- **States**: `kyc_step_name`, `kyc_step_phone`, `kyc_step_otp:{code}`, `kyc_step_email`, `kyc_step_email_otp:{code}`, `kyc_step_country`, `kyc_step_country_text`, `kyc_step_photo`

##### `ExchangeStateHandler.cs` (Currency Exchange)
- **Flow (Buy/Sell)**: Currency -> Amount -> Delivery -> [Delivery-specific fields] -> Rate -> Description -> Preview -> Confirm
- **Flow (Exchange)**: Source Currency -> Dest Currency -> Amount -> Countries -> City -> Meeting -> Rate -> Description -> Preview -> Confirm
- **Delivery methods**: Bank (IBAN, bank name), PayPal (email), Cash (country, city, meeting preference)
- **Rate validation**: Custom rates must be within +/-10% of market rate
- **States**: `exc_currency`, `exc_type`, `exc_amount`, `exc_delivery`, `exc_rate`, `exc_desc`, `exc_preview`, etc.

##### `FinanceHandler.cs` (Wallet & Payments)
- **Features**: Balance check, charge via BitPay, transfer to users, transaction/payment history
- **Callbacks**: `fin_menu`, `fin_balance`, `fin_charge`, `fin_transfer`, `fin_history`, `fin_payments`
- **States**: `fin_charge_amount`, `fin_transfer_user`, `fin_transfer_amount`

##### `BidStateHandler.cs` (Exchange Ad Bidding)
- **Entry**: `/start bid_{requestId}` deep link from channel
- **Flow**: Amount -> Rate -> Message -> Preview -> Confirm
- **Ad owner actions**: Accept/reject bids via inline buttons
- **States**: `bid_amount`, `bid_rate`, `bid_message`, `bid_preview`

##### `ProfileStateHandler.cs` (User Profile)
- **View**: Profile info, completion percentage, verification status
- **Edit**: Bio, GitHub, LinkedIn, Instagram links
- **Public profile**: Other users can view via `view_profile:{userId}`
- **States**: `awaiting_profile_bio`, `awaiting_profile_github`, etc.

##### `GroupStateHandler.cs` (Exchange Groups)
- **Features**: Browse groups (filter by currency/country), submit new groups
- **Flow**: Link -> Type -> Currency/Country -> Description -> Preview -> Submit
- **States**: `grp_submit_link`, `grp_submit_type`, etc.

##### `InternationalQuestionHandler.cs` (Q&A)
- **Features**: Post questions (with optional bounty), browse, answer, accept answers
- **Callbacks**: `iq_menu`, `iq_post`, `iq_browse`, `iq_my`, `iq_answer:{id}`

##### `StudentProjectHandler.cs` (Projects)
- **Features**: Post projects, browse, submit proposals, accept/reject proposals
- **Callbacks**: `proj_menu`, `proj_post`, `proj_browse`, `proj_bid:{id}`

##### `SponsorshipHandler.cs` (Sponsorships)
- **Features**: Request sponsorship (with project collateral), browse, fund projects
- **Callbacks**: `sp_menu`, `sp_request`, `sp_browse`, `sp_fund:{id}`

##### `TicketHandler.cs` (Support)
- **Features**: Create tickets, view ticket list, reply to tickets
- **Callbacks**: `tkt_menu`, `tkt_new`, `tkt_list`, `tkt_reply:{id}`

##### `MyMessagesHandler.cs` (System Messages)
- **Features**: View unread/all system messages, mark as read
- **Callbacks**: `msg_menu`, `msg_unread`, `msg_all`, `msg_read:{id}`

##### `MyProposalsHandler.cs` (User's Proposals)
- **Features**: View exchange bids and project proposals submitted by the user
- **Callbacks**: `myprop_menu`, `myprop_exchange`, `myprop_project`

##### `CurrencyPurchaseHandler.cs` (Crypto Purchases)
- **Features**: Buy crypto, manage wallets, purchase history
- **Callbacks**: `cp_menu`, `cp_buy`, `cp_wallets`, `cp_add_wallet`

##### `HelpHandler.cs`
- **Triggers**: `/help` command
- **Action**: Sends help text with available commands

##### `UnknownCommandHandler.cs`
- **Triggers**: Any unmatched text message (fallback)
- **Action**: "Command not recognized" message

---

### 4.6 Host/Webhook Layer (`AbroadQs.Bot.Host.Webhook`)

> **Purpose**: Main application entry point, service configuration, REST API.

#### `Program.cs` - Application Entry Point

**Startup sequence**:

1. Load configuration (appsettings.json, environment variables)
2. Load bot token (DB -> appsettings.Token.json -> appsettings.json -> env)
3. Register services:
   - `ApplicationDbContext` (SQL Server)
   - All repositories
   - Redis (ConnectionMultiplexer)
   - RabbitMQ (publisher + consumer)
   - Telegram bot client
   - SMS service (sms.ir)
   - Email service (HTTP relay + SMTP fallback)
   - Payment gateway (BitPay)
   - Background services (polling, rate refresh)
   - All update handlers
4. Apply EF migrations (`MigrateAsync`)
5. Seed default data (`SeedDefaultDataAsync`)
6. Configure webhook (if Webhook mode)
7. Start background services

**REST API Endpoints** (for Dashboard):

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/webhook` | POST | Telegram webhook receiver |
| `/dashboard` | GET | Serve admin panel HTML |
| `/api/bot/status` | GET | Bot status and connection info |
| `/api/bot/toggle` | POST | Start/stop bot |
| `/api/bot/webhook` | POST | Set/update webhook URL |
| `/api/bot/token` | POST | Update bot token |
| `/api/bot/updates` | GET | Recent webhook log |
| `/api/users` | GET | List all users |
| `/api/users/{id}/messages` | GET | User message history |
| `/api/kyc/pending` | GET | Pending KYC submissions |
| `/api/kyc/{userId}/approve` | POST | Approve KYC |
| `/api/kyc/{userId}/reject` | POST | Reject KYC (with reasons) |
| `/api/stages` | GET/POST | List/create stages |
| `/api/stages/{id}` | PUT/DELETE | Update/delete stages |
| `/api/stages/{key}/buttons` | GET/POST | List/create buttons |
| `/api/stages/buttons/{id}` | PUT/DELETE | Update/delete buttons |
| `/api/permissions` | GET/POST | List/create permissions |
| `/api/permissions/{key}` | DELETE | Delete permission |
| `/api/permissions/user/{userId}` | GET | User's permissions |
| `/api/permissions/grant` | POST | Grant permission |
| `/api/permissions/revoke` | POST | Revoke permission |
| `/api/settings/{key}` | GET/PUT | Get/set settings |
| `/api/exchange-requests` | GET | List exchange requests |
| `/api/exchange-requests/{id}/approve` | POST | Approve request |
| `/api/exchange-requests/{id}/reject` | POST | Reject request |
| `/api/exchange-rates` | GET | List exchange rates |
| `/api/exchange-rates/fetch` | POST | Fetch from Navasan API |
| `/api/exchange-rates/{code}` | PUT | Update rate manually |
| `/api/exchange-groups` | GET | List exchange groups |
| `/api/exchange-groups/{id}/approve` | POST | Approve group |
| `/api/exchange-groups/{id}/reject` | POST | Reject group |
| `/api/bids/request/{requestId}` | GET | Bids for a request |
| `/api/tickets` | GET | List tickets |
| `/api/tickets/{id}` | GET | Ticket detail |
| `/api/tickets/{id}/reply` | POST | Admin reply to ticket |
| `/api/projects` | GET | List projects |
| `/api/projects/{id}/approve` | POST | Approve project |
| `/api/questions` | GET | List questions |
| `/health` | GET | Health check |

#### Services

##### `ProcessingContext.cs`
- **Scoped** service tracking per-request state
- Properties: `Source`, `RedisAccessed`, `RabbitMqPublished`, `SqlAccessed`, `ResponseSent`, `HandlerName`

##### `RabbitMqPublisher.cs` / `RabbitMqConsumerService.cs`
- Publisher: Serializes `Update` to JSON, publishes to queue
- Consumer: Background service reading from queue (currently for logging only)
- Auto-reconnect on connection failures

##### `RedisUserConversationStateStore.cs`
- Redis keys:
  - `user:state:{userId}` (TTL: 1 hour) - Conversation state
  - `user:replystage:{userId}` (TTL: 24 hours) - Current reply keyboard stage
  - `user:flowmsgs:{userId}` (TTL: 2 hours) - Message IDs for cleanup
  - `user:flowdata:{userId}:{key}` (TTL: 2 hours) - Flow key-value data

##### `RedisUserLastCommandStore.cs`
- Redis key: `user:lastcmd:{userId}` (TTL: 30 days)
- Used for anti-spam deduplication

##### `SmsIrService.cs`
- Uses `IPE.SmsIrClient` NuGet package
- Normalizes Iranian phone numbers to `0XXXXXXXXX` format
- Template-based OTP via `VerifySendAsync`

##### `EmailOtpService.cs`
- **Primary**: HTTP POST to `https://abroadqs.com/api/email_relay.php`
- **Fallback**: Direct SMTP via MailKit (port 465, implicit SSL)
- **Timeout**: 15 seconds hard limit to prevent hangs
- **Note**: Currently skipped due to SMTP port blocking on Hetzner VPS

##### `NavasanApiService.cs`
- Fetches rates from `https://api.navasan.tech/latest/`
- Monthly limit: 120 calls (tracks in settings)
- Maps Navasan currency codes to internal codes

##### `BitPayService.cs` / `BitPayPaymentGatewayAdapter.cs`
- BitPay.ir payment gateway
- Creates payment links, verifies callbacks
- Adapter converts between BitPay API and `IPaymentGatewayService` interface

##### `RateAutoRefreshService.cs`
- Background service distributing 110 API calls evenly across the month
- Calculates optimal interval based on remaining budget and days

##### `GetUpdatesPollingService.cs`
- Alternative to webhook mode (for development)
- Long polling with configurable timeout (1-50s)
- Only active when `UpdateMode = "GetUpdates"`

##### `BotStatusService.cs` / `UpdateLogService.cs`
- Bot start/stop state management
- In-memory log of last 50 updates (for dashboard display)

##### `PlaceholderTelegramBotClient.cs`
- No-op Telegram client when token is not configured
- Prevents startup crashes, allows dashboard to configure token

##### NoOp Services
- `NoOpUserConversationStateStore` - Returns null/empty
- `NoOpUserLastCommandStore` - Returns null
- `NoOpTelegramUserRepository` - Returns null/empty
- `NoOpExchangeRepository` - Returns null/empty
- `NoOpPermissionRepository` - Returns true for all permission checks
- `NoOpBotStageRepository` - Returns null/empty

#### Static Files

| File | Purpose |
|------|---------|
| `wwwroot/dashboard/index.html` | Admin dashboard (single-page app) |
| `wwwroot/css/site.css` | Custom CSS |
| `wwwroot/kyc_sample_photo.png` | Sample KYC selfie photo |

---

### 4.7 Tunnel System

> **Purpose**: WebSocket-based tunneling for development (like ngrok).

#### `AbroadQs.TunnelServer` (runs on server)

- Listens on port **9080**
- WebSocket endpoint at `/tunnel` for client connections
- Forwards all HTTP requests to connected tunnel client via WebSocket
- Returns 503 if no client connected
- Health check at `/` and `/health`

#### `AbroadQs.TunnelClient` (runs on local machine)

- Connects to tunnel server via WebSocket
- Receives forwarded requests
- Forwards to local backend (default: `localhost:5252`)
- Sends responses back to server
- Auto-reconnects on disconnect
- CLI args: `--url` (server URL), `--local` (local port)

#### Use Case

When developing locally, run the bot on your machine and use the tunnel to receive webhooks from Telegram:

```
[Telegram] -> [Nginx] -> [TunnelServer:9080] -> [WebSocket] -> [TunnelClient] -> [Local Bot:5252]
```

---

## 5. Database Schema

### Entity Relationship Diagram (Simplified)

```
┌──────────────────────────────────┐
│        TelegramUserEntity        │
│  PK: Id                          │
│  UK: TelegramUserId              │
│  Fields: Username, Name, KYC...  │
└──────────────┬───────────────────┘
               │
    ┌──────────┼──────────┬────────────┬──────────────┐
    │          │          │            │              │
    ▼          ▼          ▼            ▼              ▼
Messages  UserMsgState  UserPerms  ExchRequests   Wallets
    │                                  │              │
    │                                  ▼              ▼
    │                              AdBids      WalletTransactions
    │
    ▼ (self-ref)
ReplyToMessage

┌───────────────┐     ┌──────────────┐
│  BotStages    │────►│ BotStageBtns │
└───────────────┘     └──────────────┘

┌───────────────┐     ┌──────────────┐
│   Tickets     │────►│ TicketMsgs   │
└───────────────┘     └──────────────┘

┌───────────────┐     ┌──────────────┐
│ StudentProjs  │────►│ ProjectBids  │
└───────────────┘     └──────────────┘

┌───────────────┐     ┌──────────────┐
│  IntlQuestions│────►│ QuestAnswers │
└───────────────┘     └──────────────┘

┌───────────────┐     ┌──────────────┐
│ SponsorReqs   │────►│ Sponsorships │
└───────────────┘     └──────────────┘
```

---

## 6. Bot Features & Handlers

### Bot Menu Structure

```
/start
├── Main Menu (ReplyKeyboard)
│   ├── ثبت درخواست (New Request)
│   │   ├── تبادل مالی دانشجویی (Student Exchange)
│   │   │   ├── ثبت درخواست تبادل (Submit Exchange)
│   │   │   │   ├── خرید ارز (Buy Currency) *
│   │   │   │   ├── فروش ارز (Sell Currency) *
│   │   │   │   ├── تبادل (Exchange) *
│   │   │   │   └── بازگشت (Back)
│   │   │   ├── تبادلات من (My Exchanges)
│   │   │   ├── گروه های تبادل (Exchange Groups)
│   │   │   ├── نرخ ارز ها (Exchange Rates)
│   │   │   ├── شرایط و راهنما (Terms & Guide)
│   │   │   └── بازگشت (Back)
│   │   ├── سوال بین الملل (International Q&A)
│   │   ├── پروژه دانشجویی (Student Projects)
│   │   ├── حامی مالی (Financial Sponsor)
│   │   └── بازگشت (Back)
│   ├── پروفایل من (My Profile) [InlineKeyboard]
│   │   ├── View profile info
│   │   ├── Edit bio/links
│   │   └── Start KYC
│   ├── کیف پول (Wallet) [InlineKeyboard]
│   │   ├── Balance
│   │   ├── Charge (BitPay)
│   │   ├── Transfer
│   │   └── History
│   ├── پیام های من (My Messages)
│   ├── پیشنهادهای من (My Proposals)
│   ├── تیکت پشتیبانی (Support Ticket)
│   ├── تنظیمات (Settings) [InlineKeyboard]
│   │   ├── Language (fa/en)
│   │   └── Clean Chat Mode (on/off)
│   └── راهنما (Help)

* Requires KYC verification
```

### Callback Data Patterns

| Prefix | Handler | Description |
|--------|---------|-------------|
| `stage:` | DynamicStageHandler | Navigate to stage |
| `lang:` | DynamicStageHandler | Language change |
| `toggle:` | DynamicStageHandler | Settings toggle |
| `start_kyc` | KycStateHandler | Start KYC |
| `cancel_kyc` | KycStateHandler | Cancel KYC |
| `skip_email` | KycStateHandler | Skip email step |
| `skip_country` | KycStateHandler | Skip country step |
| `country:` | KycStateHandler | Country selection |
| `start_kyc_fix` | KycStateHandler | Fix rejected KYC |
| `phone_manual_continue` | KycStateHandler | Continue after manual phone check |
| `profile_edit:` | ProfileStateHandler | Profile editing |
| `view_profile:` | ProfileStateHandler | View public profile |
| `exc_hist:` | DynamicStageHandler | Exchange history |
| `exc_rates:` | DynamicStageHandler | Exchange rates |
| `exc_grp:` | DynamicStageHandler | Exchange groups |
| `exc_confirm` | ExchangeStateHandler | Confirm exchange |
| `exc_cancel` | ExchangeStateHandler | Cancel exchange |
| `fin_` | FinanceHandler | Finance/wallet |
| `cp_` | CurrencyPurchaseHandler | Crypto purchase |
| `grp_` | GroupStateHandler | Group management |
| `bid_` | BidStateHandler | Bidding |
| `iq_` | InternationalQuestionHandler | Questions |
| `proj_` | StudentProjectHandler | Projects |
| `sp_` | SponsorshipHandler | Sponsorships |
| `tkt_` | TicketHandler | Tickets |
| `msg_` | MyMessagesHandler | Messages |
| `myprop_` | MyProposalsHandler | Proposals |

### Conversation States

| State Pattern | Handler | Description |
|---------------|---------|-------------|
| `kyc_step_*` | KycStateHandler | KYC flow steps |
| `awaiting_profile_*` | ProfileStateHandler | Profile editing |
| `exc_*` | ExchangeStateHandler | Exchange request flow |
| `fin_*` | FinanceHandler | Wallet operations |
| `cp_*` | CurrencyPurchaseHandler | Crypto purchase |
| `grp_submit_*` | GroupStateHandler | Group submission |
| `bid_*` | BidStateHandler | Bidding flow |
| `iq_*` | InternationalQuestionHandler | Question posting |
| `proj_*` | StudentProjectHandler | Project posting/bidding |
| `sp_*` | SponsorshipHandler | Sponsorship flow |
| `tkt_*` | TicketHandler | Ticket creation |

---

## 7. Services & Integrations

### SMS (sms.ir)

| Setting | Value |
|---------|-------|
| API Token | `ZxpWSZ0nSgVcqRGecTPGS0KGltods6GJhfZSGyVUjLuEGXks` |
| Template ID | `168094` |
| NuGet Package | `IPE.SmsIr` |

### Email (HTTP Relay)

| Setting | Value |
|---------|-------|
| Relay URL | `https://abroadqs.com/api/email_relay.php` |
| Secret Token | `AbroadQs_Email_Relay_Secret_2026!` |
| SMTP Host | `abroadqs.com` |
| SMTP Port | `465` (SSL) |
| SMTP User | `info@abroadqs.com` |
| SMTP Pass | `Kia135724!` |
| Status | HTTP relay needs PHP file upload; SMTP blocked by Hetzner |

### Payment Gateway (BitPay.ir)

| Setting | Value |
|---------|-------|
| API ID | Stored in DB settings (`bitpay_api_id`) |
| Mode | Test/Production (configurable) |
| Redirect | Configurable base URL |

### Exchange Rates (Navasan)

| Setting | Value |
|---------|-------|
| API URL | `https://api.navasan.tech/latest/` |
| API Key | Stored in DB settings (`navasan_api_key`) |
| Monthly Limit | 120 calls (110 auto, 10 manual) |

### Redis Keys

| Key Pattern | TTL | Purpose |
|-------------|-----|---------|
| `user:state:{userId}` | 1 hour | Conversation state |
| `user:replystage:{userId}` | 24 hours | Current reply keyboard stage |
| `user:flowmsgs:{userId}` | 2 hours | Flow message IDs for cleanup |
| `user:flowdata:{userId}:{key}` | 2 hours | Flow key-value data |
| `user:lastcmd:{userId}` | 30 days | Anti-spam deduplication |
| `update_lock:{updateId}` | 1 min | Update deduplication |
| `cb_lock:{callbackQueryId}` | 5 sec | Callback query deduplication |

---

## 8. Dashboard (Admin Panel)

Access: `https://webhook.abroadqs.com/dashboard`

### Sections

| Section | Features |
|---------|----------|
| **Overview** | Stats cards: pending requests, active bids, users, KYC, bot status |
| **Users** | List all users, view profile details, message history |
| **KYC Review** | Pending submissions, approve/reject with reasons, photo viewing |
| **Exchange Requests** | List/filter by status, approve/reject, view details & bids |
| **Ad Pricing** | Configure free/paid mode, commission, payment methods |
| **Exchange Groups** | Manage groups, approve/reject submissions |
| **Exchange Rates** | View/edit rates, fetch from Navasan, track API usage |
| **Bot Operations** | Token config, webhook setup, mode selection, start/stop, logs |
| **Bot Stages** | Dynamic stage/button management (CRUD) |
| **Permissions** | Define permissions, assign to users |
| **Channel Settings** | Exchange channel ID configuration |
| **Tickets** | Ticket management, admin replies |
| **Projects** | Project management, approval |
| **Questions** | Q&A management |

### Technologies

- **Bootstrap 5** - UI framework
- **SweetAlert2** - Modal dialogs and confirmations
- **Vanilla JavaScript** - No frameworks, direct API calls
- **Fetch API** - REST API communication

---

## 9. Configuration

### `appsettings.json` (Default)

```json
{
  "Telegram": {
    "BotToken": "<YOUR_BOT_TOKEN>",
    "WebhookUrl": "",
    "UpdateMode": "Webhook",          // "Webhook" or "GetUpdates"
    "PollingTimeoutSeconds": 25
  },
  "RabbitMQ": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest",
    "QueueName": "telegram-updates"
  },
  "Redis": {
    "Configuration": "localhost:6379"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost,1433;Database=AbroadQsBot;User Id=sa;Password=YourStrong@Pass123;TrustServerCertificate=True;"
  }
}
```

### `appsettings.Development.json` (Local Dev)

- Uses `GetUpdates` mode (no webhook needed)
- Connects to remote server's Redis/RabbitMQ/SQL (or local Docker)

### `appsettings.Token.json` (Git-ignored)

- Stores the actual bot token locally
- Not committed to version control

### Environment Variables (Docker)

Set in `docker-compose.server.yml`:

```yaml
ConnectionStrings__DefaultConnection: "Server=sqlserver,1433;..."
RabbitMQ__HostName: rabbitmq
Redis__Configuration: redis:6379
PUBLIC_WEBHOOK_URL: "https://webhook.abroadqs.com/webhook"
```

### Database Settings (Runtime)

Stored in `Settings` table, managed via dashboard:

| Key | Description |
|-----|-------------|
| `Telegram.BotToken` | Bot token (highest priority) |
| `Telegram.WebhookUrl` | Webhook URL |
| `Telegram.UpdateMode` | Webhook or GetUpdates |
| `navasan_api_key` | Navasan API key |
| `navasan_calls_month` | Current month API call count |
| `navasan_calls_limit` | Monthly limit (default: 120) |
| `bitpay_api_id` | BitPay API ID |
| `bitpay_test_mode` | Test/Production mode |
| `ad_pricing_mode` | "free" or "paid" |
| `ad_pricing_amount` | Price per ad (Rials) |
| `exchange_channel_id` | Telegram channel for exchange ads |
| `base_url` | Base URL for payment callbacks |

---

## 10. Docker & Infrastructure

### `docker-compose.server.yml` (Production)

| Service | Image | Port | Purpose |
|---------|-------|------|---------|
| `rabbitmq` | `rabbitmq:3-management-alpine` | 5672, 15672 | Message queue |
| `redis` | `redis:7-alpine` | 6379 | Cache/state |
| `sqlserver` | `mssql/server:2022-latest` | 1433 | Database |
| `tunnel` | Custom build | 9080 (localhost) | WebSocket tunnel |
| `bot` | Custom build | 5252 | Main bot application |

### `docker-compose.yml` (Local Development)

Only infrastructure services (RabbitMQ, Redis, SQL Server). Bot runs directly from IDE.

### Docker Volumes

| Volume | Purpose |
|--------|---------|
| `rabbitmq-data` | RabbitMQ persistent data |
| `redis-data` | Redis AOF/RDB snapshots |
| `sqlserver-data` | SQL Server databases |

### Container Dependencies

```
rabbitmq (healthy) ─┐
redis (healthy) ────┤──► bot
sqlserver (started) ─┘
                         tunnel (no dependencies)
```

### Nginx Configuration

```nginx
server {
    listen 443 ssl;
    server_name webhook.abroadqs.com;

    ssl_certificate /etc/letsencrypt/live/webhook.abroadqs.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/webhook.abroadqs.com/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:9080;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_read_timeout 86400;
        proxy_send_timeout 86400;
    }
}
```

---

## 11. Deployment Guide

### Prerequisites

**On your local PC:**
- Python 3.x with `paramiko` package
- Git access to the repository

**On the server:**
- Docker & Docker Compose installed
- Nginx + Certbot installed
- Git clone of the repository at `/root/AbroadQs.TelegramBot/`
- DNS: `webhook.abroadqs.com` pointing to server IP

### Server Details

| Setting | Value |
|---------|-------|
| **IP** | `167.235.159.117` |
| **SSH Port** | `2200` |
| **User** | `root` |
| **Provider** | Hetzner |
| **OS** | Ubuntu/Debian |

### Deploy Script (`_deploy.py`)

```bash
# Full deploy (pull + build + restart)
python _deploy.py

# Show bot logs
python _deploy.py --logs
python _deploy.py --logs 100

# Container and server status
python _deploy.py --status

# Restart without rebuild
python _deploy.py --restart

# Rebuild everything (including infra)
python _deploy.py --rebuild-all

# Rollback to previous commit
python _deploy.py --rollback

# Full health check
python _deploy.py --health

# Run custom command on server
python _deploy.py --shell "df -h"
python _deploy.py --shell "docker logs abroadqs-bot --tail 50"
```

### Deployment Steps (What `_deploy.py` Does)

1. **SSH** to server (`167.235.159.117:2200`)
2. **Git pull** latest code from `origin/main`
3. **Docker build** bot and tunnel images
4. **Docker up** restart bot and tunnel containers
5. **Wait** 10 seconds for startup
6. **Verify** container status and check for errors

### Manual Deployment (On Server)

```bash
cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot

# Pull latest
git pull origin main

# Build and restart
docker compose -f docker-compose.server.yml build bot tunnel
docker compose -f docker-compose.server.yml up -d bot tunnel

# Check status
docker compose -f docker-compose.server.yml ps

# View logs
docker logs abroadqs-bot --tail 50 -f
```

### First-Time Server Setup

```bash
# 1. Clone repository
git clone https://github.com/ThatsKiarash/AbroadQs.TelegramBot.git /root/AbroadQs.TelegramBot

# 2. Start all services
cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot
docker compose -f docker-compose.server.yml up -d

# 3. Setup Nginx + SSL
bash scripts/setup-webhook-ssl.sh

# 4. Update Nginx for tunnel
bash scripts/update-nginx-tunnel.sh

# 5. Configure bot token via dashboard
# Go to https://webhook.abroadqs.com/dashboard
# Set bot token in Bot Operations section
# Set webhook URL to https://webhook.abroadqs.com/webhook
```

### What Must Be on GitHub

- All source code (`src/` directory)
- Docker files (`docker-compose.server.yml`, `Dockerfile`s)
- Scripts (`scripts/` directory)
- Configuration templates (`appsettings.json`)

### What Must NOT Be on GitHub

- `appsettings.Token.json` (contains real bot token)
- `appsettings.*.local.json` (local overrides)
- Build outputs (`bin/`, `obj/`, `publish/`)
- IDE files (`.vs/`, `.idea/`)

### What Exists Only on Server

- Docker volumes (database data, Redis data, RabbitMQ data)
- Nginx configuration (`/etc/nginx/sites-available/webhook-abroadqs`)
- SSL certificates (`/etc/letsencrypt/live/webhook.abroadqs.com/`)
- Bot token in database `Settings` table

---

## 12. Troubleshooting

### Common Issues

#### Bot not responding
```bash
# Check if container is running
python _deploy.py --status

# Check logs for errors
python _deploy.py --logs 100

# Restart bot
python _deploy.py --restart
```

#### Database migration error
```bash
# Check logs for migration errors
python _deploy.py --shell "docker logs abroadqs-bot 2>&1 | grep -i migration"

# If stuck, recreate bot container
python _deploy.py --shell "cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && docker compose -f docker-compose.server.yml up -d --force-recreate bot"
```

#### Webhook not working
```bash
# Check Nginx status
python _deploy.py --shell "systemctl status nginx"

# Check SSL certificate
python _deploy.py --shell "certbot certificates"

# Test webhook endpoint locally
python _deploy.py --shell "curl -s http://127.0.0.1:5252/health"

# Test via tunnel
python _deploy.py --shell "curl -s http://127.0.0.1:9080/health"
```

#### Redis/RabbitMQ connection issues
```bash
# Check infra containers
python _deploy.py --shell "docker compose -f /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot/docker-compose.server.yml ps"

# Restart all infra
python _deploy.py --rebuild-all
```

#### Disk space issues
```bash
# Check disk space
python _deploy.py --shell "df -h"

# Clean Docker
python _deploy.py --shell "docker system prune -f"
python _deploy.py --shell "docker image prune -a -f"
```

#### Email not sending
- **Root cause**: Hetzner blocks outgoing SMTP ports (25, 465, 587)
- **Solution**: Upload `email_relay.php` to `https://abroadqs.com/api/email_relay.php`
- **Current workaround**: Email OTP is skipped; email is collected but not verified

#### SSL certificate renewal
```bash
# Certificates auto-renew via certbot timer
# Manual renewal if needed:
python _deploy.py --shell "certbot renew --force-renewal && systemctl reload nginx"
```

### Useful Commands

```bash
# Live bot logs
python _deploy.py --shell "docker logs abroadqs-bot -f --tail 20"

# Enter bot container
python _deploy.py --shell "docker exec -it abroadqs-bot bash"

# Check SQL Server
python _deploy.py --shell "docker exec -it abroadqs-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'YourStrong@Pass123' -C -Q 'SELECT COUNT(*) FROM AbroadQsBot.dbo.TelegramUsers'"

# Check Redis
python _deploy.py --shell "docker exec -it abroadqs-redis redis-cli DBSIZE"

# Check RabbitMQ
python _deploy.py --shell "docker exec -it abroadqs-rabbitmq rabbitmqctl list_queues"
```

---

## Appendix: File Reference

### All Source Files

<details>
<summary>Click to expand full file list</summary>

#### AbroadQs.Bot.Contracts (27 files)
| File | Purpose |
|------|---------|
| `BilingualHelper.cs` | Farsi/English text helper |
| `BotUpdateContext.cs` | Parsed Telegram update |
| `IBidRepository.cs` | Exchange ad bids |
| `IBotStageRepository.cs` | Bot stages and buttons |
| `ICryptoWalletRepository.cs` | Crypto wallets |
| `IEmailService.cs` | Email OTP |
| `IExchangeRepository.cs` | Exchange requests and rates |
| `IGroupRepository.cs` | Exchange groups |
| `IInternationalQuestionRepository.cs` | International Q&A |
| `IMessageRepository.cs` | Message logging |
| `IModuleMarker.cs` | Module registration marker |
| `IPaymentGatewayService.cs` | Payment gateway |
| `IPermissionRepository.cs` | Permissions |
| `IProcessingContext.cs` | Request tracking |
| `IResponseSender.cs` | Telegram response abstraction |
| `ISettingsRepository.cs` | Key-value settings |
| `ISmsService.cs` | SMS OTP |
| `ISponsorshipRepository.cs` | Sponsorships |
| `IStudentProjectRepository.cs` | Student projects |
| `ISystemMessageRepository.cs` | System messages |
| `ITelegramUserRepository.cs` | User data |
| `ITicketRepository.cs` | Support tickets |
| `IUpdateHandler.cs` | Handler interface |
| `IUserConversationStateStore.cs` | Conversation state (Redis) |
| `IUserLastCommandStore.cs` | Last command (Redis) |
| `IUserMessageStateRepository.cs` | Message state (SQL) |
| `IWalletRepository.cs` | Wallets and payments |

#### AbroadQs.Bot.Data (37+ files)
| File | Purpose |
|------|---------|
| `ApplicationDbContext.cs` | EF Core DbContext (27 DbSets) |
| `DesignTimeDbContextFactory.cs` | Migration factory |
| `TelegramUserEntity.cs` | User entity |
| `TelegramUserRepository.cs` | User repository |
| `BotStageEntity.cs` | Stage entity |
| `BotStageButtonEntity.cs` | Button entity |
| `BotStageRepository.cs` | Stage repository |
| `MessageEntity.cs` | Message entity |
| `MessageRepository.cs` | Message repository |
| `SettingEntity.cs` | Setting entity |
| `SettingsRepository.cs` | Settings repository |
| `PermissionEntity.cs` | Permission entity |
| `UserPermissionEntity.cs` | User-permission entity |
| `PermissionRepository.cs` | Permission repository |
| `ExchangeRequestEntity.cs` | Exchange request entity |
| `ExchangeRateEntity.cs` | Exchange rate entity |
| `ExchangeGroupEntity.cs` | Exchange group entity |
| `ExchangeRepository.cs` | Exchange repository |
| `GroupRepository.cs` | Group repository |
| `AdBidEntity.cs` | Ad bid entity |
| `BidRepository.cs` | Bid repository |
| `WalletEntity.cs` | Wallet + Transaction + Payment entities |
| `WalletRepository.cs` | Wallet repository |
| `CryptoWalletEntity.cs` | Crypto wallet + Purchase entities |
| `CryptoWalletRepository.cs` | Crypto wallet repository |
| `TicketEntity.cs` | Ticket + Message entities |
| `TicketRepository.cs` | Ticket repository |
| `StudentProjectEntity.cs` | Project + Bid entities |
| `StudentProjectRepository.cs` | Project repository |
| `InternationalQuestionEntity.cs` | Question + Answer entities |
| `InternationalQuestionRepository.cs` | Question repository |
| `SponsorshipEntity.cs` | Sponsorship entities |
| `SponsorshipRepository.cs` | Sponsorship repository |
| `SystemMessageEntity.cs` | System message entity |
| `SystemMessageRepository.cs` | System message repository |
| `UserMessageStateEntity.cs` | Message state entity |
| `UserMessageStateRepository.cs` | Message state repository |
| `Migrations/*.cs` | 14 migrations |

#### AbroadQs.Bot.Application (2 files)
| File | Purpose |
|------|---------|
| `UpdateDispatcher.cs` | Central routing engine |
| `ServiceCollectionExtensions.cs` | DI registration helpers |

#### AbroadQs.Bot.Telegram (2 files)
| File | Purpose |
|------|---------|
| `TelegramResponseSender.cs` | Telegram API implementation |
| `ServiceCollectionExtensions.cs` | DI registration |

#### AbroadQs.Bot.Modules.Common (18 files)
| File | Purpose |
|------|---------|
| `CommonModule.cs` | Module registration |
| `StartHandler.cs` | /start command, welcome, language |
| `DynamicStageHandler.cs` | Stage navigation, settings, routing |
| `KycStateHandler.cs` | Identity verification flow |
| `ProfileStateHandler.cs` | Profile view/edit |
| `ExchangeStateHandler.cs` | Exchange request flow |
| `FinanceHandler.cs` | Wallet, payments, transfers |
| `CurrencyPurchaseHandler.cs` | Crypto purchase flow |
| `GroupStateHandler.cs` | Exchange group management |
| `BidStateHandler.cs` | Exchange ad bidding |
| `InternationalQuestionHandler.cs` | International Q&A |
| `StudentProjectHandler.cs` | Student projects |
| `SponsorshipHandler.cs` | Financial sponsorships |
| `TicketHandler.cs` | Support tickets |
| `MyMessagesHandler.cs` | System messages |
| `MyProposalsHandler.cs` | User proposals |
| `HelpHandler.cs` | /help command |
| `UnknownCommandHandler.cs` | Fallback handler |

#### AbroadQs.Bot.Host.Webhook (25+ files)
| File | Purpose |
|------|---------|
| `Program.cs` | Main entry point, DI, API endpoints |
| `Dockerfile` | Docker build instructions |
| `appsettings.json` | Default configuration |
| `appsettings.Development.json` | Dev configuration |
| `Services/ProcessingContext.cs` | Request tracking |
| `Services/RabbitMqPublisher.cs` | RabbitMQ publisher |
| `Services/RabbitMqConsumerService.cs` | RabbitMQ consumer |
| `Services/RabbitMqOptions.cs` | RabbitMQ config |
| `Services/IRabbitMqPublisher.cs` | Publisher interface |
| `Services/RedisUserConversationStateStore.cs` | Redis conversation state |
| `Services/RedisUserLastCommandStore.cs` | Redis last command |
| `Services/RedisOptions.cs` | Redis config |
| `Services/BotStatusService.cs` | Bot status |
| `Services/UpdateLogService.cs` | Update log |
| `Services/SmsIrService.cs` | SMS OTP (sms.ir) |
| `Services/EmailOtpService.cs` | Email OTP (HTTP relay) |
| `Services/NavasanApiService.cs` | Exchange rates API |
| `Services/BitPayService.cs` | BitPay gateway |
| `Services/BitPayPaymentGatewayAdapter.cs` | Payment adapter |
| `Services/CryptoWalletService.cs` | Crypto service (skeleton) |
| `Services/RateAutoRefreshService.cs` | Rate auto-refresh |
| `Services/GetUpdatesPollingService.cs` | Long polling |
| `Services/PlaceholderTelegramBotClient.cs` | No-op bot client |
| `Services/NoOp*.cs` | No-op service implementations |
| `wwwroot/dashboard/index.html` | Admin dashboard |
| `wwwroot/css/site.css` | Custom CSS |
| `wwwroot/kyc_sample_photo.png` | KYC sample photo |

#### Tunnel & Proxy
| File | Purpose |
|------|---------|
| `AbroadQs.TunnelServer/Program.cs` | WebSocket tunnel server |
| `AbroadQs.TunnelServer/Dockerfile` | Tunnel server Docker |
| `AbroadQs.TunnelClient/Program.cs` | WebSocket tunnel client |
| `AbroadQs.ReverseProxy/Program.cs` | YARP reverse proxy (legacy) |

#### Scripts
| File | Purpose |
|------|---------|
| `scripts/deploy-server.sh` | Server-side deploy |
| `scripts/setup-webhook-ssl.sh` | Nginx + SSL setup |
| `scripts/update-nginx-tunnel.sh` | Nginx tunnel config |

</details>

---

*This wiki documents the complete AbroadQs Telegram Bot project as of February 2026.*
