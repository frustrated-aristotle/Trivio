using Microsoft.AspNetCore.SignalR;
using Trivio.Enums;
using Trivio.Models;
using Trivio.Services;

namespace Trivio.Hubs
{


    public class User
    {
        public string ConnectionId { get; set; }
        public string Username { get; set; } // Önemli: Gerçek ad
        public string Role { get; set; }     // player veya spectator
        public int Points { get; set; } = 0; // User's total points
    }

    public class GuessData
    {
        public string Guess { get; set; }
        public string Code { get; set; }
        public string Username { get; set; }
    }

    public class ShareTypingInputData
    {
        public string Username { get; set; }
        public string Input { get; set; }
        public string Code { get; set; }
    }

    public class GameHub : Hub
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
                            // Check if the disconnected user was the room owner
                            bool wasOwner = room.OwnerConnectionId == Context.ConnectionId;
                            
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
                
                // Clean up the room after a delay
                _ = Task.Delay(5000).ContinueWith(async _ => {
                    await CleanupEmptyRoom(roomCode);
                });
            }
        }

        private async Task CleanupEmptyRoom(int roomCode)
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
        }

        [HubMethodName("CreateRoom")]
        public async Task<int> CreateRoom(int code, string role, string username)
        {
            // Create room in registry
            Enum.TryParse<Roles>(role, true, out var parsedRole);
            _roomRegistry.CreateRoom(code, Context.ConnectionId, username, parsedRole);
            return code;
        }

        [HubMethodName("JoinRoom")] //For the client, we may need to change it. 
        public async Task JoinRoom(int code, string role, string username)
        {
            try
            {
                _logger.LogInformation("User {Username} attempting to join room {RoomCode} as {Role}", 
                    username, code, role);

                var newUser = new User
                {
                    ConnectionId = Context.ConnectionId,
                    Username = username,
                    Role = role
                };

                // Validate/add via registry first
                Enum.TryParse<Roles>(role, true, out var parsedRole);
                if (!_roomRegistry.TryAddConnection(code, Context.ConnectionId, username, parsedRole, out var reason))
                {
                    _logger.LogWarning("Failed to join room {RoomCode}: {Reason}", code, reason);
                    throw new HubException(reason ?? "Join failed");
                }

                // If owner not set, first joiner becomes owner
                var room = _roomRegistry.GetRoom(code);
                if (room != null && string.IsNullOrEmpty(room.OwnerConnectionId))
                {
                    room.OwnerConnectionId = Context.ConnectionId;
                    _logger.LogInformation("User {Username} became owner of room {RoomCode}", username, code);
                }

                // 1. Yeni Kullanıcıyı Veri Yapısına Ekleme
                if (!GameUsers.ContainsKey(code.ToString()))
                {
                    GameUsers[code.ToString()] = new List<User>();
                }
                var existingUsers = GameUsers[code.ToString()].ToList();
                GameUsers[code.ToString()].Add(newUser);

                await Clients.Caller.SendAsync("ReceiveUserList", existingUsers);

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
                await Clients.Group(code.ToString()).SendAsync("UserJoined", username, role);
                await Clients.Caller.SendAsync("JoinSuccess", code, role);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error joining room {RoomCode} for user {Username}", code, username);
                throw new HubException("An error occurred while joining the room");
            }
        }

        public async Task StartTheGame(int code, int wordCount)
        {
            var room = _roomRegistry.GetRoom(code);
            if (room == null)
            {
                throw new HubException("Room not found");
            }
            if (!string.Equals(room.OwnerConnectionId, Context.ConnectionId, StringComparison.Ordinal))
            {
                throw new HubException("Only the room owner can start the game");
            }

            // Initialize game state for consonant game
            room.GameStarted = true;
            room.GameCompleted = false;
            room.RoundNumber = 1;

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
            
            await Clients.Group(code.ToString()).SendAsync("RoundStarted", new { 
                consonants = room.CurrentConsonants.ToArray(),
                roundNumber = room.RoundNumber,
                message = $"Round {room.RoundNumber}: Use only these consonants: {string.Join(", ", room.CurrentConsonants.Select(c => char.ToUpper(c)))}"
            });
        }

        public async Task SubmitGuess(GuessData guessData)
        {
            if (guessData == null || string.IsNullOrWhiteSpace(guessData.Guess) || 
                string.IsNullOrWhiteSpace(guessData.Code) || string.IsNullOrWhiteSpace(guessData.Username))
            {
                await Clients.Caller.SendAsync("GuessResult", new { success = false, message = "Invalid guess data" });
                return;
            }

            var roomCode = int.Parse(guessData.Code);
            var room = _roomRegistry.GetRoom(roomCode);
            if (room == null || !room.GameStarted || room.CurrentConsonants == null || room.CurrentConsonants.Count == 0)
            {
                await Clients.Caller.SendAsync("GuessResult", new { success = false, message = "Game not started or no current consonants" });
                return;
            }

            var word = guessData.Guess.Trim().ToLower();

            // Check if word uses only allowed consonants
            if (!_wordService.IsValidWord(word, room.CurrentConsonants))
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
                return;
            }

            // Move directly to next round
            room.RoundNumber++;
            await StartNewRound(roomCode);
        }

        private async Task SendUpdatedUserList(int roomCode)
        {
            var codeKey = roomCode.ToString();
            if (GameUsers.ContainsKey(codeKey))
            {
                var users = GameUsers[codeKey];
                await Clients.Group(codeKey).SendAsync("UserListUpdated", users);
            }
        }

        [HubMethodName("CloseRoom")]
        public async Task CloseRoom(int code)
        {
            try
            {
                var room = _roomRegistry.GetRoom(code);
                if (room == null)
                {
                    await Clients.Caller.SendAsync("ServerMessage", "Room not found", "error");
                    return;
                }

                // Check if the caller is the room owner
                if (room.OwnerConnectionId != Context.ConnectionId)
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
        public async Task KickUser(int code, string targetUsername)
        {
            var room = _roomRegistry.GetRoom(code);
            if (room == null)
            {
                throw new HubException("Room not found");
            }
            if (!string.Equals(room.OwnerConnectionId, Context.ConnectionId, StringComparison.Ordinal))
            {
                throw new HubException("Only the room owner can kick users");
            }

            // Find target connection by username
            var codeKey = code.ToString();
            if (!GameUsers.ContainsKey(codeKey)) return;
            var list = GameUsers[codeKey];
            var target = list.FirstOrDefault(u => string.Equals(u.Username, targetUsername, StringComparison.OrdinalIgnoreCase));
            if (target == null) return;

            // Remove from registry and memory list
            _roomRegistry.RemoveConnection(code, target.ConnectionId);
            list.RemoveAll(u => u.ConnectionId == target.ConnectionId);

            // Remove from groups and notify
            await Groups.RemoveFromGroupAsync(target.ConnectionId, codeKey);
            await Clients.Client(target.ConnectionId).SendAsync("Kicked");
            await Clients.Group(codeKey).SendAsync("UserKicked", targetUsername);
        }

        public async Task LeaveRoom(int code)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, code.ToString());
        }
    }
}
