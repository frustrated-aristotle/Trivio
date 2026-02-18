using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Trivio.Enums;
using Trivio.Models;
using Trivio.Services;
using System.Security.Claims;
using System.Reflection.Metadata.Ecma335;

namespace Trivio.Hubs
{


    public class User
    {
        public string UserId { get; set; } = string.Empty;
        public string ConnectionId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty; // Önemli: Gerçek ad
        public string Role { get; set; } = string.Empty;     // player veya spectator
        public string Status { get; set; } = "online";
        public int Points { get; set; } = 0; // User's total points
    }

    public class GuessData
    {
        public string Guess { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
    }
    public class RoundData
    {
        public int RoundNumber { get; set; }
        public List<char> Consonants { get; set; } = new();
        public string Message { get; set; } = string.Empty;
        public DateTime RoundStartedAt { get; set; }
    }
    public class ShareTypingInputData
    {
        public string Username { get; set; } = string.Empty;
        public string Input { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }
    // Don't require auth at hub level - allow negotiation, check auth in methods
    public class GameHub : Hub, IGameHub
    {
        private readonly IRoomRegistry _roomRegistry;
        private readonly IWordService _wordService;
        private readonly ILogger<GameHub> _logger;
        
        public GameHub(IRoomRegistry roomRegistry, IWordService wordService, ILogger<GameHub> logger)
        {
            _roomRegistry = roomRegistry;
            _wordService = wordService;
            _logger = logger;
        }

        public static readonly Dictionary<string, List<User>> GameUsers = new();

        public async Task AddUserToLobby()
        {
            _logger.LogInformation("Connectionid is : {ConnectionId}", Context.ConnectionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, "lobby");
            
            // Send current list of open rooms to the newly joined user
            try
            {
                var openRooms = _roomRegistry.GetAllOpenRooms();
                var roomsData = openRooms.Select(room => BuildRoomDataForLobby(room)).ToList();
                
                await Clients.Caller.SendAsync("InitialRoomsList", roomsData);
                _logger.LogInformation("Sent {RoomCount} open rooms to new lobby member {ConnectionId}", roomsData.Count, Context.ConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending initial rooms list to lobby member {ConnectionId}", Context.ConnectionId);
            }
        }
        
        // Helper method to build room data for lobby broadcasts
        private object BuildRoomDataForLobby(Room room)
        {
            // Get host name from connections
            string hostName = "Unknown";
            if (room.Connections.Any())
            {
                // Try to find owner first
                if (!string.IsNullOrEmpty(room.OwnerUserId))
                {
                    var ownerConnection = room.Connections.FirstOrDefault(c => c.Value.UserId == room.OwnerUserId);
                    if (ownerConnection.Value.Username != null)
                    {
                        hostName = ownerConnection.Value.Username;
                    }
                }
                
                // Fallback to first connection if owner not found
                if (hostName == "Unknown")
                {
                    hostName = room.Connections.First().Value.Username ?? "Unknown";
                }
            }
            
            return new
            {
                code = room.Code,
                hostName = hostName,
                playerCount = room.Connections.Count,
                capacity = room.Capacity,
                isPrivate = room.IsPrivate,
                gameStarted = room.GameStarted,
                createdAt = room.CreatedAtUtc.ToString("O") // ISO 8601 format
            };
        }
        
        // Helper method to broadcast room update to lobby
        private async Task BroadcastRoomUpdateToLobby(int roomCode)
        {
            try
            {
                var room = _roomRegistry.GetRoom(roomCode);
                if (room != null && !room.IsClosed)
                {
                    var roomData = BuildRoomDataForLobby(room);
                    await Clients.Group("lobby").SendAsync("RoomUpdated", roomData);
                    _logger.LogInformation("Broadcasted room update to lobby for room {RoomCode}", roomCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting room update to lobby for room {RoomCode}", roomCode);
            }
        }
        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
            
            // Remove user from all rooms and notify others
            var roomsToNotify = new List<int>();
            
            foreach (var roomEntry in GameUsers.ToList())
            {
                var users = roomEntry.Value.ToList();
                var userToRemove = users.FirstOrDefault(u => u.ConnectionId == Context.ConnectionId);
                
                if (userToRemove != null)
                {
                    users.Remove(userToRemove);
                    GameUsers[roomEntry.Key] = users;
                    
                    // Remove from room registry
                    if (int.TryParse(roomEntry.Key, out var roomCode))
                    {
                        var room = _roomRegistry.GetRoom(roomCode);
                        if (room != null)
                        {
                            // Check if the disconnected user was the room owner (by userId)
                            bool wasOwner = !string.IsNullOrEmpty(room.OwnerUserId) && room.OwnerUserId == userToRemove.UserId;
                            
                            _roomRegistry.RemoveConnection(roomCode, Context.ConnectionId);
                            roomsToNotify.Add(roomCode);
                            
                            if (wasOwner)
                            {
                                // Transfer ownership to the next player or close room if empty
                                await HandleOwnerDisconnection(roomCode, room, users);
                            }
                            
                            _logger.LogInformation("Removed user {Username} from room {RoomCode} (was owner: {WasOwner})", 
                                userToRemove.Username, roomCode, wasOwner);
                        }
                    }
                }
            }
            
            // Notify remaining users in affected rooms
            foreach (var roomCode in roomsToNotify)
            {
                var room = _roomRegistry.GetRoom(roomCode);
                if (room != null && !room.IsClosed)
                {
                    await Clients.Group(roomCode.ToString()).SendAsync("UserLeft", Context.ConnectionId);
                    await SendUpdatedUserList(roomCode);
                    
                    // Broadcast room update to lobby
                    await BroadcastRoomUpdateToLobby(roomCode);
                }
                else if (room == null || room.IsClosed)
                {
                    // Room was closed or removed, notify lobby
                    await Clients.Group("lobby").SendAsync("RoomClosed", roomCode);
                }
            }
            
            await base.OnDisconnectedAsync(exception);
        }

        private async Task HandleOwnerDisconnection(int roomCode, Room room, List<User> remainingUsers)
        {
            var playerUsers = remainingUsers.Where(u => u.Role == "player").ToList();
            
            if (playerUsers.Any())
            {
                // Transfer ownership to the first remaining player
                var newOwner = playerUsers.First();
                room.OwnerConnectionId = newOwner.ConnectionId;
                room.OwnerUserId = newOwner.UserId;
                room.OwnerRole = Enums.Roles.Player;
                
                // Update the new owner's role to admin in GameUsers
                var codeKey = roomCode.ToString();
                if (GameUsers.ContainsKey(codeKey))
                {
                    var users = GameUsers[codeKey];
                    var userToUpdate = users.FirstOrDefault(u => u.Username == newOwner.Username);
                    if (userToUpdate != null)
                    {
                        userToUpdate.Role = "admin";
                        _logger.LogInformation("Updated user {Username} role to admin after ownership transfer", 
                            newOwner.Username);
                    }
                }
                
                _logger.LogInformation("Transferred room {RoomCode} ownership to {NewOwner}", 
                    roomCode, newOwner.Username);
                
                await Clients.Group(roomCode.ToString()).SendAsync("OwnerChanged", new {
                    newOwner = newOwner.Username,
                    newOwnerConnectionId = newOwner.ConnectionId,
                    message = $"{newOwner.Username} is now the room owner and has admin controls"
                });

                // Notify the new owner specifically
                await Clients.Client(newOwner.ConnectionId).SendAsync("YouAreNowOwner", new
                {
                    message = "You are now the room owner. You have full admin controls.",
                    hasAdminControls = true
                });
                
                // Send updated user list to reflect role change
                await SendUpdatedUserList(roomCode);
            }
            else
            {
                // No players left, close the room
                room.IsClosed = true;
                _logger.LogInformation("Closed room {RoomCode} - no players remaining", roomCode);
                
                await Clients.Group(roomCode.ToString()).SendAsync("RoomClosed", new {
                    message = "Room closed - no players remaining"
                });
                
                // Broadcast room closed to lobby
                await Clients.Group("lobby").SendAsync("RoomClosed", roomCode);
                
                // Clean up the room after a delay
                _ = Task.Delay(5000).ContinueWith(async _ => {
                    await CleanupEmptyRoom(roomCode);
                });
            }
        }

        private Task CleanupEmptyRoom(int roomCode)
        {
            var room = _roomRegistry.GetRoom(roomCode);
            if (room != null && room.Connections.IsEmpty)
            {
                // Remove from GameUsers if empty
                if (GameUsers.ContainsKey(roomCode.ToString()))
                {
                    GameUsers.Remove(roomCode.ToString());
                }
                
                _logger.LogInformation("Cleaned up empty room {RoomCode}", roomCode);
            }
            return Task.CompletedTask;
        }

        [HubMethodName("CreateRoom")]
        public Task<int> CreateRoom(int code, string role, string username)
        {
            // Create room in registry
            Enum.TryParse<Roles>(role, true, out var parsedRole);
            _roomRegistry.CreateRoom(code, Context.ConnectionId, username, parsedRole);
            return Task.FromResult(code);
        }

        [HubMethodName("JoinRoom")] //For the client, we may need to change it. 
        public async Task JoinRoom(int code, string role, string username, bool isAdmin, string? password = null)
        {
            try
            {
                // Enforce claims-based identity (ignore client-supplied identity fields)
                if (!Context.User?.Identity?.IsAuthenticated ?? true)
                {
                    throw new HubException("Unauthorized");
                }

                var roomClaim = Context.User.FindFirst("room")?.Value;
                if (string.IsNullOrWhiteSpace(roomClaim) || !int.TryParse(roomClaim, out var roomFromToken) || roomFromToken != code)
                {
                    throw new HubException("Invalid room token");
                }

                var userId = Context.User.FindFirst("userId")?.Value;
                if (string.IsNullOrWhiteSpace(userId))
                {
                    throw new HubException("Missing user identity");
                }

                var usernameFromToken = Context.User.FindFirst("username")?.Value ?? username;
                var roleClaim = Context.User.FindFirst("role")?.Value ?? role;
                var isAdminClaim = bool.TryParse(Context.User.FindFirst("isAdmin")?.Value, out var claimIsAdmin) && claimIsAdmin;

                _logger.LogInformation("User {Username} attempting to join room {RoomCode} as {Role} (claims enforced)", 
                    usernameFromToken, code, roleClaim);

                var newUser = new User
                {
                    UserId = userId,
                    ConnectionId = Context.ConnectionId,
                    Username = usernameFromToken,
                    Role = roleClaim
                };

                // Validate/add via registry first (password will be validated inside TryAddConnection if room is private)
                Enum.TryParse<Roles>(roleClaim, true, out var parsedRole);
                if (!_roomRegistry.TryAddConnection(code, Context.ConnectionId, userId, usernameFromToken, password, parsedRole, isAdminClaim, out var reason))
                {
                    _logger.LogInformation("Password {password}", password);
                    _logger.LogWarning("Failed to join room {RoomCode}: {Reason}", code, reason);
                    throw new HubException(reason ?? "Join failed");
                }

                // Small delay to ensure Redis has synced the connection update (removal of old connections + addition of new)
                await Task.Delay(200);

                // Refresh room from Redis to get latest connections from all servers
                var room = _roomRegistry.RefreshRoomFromRedis(code);
                if (room == null)
                {
                    throw new HubException("Room not found after joining");
                }

                _logger.LogInformation("Room {RoomCode} refreshed after join, has {ConnectionCount} connections", 
                    code, room.Connections.Count);

                // If owner not set, first joiner becomes owner
                if (string.IsNullOrEmpty(room.OwnerConnectionId))
                {
                    room.OwnerConnectionId = Context.ConnectionId;
                    room.OwnerUserId = userId;
                    _roomRegistry.UpdateRoomState(room);
                    _logger.LogInformation("User {Username} became owner of room {RoomCode}", usernameFromToken, code);
                }

                // 1. Build user list from Room.Connections (which is synced via Redis)
                var existingUsers = new List<User>();
                
                // Log for debugging
                _logger.LogInformation("Room {RoomCode} has {ConnectionCount} connections after join", code, room.Connections.Count);
                
                // Get ALL users from Room.Connections (includes users from all servers)
                foreach (var connection in room.Connections)
                {
                    _logger.LogDebug("Processing connection: {ConnectionId}, User: {Username}, Role: {Role}", 
                        connection.Key, connection.Value.Username, connection.Value.Role);
                    
                    existingUsers.Add(new User
                    {
                        UserId = connection.Value.UserId,
                        ConnectionId = connection.Key,
                        Username = connection.Value.Username,
                        Role = connection.Value.Role.ToString().ToLower(),
                        Status = "online",
                        Points = GetUserPoints(code.ToString(), connection.Value.UserId, connection.Value.Username)
                    });
                }
                
                _logger.LogInformation("Built user list with {UserCount} users for room {RoomCode}", existingUsers.Count, code);

                // Add new user to GameUsers for points tracking (per-server cache) if not already there
                var codeKey = code.ToString();
                if (!GameUsers.ContainsKey(codeKey))
                {
                    GameUsers[codeKey] = new List<User>();
                }
                
                // Check if user already exists in GameUsers (avoid duplicates)
                if (!GameUsers[codeKey].Any(u => u.ConnectionId == Context.ConnectionId))
                {
                    GameUsers[codeKey].Add(newUser);
                }

                //First, add all users to the room based on the game code.
                await Groups.AddToGroupAsync(Context.ConnectionId, code.ToString());
                
                //Then, add them to a role-based group within that room.
                if(role == "player")
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"{code}-players");
                }
                else if (role == "spectator")
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, $"{code}-spectators");
                }

                _logger.LogInformation("User {Username} successfully joined room {RoomCode} as {Role}", username, code, role);
                
                // Small delay to ensure Redis has synced the connection before broadcasting
                await Task.Delay(150);
                
                // Refresh room again to get the absolute latest state from all servers
                room = _roomRegistry.RefreshRoomFromRedis(code);
                if (room == null)
                {
                    _logger.LogWarning("Room {RoomCode} not found after refresh", code);
                    return;
                }
                
                _logger.LogInformation("Refreshed room {RoomCode} from Redis, has {ConnectionCount} connections", 
                    code, room.Connections.Count);
                
                // Rebuild the user list with the refreshed room data
                // Use a dictionary to deduplicate by UserId (same user might have multiple ConnectionIds from reconnections)
                var usersDict = new Dictionary<string, User>();
                foreach (var connection in room.Connections)
                {
                    var connectionUserId = connection.Value.UserId;
                    var connectionUsername = connection.Value.Username;
                    
                    // Deduplicate by UserId - if same user appears with different ConnectionIds, keep the latest
                    var dedupeKey = string.IsNullOrWhiteSpace(connectionUserId) ? connectionUsername : connectionUserId;
                    if (!usersDict.ContainsKey(dedupeKey))
                    {
                        _logger.LogDebug("Adding user to list: {Username} ({ConnectionId})", 
                            connectionUsername, connection.Key);
                        
                        usersDict[dedupeKey] = new User
                        {
                            UserId = connectionUserId,
                            ConnectionId = connection.Key,
                            Username = connectionUsername,
                            Role = connection.Value.Role.ToString().ToLower(),
                            Status = "online",
                            Points = GetUserPoints(codeKey, connectionUserId, connectionUsername)
                        };
                    }
                    else
                    {
                        _logger.LogWarning("Duplicate user detected (UserId: {UserId}, Username: {Username}) in room {RoomCode} (ConnectionId: {ConnectionId}), keeping first occurrence", 
                            connectionUserId, connectionUsername, code, connection.Key);
                    }
                }
                
                var allUsers = usersDict.Values.ToList();
                _logger.LogInformation("Sending {UserCount} unique users to caller for room {RoomCode}", allUsers.Count, code);
                
                // Send initial user list to the caller (they need it for initial render)
                await Clients.Caller.SendAsync("ReceiveUserList", allUsers);
                
                // Send updated user list to ALL users in the room (including cross-server users)
                await SendUpdatedUserList(code);
                
                // Broadcast room update to lobby
                await BroadcastRoomUpdateToLobby(code);
                
                await Clients.Caller.SendAsync("JoinSuccess", code, role);
                
                if(room!.GameStarted)
                {
                    _logger.LogInformation("User {Username} is joining a room with a game started", username);
                    var roomState = new RoundData();
                    roomState.Consonants = room.CurrentConsonants;
                    roomState.RoundNumber = room.RoundNumber;
                    roomState.Message = "Wait for current game to end.";
                    roomState.RoundStartedAt = room.RoundStartedAt; // Use the actual round start time from room
                    await Clients.Client(Context.ConnectionId).SendAsync("GetRoomState", roomState);//To send the state of the room.
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error joining room {RoomCode} for user {Username}", code, username);
                throw new HubException("An error occurred while joining the room");
            }
        }

        public async Task StartTheGame(int code, int wordCount)
        {
            var callerUserId = Context.User?.FindFirst("userId")?.Value;
            if (string.IsNullOrWhiteSpace(callerUserId))
            {
                throw new HubException("Unauthorized");
            }

            var room = _roomRegistry.GetRoom(code);
            if (room == null)
            {
                throw new HubException("Room not found");
            }
            if (!string.Equals(room.OwnerUserId, callerUserId, StringComparison.Ordinal))
            {
                throw new HubException("Only the room owner can start the game");
            }

            // Initialize game state for consonant game
            room.GameStarted = true;
            room.GameCompleted = false;
            room.RoundNumber = 1;
            room.RoundStartedAt = DateTime.UtcNow;

            // Save game state to Redis so other servers can see it
            _roomRegistry.UpdateRoomState(room);
            
            // Broadcast room status change to lobby
            await Clients.Group("lobby").SendAsync("RoomStatusChanged", code, "In Game");

            // Start with the first round of consonants
            await StartNewRound(code);
        }
        private async Task StartNewRound(int code)
        {
            var room = _roomRegistry.GetRoom(code);
            if (room == null || !room.GameStarted)
            {
                await Clients.Group(code.ToString()).SendAsync("GameCompleted", new { message = "Game not started or room not found." });
                return;
            }

            // Get 5 random consonants
            room.CurrentConsonants = _wordService.GetRandomConsonants(5);
            
            // Update the round start time in the room state
            room.RoundStartedAt = DateTime.UtcNow;
            
            // Save round state to Redis so other servers can see it
            _roomRegistry.UpdateRoomState(room);
            
            await Clients.Group(code.ToString()).SendAsync("RoundStarted", new { 
                consonants = room.CurrentConsonants.ToArray(),
                roundNumber = room.RoundNumber,
                message = $"Round {room.RoundNumber}: Use only these consonants: {string.Join(", ", room.CurrentConsonants.Select(c => char.ToUpper(c)))}",
                roundStartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }
        [Authorize(Roles = "player,admin")]
        public async Task SubmitGuess(GuessData guessData)
        {
            if (guessData == null || string.IsNullOrWhiteSpace(guessData.Guess) || 
                string.IsNullOrWhiteSpace(guessData.Code) || string.IsNullOrWhiteSpace(guessData.Username))
            {
                await Clients.Caller.SendAsync("GuessResult", new { success = false, message = "Invalid guess data" });
                return;
            }

            var roomCode = int.Parse(guessData.Code);
            // Refresh room from Redis to get latest game state (in case it was updated on another server)
            var room = _roomRegistry.RefreshRoomFromRedis(roomCode);
            if (room == null || !room.GameStarted || room.CurrentConsonants == null || room.CurrentConsonants.Count == 0)
            {
                await Clients.Caller.SendAsync("GuessResult", new { success = false, message = "Game not started or no current consonants" });
                return;
            }

            var word = guessData.Guess.Trim().ToLower();

            // Check if word uses only allowed consonants
            if (!_wordService.HasAllowedConsonants(word, room.CurrentConsonants))
            {
                await Clients.Caller.SendAsync("GuessResult", new { 
                    success = false, 
                    message = $"Word must only use these consonants: {string.Join(", ", room.CurrentConsonants.Select(c => char.ToUpper(c)))}" 
                });
                return;
            }

            // Check if word exists in dictionary
            if (!await _wordService.WordExistsInDictionary(word))
            {
                await Clients.Caller.SendAsync("GuessResult", new { 
                    success = false, 
                    message = "Word not found in dictionary!" 
                });
                await Clients.OthersInGroup(guessData.Code).SendAsync("GuessResult", new{
                    success = false,
                    message = $"{guessData.Username} tried '{word}', not found in dictionary."
                });
                return;
            }

            var point = guessData.Guess.Length;
            
            // Update user's points
            var codeKey = guessData.Code;
            if (GameUsers.ContainsKey(codeKey))
            {
                var user = GameUsers[codeKey].FirstOrDefault(u => u.Username == guessData.Username);
                if (user != null)
                {
                    user.Points += point;
                    _logger.LogInformation("User {Username} earned {Points} points for word '{Word}' (total: {TotalPoints})", 
                        guessData.Username, point, word, user.Points);
                }
            }
            
            // Word is valid and exists!
            await Clients.Group(guessData.Code).SendAsync("GuessResult", new { 
                success = true, 
                message = $"Correct! '{word}' was guessed by {guessData.Username} (+{point} points)",
                correctWord = word,
                guesser = guessData.Username,
                pointsEarned = point
            });
            
            // Send updated user list with points
            await SendUpdatedUserList(int.Parse(guessData.Code));
            
            room.WordList.Add(word);
            // Save updated room state (round number, game completion)
            _roomRegistry.UpdateRoomState(room);

            // Check if we've reached the maximum rounds
            if (room.RoundNumber >= 10)
            {
                // Game completed - show completion modal
                await Clients.Group(guessData.Code).SendAsync("GameCompleted", new { 
                    message = "Game completed! You've finished all 10 rounds!",
                    totalRounds = room.RoundNumber,
                    gameCompleted = true
                });
                room.GameCompleted = true;
                
                // Save final game state
                _roomRegistry.UpdateRoomState(room);
                return;
            }

            // Move directly to next round
            room.RoundNumber++;
            
            // Save room state to Redis before starting next round
            _roomRegistry.UpdateRoomState(room);
            
            await StartNewRound(roomCode);
        }

        private async Task SendUpdatedUserList(int roomCode)
        {
            var room = _roomRegistry.GetRoom(roomCode);
            if (room == null)
            {
                return;
            }

            var codeKey = roomCode.ToString();
            // Use a dictionary to deduplicate by UserId (same user might have multiple ConnectionIds)
            var usersDict = new Dictionary<string, User>();

            // Build user list from Room.Connections (synced via Redis) and GameUsers (for points)
            foreach (var connection in room.Connections)
            {
                var username = connection.Value.Username;
                var userId = connection.Value.UserId;
                var dedupeKey = string.IsNullOrWhiteSpace(userId) ? username : userId;
                
                // Deduplicate by UserId (fallback to username) - keep the first occurrence if same user appears multiple times
                if (!usersDict.ContainsKey(dedupeKey))
                {
                    var points = GetUserPoints(codeKey, userId, username);
                    
                    usersDict[dedupeKey] = new User
                    {
                        UserId = userId,
                        ConnectionId = connection.Key,
                        Username = username,
                        Role = connection.Value.Role.ToString().ToLower(),
                        Status = "online",
                        Points = points
                    };
                }
                else
                {
                    _logger.LogDebug("Skipping duplicate user (UserId: {UserId}, Username: {Username}) in room {RoomCode} (ConnectionId: {ConnectionId})", 
                        userId, username, roomCode, connection.Key);
                }
            }

            var users = usersDict.Values.ToList();
            _logger.LogDebug("Sending updated user list with {UserCount} unique users for room {RoomCode}", 
                users.Count, roomCode);
            
            await Clients.Group(codeKey).SendAsync("UserListUpdated", users);
        }

        private int GetUserPoints(string codeKey, string userId, string username)
        {
            // Get points from GameUsers if available (per-server cache)
            if (GameUsers.ContainsKey(codeKey))
            {
                var user = GameUsers[codeKey].FirstOrDefault(u => (!string.IsNullOrWhiteSpace(userId) && u.UserId == userId) || u.Username == username);
                if (user != null)
                {
                    return user.Points;
                }
            }
            return 0;
        }

        [Authorize(Roles = "admin")]
        [HubMethodName("CloseRoom")]
        public async Task CloseRoom(int code)
        {
            try
            {
                var callerUserId = Context.User?.FindFirst("userId")?.Value;
                _logger.LogInformation("Role: " + Context.User?.FindFirst("role")?.Value);
                if (string.IsNullOrWhiteSpace(callerUserId))
                {
                    await Clients.Caller.SendAsync("ServerMessage", "Unauthorized", "error");
                    return;
                }

                var room = _roomRegistry.GetRoom(code);
                if (room == null)
                {
                    await Clients.Caller.SendAsync("ServerMessage", "Room not found", "error");
                    return;
                }

                // Check if the caller is the room owner (by userId)
                if (!string.Equals(room.OwnerUserId, callerUserId, StringComparison.Ordinal))
                {
                    await Clients.Caller.SendAsync("ServerMessage", "Only room owner can close the room", "error");
                    return;
                }

                // Close the room
                room.IsClosed = true;
                _roomRegistry.CloseRoom(code);

                // Notify all users in the room
                await Clients.Group(code.ToString()).SendAsync("RoomClosed", new
                {
                    message = "Room has been closed by the owner",
                    closedBy = "owner"
                });
                
                // Broadcast room closed to lobby
                await Clients.Group("lobby").SendAsync("RoomClosed", code);

                _logger.LogInformation("Room {RoomCode} closed by owner {ConnectionId}", code, Context.ConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error closing room {RoomCode}", code);
                await Clients.Caller.SendAsync("ServerMessage", "Error closing room", "error");
            }
        }
        [HubMethodName("ShareTypingInput")]
        public async Task ShareTypingInput(ShareTypingInputData data)
        {       
            
            // Ensure the group name matches how it was created in JoinRoom
            var groupName = data.Code.Trim();
            await Clients.OthersInGroup(groupName).SendAsync("ReceiveTypingInput", data.Username, data.Input);
        }
        [HubMethodName("KickUser")]
        public async Task KickUser(int code, string targetUserId, string targetUsername)
        {
            var callerUserId = Context.User?.FindFirst("userId")?.Value;
            if (string.IsNullOrWhiteSpace(callerUserId))
            {
                throw new HubException("Unauthorized");
            }

            var room = _roomRegistry.GetRoom(code);
            if (room == null)
            {
                throw new HubException("Room not found");
            }
            if (!string.Equals(room.OwnerUserId, callerUserId, StringComparison.Ordinal))
            {
                throw new HubException("Only the room owner can kick users");
            }

            // Find target connection by userId first, fallback to username
            var codeKey = code.ToString();
            if (!GameUsers.ContainsKey(codeKey)) return;
            var list = GameUsers[codeKey];

            var target = list.FirstOrDefault(u => 
                (!string.IsNullOrWhiteSpace(targetUserId) && u.UserId == targetUserId) ||
                (!string.IsNullOrWhiteSpace(targetUsername) && string.Equals(u.Username, targetUsername, StringComparison.OrdinalIgnoreCase)));

            if (target == null) return;

            // Remove from registry and memory list
            _roomRegistry.RemoveConnection(code, target.ConnectionId);
            list.RemoveAll(u => u.ConnectionId == target.ConnectionId);

            // Remove from groups and notify
            await Groups.RemoveFromGroupAsync(target.ConnectionId, codeKey);
            await Clients.Client(target.ConnectionId).SendAsync("Kicked");
            await Clients.Group(codeKey).SendAsync("UserKicked", new { userId = target.UserId, username = target.Username });
            
            // Broadcast room update to lobby
            await BroadcastRoomUpdateToLobby(code);
        }
        
        public async Task LeaveRoom(int code)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, code.ToString());
        }
    }
}
