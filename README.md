# Trivio - Real-time Word Game with SignalR

## 🎯 Project Overview

Trivio is a real-time multiplayer word game built with ASP.NET Core and SignalR, designed to demonstrate advanced SignalR implementation skills. The game challenges players to create words using only specific consonants provided each round, with real-time communication and competitive scoring.

## 🎮 Game Mechanics

### Core Gameplay
- **Multi-round Format**: 10 rounds of word creation challenges
- **Consonant Constraints**: Each round provides 5 random Turkish consonants
- **Word Validation**: Real-time dictionary validation using Turkish word database
- **Scoring System**: Points based on word length (1 point per letter)
- **Real-time Updates**: Instant feedback and score updates via SignalR

### User Roles
- **Player**: Active participants who can submit words and earn points
- **Spectator**: Observers who can watch the game but cannot participate

### Room Management
- **Admin**: Room owners with administrative controls (kick users, close room, start game). When connection break, admin will be the next one. 

## 🏗️ Architecture & SignalR Features

### SignalR Implementation Highlights

#### 1. **Hub Architecture**
```csharp
public class GameHub : Hub
{
    // Real-time game state management
    // Connection lifecycle handling
    // Group-based communication
}
```

#### 2. **Advanced Connection Management**
- **Automatic Reconnection**: Client-side reconnection with exponential backoff
- **Connection State Tracking**: Real-time connection status indicators
- **Graceful Disconnection**: Proper cleanup and ownership transfer
- **Group Management**: Dynamic room and role-based groups

#### 3. **Real-time Features**
- **Live User Lists**: Dynamic user management with role updates
- **Instant Game Updates**: Round progression and score changes
- **Admin Controls**: Real-time room management (kick, close, start)
- **Ownership Transfer**: Automatic admin role transfer when owner disconnects

### ✅ What the app actually does via SignalR

This section reflects the current, working SignalR behavior implemented in `Hubs/GameHub.cs` and used by `Pages/GamePage.cshtml`.

- **Hub methods (server-called by client)**
  - `CreateRoom(code, role, username)`: Creates a room in the registry and returns the provided `code`.
  - `JoinRoom(code, role, username)`: Validates and joins the room. Adds the connection to groups: `code`, and role-scoped group (`code-players` or `code-spectators`). Sends:
    - `ReceiveUserList(existingUsers)` only to the caller for pre-existing room members
    - `UserJoined(username, role)` to the whole room
    - `JoinSuccess(code, role)` to the caller
    - If a round is in progress, `GetRoomState(roundData)` to the caller so late joiners can catch up
  - `StartTheGame(code, wordCount)`: Owner-only. Initializes game state then starts round 1 by invoking `RoundStarted(...)` to the room.
  - `SubmitGuess({ guess, code, username })`: Validates word against allowed consonants and the dictionary. On success:
    - Updates the user's points
    - Sends `GuessResult({ success: true, message, correctWord, guesser, pointsEarned })` to the room
    - Sends `UserListUpdated(users)` with fresh scores
    - Advances to the next round with `RoundStarted(...)`, or sends `GameCompleted(...)` after round 10
    On failure, sends `GuessResult({ success: false, message })` to the caller, and a feedback `GuessResult` to others when word not found.
  - `CloseRoom(code)`: Owner-only. Marks room closed and broadcasts `RoomClosed({ message, closedBy: 'owner' })`.
  - `KickUser(code, targetUsername)`: Owner-only. Removes the user and broadcasts `UserKicked(targetUsername)`; kicked client receives `Kicked`.
  - `ShareTypingInput({ username, input, code })`: Broadcasts `ReceiveTypingInput(username, input)` to others in the room. Used by spectators to live-view player typing in the 4-card grid.
  - `LeaveRoom(code)`: Removes the connection from the room group.

- **Server-to-client events (received by client)**
  - Connection/Presence: `UserJoined`, `UserLeft`, `UserKicked`, `Kicked`, `OwnerChanged`, `YouAreNowOwner`
  - Room lifecycle: `JoinSuccess`, `RoomClosed`, `ReceiveUserList`, `UserListUpdated`
  - Gameplay: `RoundStarted`, `GetRoomState`, `GuessResult`, `GameCompleted`
  - Typing share: `ReceiveTypingInput`

- **Ownership transfer logic**
  - If the owner disconnects, the server promotes the first remaining player to owner and updates their role to `admin` in the in-memory `GameUsers` list.
  - Broadcasts `OwnerChanged({ newOwner, newOwnerConnectionId, message })` to the room and `YouAreNowOwner({ message, hasAdminControls: true })` to the new owner, prompting the client UI to reveal admin controls.

- **Reconnection behavior**
  - Clients auto-reconnect with delays. On reconnection, the client re-invokes `JoinRoom(code, role, username)` to re-enter groups and refresh state. If a round is active, the server sends `GetRoomState(roundData)` so the UI can restore the timer, consonants, and round number.

- **Round lifecycle**
  - `StartTheGame` → server sets `GameStarted = true`, `RoundNumber = 1`, then `RoundStarted({ consonants, roundNumber, message, roundStartedAt })` to the room.
  - Each valid guess increments the scorer's `points = word.Length`, updates users via `UserListUpdated`, and either
    - increments `RoundNumber` and sends the next `RoundStarted`, or
    - after round 10, sends `GameCompleted({ message, totalRounds, gameCompleted: true })`.

- **Groups used**
  - Room group: ``{code}``
  - Role groups: ``{code}-players``, ``{code}-spectators`` (prepared for role-targeted features)

#### 4. **Advanced SignalR Patterns**

**Hub Filters for Validation**:
```csharp
public class RoomValidationFilter : IHubFilter
{
    // Pre-method validation
    // Room capacity checking
    // Username uniqueness validation
}
```

**Group-based Communication**:
- Room-specific groups: `{code}`
- Role-based groups: `{code}-players`, `{code}-spectators`
- Targeted messaging for admins and specific users

**State Management**:
- In-memory room registry with automatic cleanup
- Concurrent dictionary for thread-safe operations
- Timer-based expired room cleanup

## 🛠️ Technical Stack

### Backend
- **.NET 9.0** - Latest framework features
- **ASP.NET Core** - Web application framework
- **SignalR** - Real-time commsunication
- **Razor Pages** - Server-side rendering
- **Dependency Injection** - Service container management

### Frontend
- **JavaScript** - Client-side SignalR implementation
- **Bootstrap 5** - Responsive UI framework
- **jQuery** - DOM manipulation and utilities

### Services
- **RoomRegistry** - Room lifecycle and connection management
- **WordService** - Turkish word validation and consonant generation
- **GameHub** - SignalR hub for real-time communication

## 🚀 Key Features

### Real-time Communication
- ✅ Live user joining/leaving notifications
- ✅ Instant word validation and scoring
- ✅ Round progression updates
- ✅ Admin control notifications
- ✅ Connection status indicators

### Game Management
- ✅ Dynamic room creation with custom codes
- ✅ Role-based access control
- ✅ Automatic room cleanup (2-hour expiration)
- ✅ Owner disconnection handling
- ✅ User kicking functionality

### Data Management
- ✅ Turkish word dictionary (38,000+ words)
- ✅ Cryptographic random number generation
- ✅ Thread-safe concurrent operations
- ✅ Automatic file-based word loading

## 📁 Project Structure

```
Trivio/
├── Hubs/
│   └── GameHub.cs              # SignalR hub implementation
├── Models/
│   ├── Room.cs                 # Room state management
│   └── Player.cs               # Player data model
├── Services/
│   ├── IRoomRegistry.cs        # Room management interface
│   ├── RoomRegistry.cs         # Room lifecycle service
│   ├── IWordService.cs         # Word validation interface
│   └── WordService.cs          # Turkish word processing
├── Filters/
│   └── RoomValidationFilter.cs # SignalR hub filter
├── Enums/
│   ├── Roles.cs                # User role definitions
│   └── RoomState.cs            # Room state definitions
├── Pages/
│   ├── GamePage.cshtml         # Main game interface
│   └── GamePage.cshtml.cs      # Page model
└── wwwroot/
    ├── data/
    │   └── tr_words.csv        # Turkish word dictionary
    ├── js/
    │   ├── site.js             # Client-side utilities
    │   └── utils.js            # Helper functions
    └── css/
        ├── site.css            # Global styles
        └── gamepage.css        # Game-specific styles
```

## 🎯 SignalR Demonstrations

This project showcases several advanced SignalR concepts:

1. **Hub Method Implementation** - Custom methods for game actions
2. **Group Management** - Dynamic group joining/leaving
3. **Connection Lifecycle** - Proper connection/disconnection handling
4. **Hub Filters** - Pre-method validation and authorization
5. **Client Reconnection** - Automatic reconnection with state restoration
6. **Real-time State Synchronization** - Game state consistency across clients
7. **Error Handling** - Comprehensive error management and user feedback

## 🚀 Getting Started

### Prerequisites
- .NET 9.0 SDK
- Visual Studio 2022 or VS Code

### Running the Application
1. Clone the repository
2. Navigate to the Trivio directory
3. Run `dotnet restore` to restore packages
4. Run `dotnet run` to start the application
5. Navigate to `https://localhost:5001` (or the port shown in console)

### Game Flow
1. **Create/Join Room**: Enter a room code and choose your role
2. **Wait for Players**: Room owner can start the game when ready
3. **Play Rounds**: Create words using only the provided consonants
4. **Score Points**: Earn points based on word length
5. **Complete Game**: Finish all 10 rounds and see final scores

## 🎓 Learning Objectives Demonstrated

This project demonstrates proficiency in:
- **Real-time Web Development** with SignalR
- **Multi-user Game Architecture** 
- **Connection State Management**
- **Group-based Communication Patterns**
- **Error Handling and Resilience**
- **Client-side State Synchronization**
- **Server-side Validation and Security**

## 📝 Notes

- The first user to create a room becomes the admin
- Rooms automatically expire after 2 hours of inactivity
- Turkish word dictionary contains 38,000+ words
- Game supports up to 8 players per room
- Spectators can watch but cannot participate in scoring

## 🚀 Additional SignalR Features You Could Add

### 1. **Real-time Chat System**
```csharp
public async Task SendMessage(string roomCode, string message)
{
    await Clients.Group(roomCode).SendAsync("ReceiveMessage", 
        new { Username = Context.User.Identity.Name, Message = message, Timestamp = DateTime.UtcNow });
}
```

### 2. **Typing Indicators**
```csharp
public async Task StartTyping(string roomCode)
{
    await Clients.OthersInGroup(roomCode).SendAsync("UserTyping", Context.ConnectionId);
}

public async Task StopTyping(string roomCode)
{
    await Clients.OthersInGroup(roomCode).SendAsync("UserStoppedTyping", Context.ConnectionId);
}
```

### 3. **Game Timer with Server-side Validation**
```csharp
public async Task StartRoundTimer(int roomCode, int seconds)
{
    var timer = new Timer(async _ => {
        await Clients.Group(roomCode.ToString()).SendAsync("RoundTimeUp");
        await EndRound(roomCode);
    }, null, seconds * 1000, Timeout.Infinite);
}
```

### 4. **Presence System**
```csharp
public override async Task OnConnectedAsync()
{
    var user = GetUserFromConnection();
    await Clients.Others.SendAsync("UserOnline", user.Username);
    await base.OnConnectedAsync();
}
```

### 5. **Real-time Notifications System**
```csharp
public async Task SendNotification(string userId, string message, NotificationType type)
{
    await Clients.User(userId).SendAsync("ReceiveNotification", 
        new { Message = message, Type = type, Timestamp = DateTime.UtcNow });
}
```

### 6. **Streaming Data (for live scoreboards)**
```csharp
public async IAsyncEnumerable<ScoreUpdate> StreamScores(int roomCode, 
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        var scores = GetRoomScores(roomCode);
        yield return scores;
        await Task.Delay(1000, cancellationToken);
    }
}
```

### 7. **Custom Authentication & Authorization**
```csharp
public class AuthorizeHubMethod : Attribute, IHubMethodNameProvider
{
    public IEnumerable<string> GetHubMethodNames(HubMethodDescriptor descriptor)
    {
        // Custom authorization logic
    }
}
```

### 8. **Rate Limiting for Hub Methods**
```csharp
public class RateLimitFilter : IHubFilter
{
    private readonly IMemoryCache _cache;
    
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, 
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        // Implement rate limiting logic
        var key = $"{context.Context.ConnectionId}:{context.HubMethodName}";
        // Check and enforce rate limits
    }
}
```

### 9. **Client-side State Persistence**
```javascript
// Save game state to localStorage
connection.on("GameStateUpdate", (state) => {
    localStorage.setItem('gameState', JSON.stringify(state));
    updateGameUI(state);
});

// Restore state on reconnection
connection.onreconnected(() => {
    const savedState = localStorage.getItem('gameState');
    if (savedState) {
        connection.invoke("RestoreGameState", JSON.parse(savedState));
    }
});
```

### 10. **Advanced Error Handling & Retry Logic**
```csharp
public async Task<T> InvokeWithRetry<T>(Func<Task<T>> operation, int maxRetries = 3)
{
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex) when (i < maxRetries - 1)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)));
            _logger.LogWarning("Retry {Attempt} failed: {Error}", i + 1, ex.Message);
        }
    }
    throw new HubException("Operation failed after all retries");
}
```

### 11. **Performance Monitoring**
```csharp
public class PerformanceHubFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, 
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await next(context);
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogInformation("Hub method {Method} took {ElapsedMs}ms", 
                context.HubMethodName, stopwatch.ElapsedMilliseconds);
        }
    }
}
```

### 12. **Real-time Analytics Dashboard**
```csharp
public async Task GetRoomAnalytics(int roomCode)
{
    var analytics = new {
        ActiveConnections = GetActiveConnections(roomCode),
        MessagesPerMinute = GetMessageRate(roomCode),
        AverageResponseTime = GetAverageResponseTime(roomCode),
        ErrorRate = GetErrorRate(roomCode)
    };
    
    await Clients.Caller.SendAsync("ReceiveAnalytics", analytics);
}
```

These additional features would significantly enhance your SignalR demonstration and show employers that you understand:

- **Advanced SignalR Patterns** (streaming, custom filters, authentication)
- **Performance Considerations** (rate limiting, monitoring, optimization)
- **User Experience** (presence, notifications, state persistence)
- **Scalability** (error handling, retry logic, analytics)
- **Real-world Application** (chat, timers, live updates)

Your current implementation already shows strong SignalR fundamentals with room management, real-time updates, and connection handling. Adding some of these features would make it an even more impressive portfolio piece!

## Planned Features

# Pause/resume by owner
A game can be paused by the admin. Also it can be demanded by any user. 

# Private Room
Normal room has no password. But private room has a password. Only users have code and password can join these rooms. 

