## Trivio – Real-time Multiplayer Word Game (ASP.NET Core, SignalR, Redis)

Trivio is a **real-time multiplayer word game** built on **ASP.NET Core 9**, **SignalR**, and a **Redis backplane**.  
It is designed as a **portfolio project** to showcase:

- **Real-time web development** with SignalR
- **Horizontal scaling** with a Redis backplane
- **Distributed state management** using Redis
- **Clean architecture** with services, filters, and Razor Pages
- **Feature work** like private rooms, roles, and room lifecycle management

---

## 🔌 How to Run the Project (Local Setup)

### 1. Prerequisites

- **.NET 9 SDK**
- **Docker Desktop** installed and running (for Redis)
- A browser (Chrome/Edge/Firefox)

### 2. Start Redis via Docker

From the repository root (where `docker-compose.yml` lives, or from the solution root if you added it there):

```bash
docker compose up -d
```

This will:
- Start a container named `trivio-redis`
- Expose Redis on `localhost:6379`
- Persist data to a Docker volume (`redis_data`)

#### Offline / air-gapped tip

- The compose file pins the image to `redis:7.4-alpine`, so Docker never checks for `latest`.
- Run the following **once while you have connectivity** and Docker will cache the layers for offline use:

```bash
docker pull redis:7.4-alpine
```

After that, `docker compose up -d` works even with Wi-Fi off because it uses the cached image only.

You can verify Redis is up with:

```bash
docker ps
docker exec -it trivio-redis redis-cli ping   # should return PONG
```

### 3. Run the ASP.NET Core App

From the `Trivio` project directory:

```bash
dotnet restore
dotnet run
```

Then browse to the HTTPS URL shown in the console (for example `https://localhost:7185`).

To simulate multiple servers / browser clients:
- Open the game in **two different browsers** or profiles (e.g. Chrome + Edge)
- Or open multiple windows pointing to the same URL

---

## 🧠 What the App Does (High-level)

- **Players create or join rooms** using a numeric room code.
- Each room has:
  - An **owner/admin**
  - **Players** (who submit words and earn points)
  - **Spectators** (who only watch)
- The game runs for **multiple rounds**:
  - Each round has **random Turkish consonants**
  - Players submit words built from these consonants
  - Words are checked against a **Turkish dictionary** (`tr_words.csv`)
  - Scores are updated in real time via **SignalR**
- **Private rooms** can be protected by a **password**:
  - Only users with the correct password can join
  - Password handling is server-side and flows through the full pipeline

The important part for your portfolio: this is not just a simple chat hub. It uses **Redis** plus **SignalR backplane** to keep **room state, players, and game progress consistent** across server instances.

---

## 🏗️ Architecture Overview

At a high level, the architecture looks like this:

- **ASP.NET Core / Razor Pages**
  - UI pages: `Index` (create/join room) and `GamePage` (actual game)
- **SignalR Hub**
  - `GameHub` in `Hubs/GameHub.cs`
  - Handles real-time messaging and game actions
- **Domain / Models**
  - `Room`, `Player`, enums like `Roles` and `RoomState`
- **Services**
  - `IRoomRegistry` + `RoomRegistry`: room lifecycle & state management (backed by Redis)
  - `IWordService` + `WordService`: consonant generation + dictionary lookup
- **Filters**
  - `RoomValidationFilter` (Hub filter): centralizes validation logic
- **Infrastructure**
  - **Redis** (via Docker)
  - **StackExchange.Redis** for low-level Redis access
  - **SignalR.Redis backplane** for cross-server broadcasting

### Redis’s Role in This Architecture

Redis is used in **two critical ways**:

1. **SignalR Backplane**  
   - `AddStackExchangeRedis` is configured for SignalR.
   - All hubs instances publish/subscribe through Redis.
   - This allows **multiple app instances** to share SignalR messages.

2. **Distributed Room State (Custom)**  
   - `RoomRegistry` maintains each room’s full state in Redis:
     - Players and spectators in the room
     - Owner connection and role
     - Game status (`GameStarted`, `GameCompleted`, `RoundNumber`, `CurrentWord`, etc.)
     - Private room settings: `IsPrivate`, `Password`
   - Any server can:
     - **Read** the room from Redis
     - **Update** it when game state changes
   - This prevents “split brain” where each server has different state.

---

## ⚙️ Key Configuration (SignalR + Redis)

All the important wiring happens in `Program.cs`.

### 1. Reading Redis configuration

`appsettings.json` / `appsettings.Development.json` include:

```json
"ConnectionStrings": {
  "Redis": "localhost:6379"
},
"Redis": {
  "InstanceName": "Trivio",
  "DefaultSlidingExpiration": "02:00:00"
}
```

In `Program.cs`, the connection string is read like this:

```csharp
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
```

### 2. SignalR with Redis backplane

The hub is configured to use Redis as a backplane:

```csharp
builder.Services
    .AddSignalR(options =>
    {
        options.AddFilter<RoomValidationFilter>();
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
        options.HandshakeTimeout = TimeSpan.FromSeconds(15);
    })
    .AddStackExchangeRedis(redisConnectionString, options =>
    {
        options.Configuration.ChannelPrefix = RedisChannel.Literal("Trivio");
        options.Configuration.AbortOnConnectFail = false;
        options.Configuration.ConnectRetry = 3;
        options.Configuration.ConnectTimeout = 5000;
    });
```

This shows:
- **Hub filters** (for validation)
- **Detailed errors & timeouts**
- **Redis backplane** with:
  - Channel prefix for isolation
  - Robust connection settings

### 3. Redis connection + distributed cache

Redis connections are registered using `StackExchange.Redis`:

```csharp
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(connectionString);
});

builder.Services.AddSingleton<IDatabase>(sp =>
{
    var redis = sp.GetRequiredService<IConnectionMultiplexer>();
    return redis.GetDatabase();
});

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = builder.Configuration["Redis:InstanceName"];
});
```

These are then used in `RoomRegistry` to store room objects in Redis.

### 4. Mapping the hub

Finally, in the request pipeline:

```csharp
app.MapHub<GameHub>("/gameHub");
app.MapRazorPages().WithStaticAssets();
```

---

## 🧩 Room & Game State Design (Redis-backed)

### Room Model

`Models/Room.cs` represents a complete snapshot of a game room:

- **Core fields**
  - `Code` (room code)
  - `CreatedAtUtc`, `ExpiresAtUtc`
  - `Capacity`
  - `OwnerConnectionId`, `OwnerRole`
  - `IsPrivate`, `Password`
- **Connections**
  - `ConcurrentDictionary<string, (string Username, Roles Role)> Connections`
  - Exposed via a **serializable proxy** `ConnectionsData` to make it JSON-serializable for Redis.
- **Game state**
  - `WordList`
  - `CurrentWordIndex`, `CurrentWord`
  - `GameStarted`, `GameCompleted`
  - `CurrentConsonants`
  - `RoundNumber`
  - `RoundStartedAt`

Because `ConcurrentDictionary` and tuples are difficult to serialize directly, the project uses:

- `[JsonIgnore]` on the `Connections` dictionary
- A `[JsonPropertyName("connections")]` proxy property that converts between:
  - `ConcurrentDictionary<string, (string Username, Roles Role)>`
  - `Dictionary<string, ConnectionInfo>`

This is a good demonstration of **practical serialization techniques** for distributed caching.

### RoomRegistry – Distributed Room State

`Services/RoomRegistry.cs` is responsible for:

- Creating rooms
- Tracking connections
- Applying room rules (capacity, private/public)
- Persisting & loading room state from Redis

Key points:

- **Constructor**: Injects `IDatabase` from `StackExchange.Redis` and logging.
- **CreateRoom**:
  - Creates an in-memory `Room` instance
  - Sets `IsPrivate` and `Password`
  - Saves the room into Redis under a key like `room:{code}`
- **GetRoom**:
  - First tries local in-memory cache
  - If not found, loads from Redis and caches it
- **TryAddConnection**:
  - Refreshes room from Redis first
  - Validates:
    - Room existence
    - Capacity
    - Private room password (trimmed on both sides)
  - Removes any old connections under the same username (deduplication)
  - Adds the new connection
  - Saves the updated room back to Redis
- **RemoveConnection / CloseRoom / UpdateRoomState**:
  - Mutate the in-memory room
  - Persist the new state to Redis
- **RefreshRoomFromRedis**:
  - Clears local cache and re-loads from Redis (used by the hub to ensure fresh state)

Serialization and deserialization are implemented with `System.Text.Json` and explicit options, with logging in case of problems. This showcases **error handling and resilience** in distributed systems.

---

## 🔐 Private Rooms & Password Flow

Private rooms are a good example of **end-to-end feature work** across layers.

### 1. Creating / Attending Rooms (Razor Page `Index`)

In `Pages/Index.cshtml` and `Index.cshtml.cs`:

- The **Create Room** modal includes:
  - “Private Room” checkbox
  - Password field (shown/hidden dynamically with JavaScript)
- The **Attend Room** modal includes:
  - Room code
  - Optional password field (required for private rooms)

Server-side:

- `OnPostStartGame`:
  - Accepts `bool privateRoom` and `string? password`
  - Trims the password, stores it in `TempData` if present
  - Calls `_roomRegistry.CreateRoom` with `isPrivate` and `password`
  - Redirects to `/GamePage?code=...`

- `OnPostAttend`:
  - Accepts `roomCode`, `username`, and `string? password`
  - Validates room existence
  - If room is private:
    - Validates the password (with trim)
    - Sets `TempData["Password"] = trimmedPassword`
  - Redirects to `/GamePage?code=...` if all checks pass

This demonstrates **proper server-side validation** instead of trusting only client-side checks.

### 2. Passing Password to SignalR

On `Pages/GamePage.cshtml.cs`:

- `Password` is read from `TempData` in `OnGet`
- Exposed to the Razor view (`GamePage.cshtml`)

In `Pages/GamePage.cshtml`:

- A JavaScript variable `roomPassword` is initialized from `Model.Password`
- When connecting/reconnecting to SignalR:
  - The client calls:
    - `connection.invoke('JoinRoom', code, role, username, roomPassword)`

### 3. Password Validation in RoomRegistry

In `RoomRegistry.TryAddConnection`:

- Refreshes room from Redis
- If `room.IsPrivate`:
  - Ensures a password was provided by the client
  - Trims both the stored password and incoming password
  - Compares them
  - Returns an error reason if they don’t match

This flow shows:
- Secure, centralized validation
- Consistent **password trimming and comparison** at all entry points
- Correct propagation of room security through HTTP → TempData → Razor → SignalR → Redis-backed service

### 4. Where Private Room Passwords Actually Live (TempData vs Redis)

- **Room password (source of truth)**:
  - When the owner creates a room, `RoomRegistry.CreateRoom` stores `IsPrivate` and a **trimmed `Password`** in the `Room` object.
  - That `Room` is saved to **Redis** under a key like `room:{code}` and also kept in the in-memory cache.
  - This Redis-backed password is what attendees and the hub always validate against; it survives browser refreshes, reconnects, and even app restarts while Redis is running.

- **Per-browser password copy (TempData + JS)**:
  - For each user (creator or attendee), `Index.cshtml.cs` temporarily puts their typed password into `TempData["Password"]` to carry it across a **POST → redirect → GET**.
  - `GamePage.cshtml.cs` reads `TempData["Password"]` once into `Model.Password`, and `GamePage.cshtml` exposes it to JavaScript as `roomPassword`, which is then passed into `JoinRoom`.
  - This TempData-based copy is **per browser, short-lived, and one-shot**: it does **not** survive a full page refresh (F5); on refresh TempData is gone and `Model.Password` becomes empty.

- **Multi-browser behavior (Chrome vs Edge)**:
  - Each browser has its **own TempData cookie**, but they all validate against the **same room password stored in Redis**.
  - When an attendee on another browser submits the password, `OnPostAttend` and `RoomRegistry.TryAddConnection` compare the attendee’s trimmed password with `room.Password` from Redis.
  - This design makes Redis the **single source of truth** for private-room passwords, while TempData is only a per-user transport mechanism to get the password into SignalR once.

---

## 📡 GameHub – Real-time Game Coordination

`Hubs/GameHub.cs` is the core real-time layer.

### Key Responsibilities

- Joining and leaving rooms
- Starting games and rounds
- Submitting guesses and updating scores
- Broadcasting user lists and room events
- Keeping all instances in sync using `IRoomRegistry` (Redis-backed)

### Important Patterns

- **JoinRoom**:
  - Accepts `code`, `role`, `username`, and `password?`
  - Calls `_roomRegistry.TryAddConnection(...)`
  - Waits briefly (`Task.Delay`) to allow Redis to propagate changes
  - Calls `_roomRegistry.RefreshRoomFromRedis` to ensure it sees latest state
  - Builds a **deduplicated user list** by username (so reconnects don’t duplicate users)
  - Sends:
    - `ReceiveUserList` to the caller
    - A broadcast of updated user list to the room

- **StartTheGame / StartNewRound / SubmitGuess**:
  - Modify the room’s game state
  - Immediately call `_roomRegistry.UpdateRoomState(room)` so Redis is always authoritative
  - This ensures:
    - If a later request hits another server, it reads the same game state
    - Late joiners see the correct round and scores

- **User list synchronization**:
  - `GameUsers` in-memory dictionary is no longer trusted as the source of truth across servers
  - The hub now builds user lists from `Room.Connections` (which is serialized into Redis)

Together, this demonstrates:
- **Using Redis for both messaging (backplane) and state**
- Handling **reconnection scenarios**
- Preventing **duplicate users**
- Keeping **multiple instances** in sync.

---

## 🧪 What This Project Demonstrates (Skills)

- **SignalR**
  - Hub design and hub methods
  - Group management (room groups, role-based groups)
  - Client → server → client messaging
  - Automatic reconnection handling
  - Hub filters for validation (`RoomValidationFilter`)

- **Redis & Distributed Systems**
  - Redis backplane configuration with `Microsoft.AspNetCore.SignalR.StackExchangeRedis`
  - Distributed cache and connection multiplexer (`StackExchange.Redis`)
  - Designing a **serializable domain model** for Redis
  - Implementing a **distributed room registry** (`RoomRegistry`) that:
    - Refreshes from Redis
    - Persists state after each mutation
    - Handles errors gracefully

- **ASP.NET Core & Razor Pages**
  - Page models (`Index`, `GamePage`)
  - `TempData` for short-lived cross-request data (e.g., passwords, role, username)
  - Model validation and error messages

- **Security & Robustness**
  - Private rooms with passwords
  - Consistent password trimming & validation
  - Preventing duplicate users and handling reconnections
  - Nullable reference warnings cleaned up with safe defaults

- **Clean Code & Architecture**
  - Separation of concerns:
    - Hubs vs services vs models vs filters
  - Strong typing with enums (`Roles`, `RoomState`)
  - Centralized state in `RoomRegistry`
  - Logging and failure handling for Redis operations

---

## 📁 Project Structure (Short)

```text
Trivio/
├── Hubs/
│   └── GameHub.cs              # SignalR hub
├── Models/
│   ├── Room.cs                 # Room + game state (Redis-serializable)
│   └── Player.cs               # Player model
├── Services/
│   ├── IRoomRegistry.cs        # Room management interface
│   ├── RoomRegistry.cs         # Redis-backed room registry
│   ├── IWordService.cs         # Word validation interface
│   └── WordService.cs          # Turkish word validation & consonants
├── Filters/
│   └── RoomValidationFilter.cs # Hub filter for validation
├── Pages/
│   ├── Index.cshtml(.cs)       # Landing page: create/join room
│   └── GamePage.cshtml(.cs)    # Game UI + SignalR client logic
└── wwwroot/
    ├── data/tr_words.csv       # Turkish dictionary
    ├── js/site.js, utils.js    # Client JS, SignalR wiring
    └── css/*.css               # Layout + game UI styling
```

---

## 📝 Notes / Planned Enhancements

- **Pause / Resume by owner** (and requested by users)
- **More analytics** (e.g., Redis-based metrics or live dashboards)
- **Additional real-time features** like in-game chat or presence indicators
 
## 📢 Do We Need IHubContext?

Not for the current design. All real-time messaging stays inside `GameHub` using `Clients`/`Groups`, and state is coordinated through `RoomRegistry` (Redis). `IHubContext<T>` is only needed when **pushing messages from outside the hub** (e.g., background services, controllers, schedulers). If you ever add a server-driven notification (like auto-closing rooms or admin dashboards), you can inject `IHubContext<GameHub>` into that service. For this portfolio scope, omitting it keeps the code lean and focused.

These would extend the existing SignalR + Redis foundation and are natural next steps for the project.


