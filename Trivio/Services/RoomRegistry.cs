using System.Collections.Concurrent;
using StackExchange.Redis;
using Trivio.Enums;
using Trivio.Models;
using System.Text.Json;

namespace Trivio.Services
{
    public class RoomRegistry : IRoomRegistry
    {
        private readonly ConcurrentDictionary<int, Room> _rooms = new();
        private readonly Timer _cleanupTimer;
        private readonly ILogger<RoomRegistry> _logger;
        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly IDatabase _database;
        public RoomRegistry(ILogger<RoomRegistry> logger, IConnectionMultiplexer connectionMultiplexer, IDatabase database)
        {
            _logger = logger;
            // Run cleanup every 5 minutes
            _cleanupTimer = new Timer(CleanupExpiredRooms, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            _connectionMultiplexer = connectionMultiplexer;
            _database = database;
        }

        public Room CreateRoom(int code, string ownerConnectionId, string ownerUsername, Roles ownerRole, int capacity = 8)
        {
            var room = new Room
            {
                Code = code,
                Capacity = capacity,
                OwnerConnectionId = ownerConnectionId,
                OwnerRole = ownerRole
            };

            // Add connection to room only if connectionId is not empty
            // (When called from server-side POST, connectionId is empty and will be set on JoinRoom)
            if (!string.IsNullOrEmpty(ownerConnectionId))
            {
                room.Connections[ownerConnectionId] = (ownerUsername, ownerRole);
                _logger.LogInformation("Added connection {ConnectionId} for user {Username} during room creation", 
                    ownerConnectionId, ownerUsername);
            }
            else
            {
                _logger.LogInformation("Room {RoomCode} created without initial connection (will be added on JoinRoom)", code);
            }

            // Save to memory cache
            _rooms[code] = room;

            // Save to Redis after connections are added
            try
            {
                if (IsRedisAvailable())
                {
                    SaveRoomToRedisAsync(room).GetAwaiter().GetResult();
                    _logger.LogInformation("Created room {RoomCode} and saved to Redis", code);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error saving room {RoomCode} to Redis, continuing with memory only", code);
                // Continue without Redis - graceful degradation
            }

            return room;
        }

        public Room? GetRoom(int code)
        {
            // 1. Check memory cache first (fast)
            if (_rooms.TryGetValue(code, out var room))
            {
                return room;
            }

            // 2. If not in memory, check Redis
            try
            {
                if (IsRedisAvailable())
                {
                    room = LoadRoomFromRedis(code).GetAwaiter().GetResult();
                    
                    // 3. If found in Redis, cache in memory
                    if (room != null)
                    {
                        _rooms[code] = room;
                        _logger.LogInformation("Loaded room {RoomCode} from Redis and cached in memory", code);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error loading room {RoomCode} from Redis", code);
                // Continue without Redis - graceful degradation
            }

            return room;
        }

        public bool TryAddConnection(int code, string connectionId, string username, Roles role, out string? reason)
        {
            reason = null;
            
            // Always refresh from Redis first to get the latest state (especially important for multi-server scenarios)
            // This ensures we're working with the most up-to-date room data
            var room = RefreshRoomFromRedis(code);
            if (room == null)
            {
                reason = "Room not found";
                return false;
            }

            if (room.IsClosed)
            {
                reason = "Room is closed";
                return false;
            }

            // FIRST: Remove any old connections for the same username (handle reconnection scenario)
            // This prevents the same user from appearing multiple times with different ConnectionIds
            // Do this BEFORE checking if the current connectionId exists
            var connectionsToRemove = room.Connections
                .Where(kvp => kvp.Value.Username == username && kvp.Key != connectionId)
                .Select(kvp => kvp.Key)
                .ToList();

            if (connectionsToRemove.Any())
            {
                _logger.LogInformation("Found {Count} old connection(s) for user {Username} in room {RoomCode}, removing them", 
                    connectionsToRemove.Count, username, code);
            }

            foreach (var oldConnectionId in connectionsToRemove)
            {
                room.Connections.TryRemove(oldConnectionId, out _);
                _logger.LogInformation("Removed old connection {OldConnectionId} for user {Username} in room {RoomCode} (new connection: {NewConnectionId})", 
                    oldConnectionId, username, code, connectionId);
            }

            // Check if connection already exists (avoid duplicates)
            if (room.Connections.ContainsKey(connectionId))
            {
                // Connection already exists, just update the username/role if different
                var existing = room.Connections[connectionId];
                if (existing.Username != username || existing.Role != role)
                {
                    room.Connections[connectionId] = (username, role);
                    _logger.LogInformation("Updated existing connection {ConnectionId} in room {RoomCode}", connectionId, code);
                }
                else
                {
                    _logger.LogDebug("Connection {ConnectionId} already exists in room {RoomCode}, skipping add", connectionId, code);
                }
                // Update Redis after removing old connections
                _rooms[code] = room;
                try
                {
                    if (IsRedisAvailable())
                    {
                        SaveRoomToRedisAsync(room).GetAwaiter().GetResult();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error saving room {RoomCode} to Redis after removing old connections", code);
                }
                return true;
            }

            if (room.Connections.Count >= room.Capacity && role == Roles.Player)
            {
                reason = "Room is full";
                return false;
            }

            // Add connection to room
            room.Connections[connectionId] = (username, role);

            // Update memory cache
            _rooms[code] = room;

            // Update Redis with new connection
            try
            {
                if (IsRedisAvailable())
                {
                    SaveRoomToRedisAsync(room).GetAwaiter().GetResult();
                    _logger.LogInformation("Added connection {ConnectionId} to room {RoomCode} and saved to Redis", connectionId, code);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error saving room {RoomCode} to Redis after adding connection", code);
                // Continue without Redis - graceful degradation
            }

            return true;
        }

        public void RemoveConnection(int code, string connectionId)
        {
            var room = GetRoom(code);
            if (room != null)
            {
                room.Connections.TryRemove(connectionId, out _);

                // Update memory cache
                _rooms[code] = room;

                // Update Redis
                try
                {
                    if (IsRedisAvailable())
                    {
                        SaveRoomToRedisAsync(room).GetAwaiter().GetResult();
                        _logger.LogInformation("Removed connection {ConnectionId} from room {RoomCode} and saved to Redis", connectionId, code);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error saving room {RoomCode} to Redis after removing connection", code);
                    // Continue without Redis - graceful degradation
                }
            }
        }

        public bool CloseRoom(int code)
        {
            var room = GetRoom(code);
            if (room != null)
            {
                room.IsClosed = true;

                // Update memory cache
                _rooms[code] = room;

                // Update Redis
                try
                {
                    if (IsRedisAvailable())
                    {
                        SaveRoomToRedisAsync(room).GetAwaiter().GetResult();
                        _logger.LogInformation("Closed room {RoomCode} and saved to Redis", code);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error saving room {RoomCode} to Redis after closing", code);
                    // Continue without Redis - graceful degradation
                }

                return true;
            }
            return false;
        }

        public void UpdateRoomState(Room room)
        {
            if (room == null)
            {
                return;
            }

            // Update memory cache
            _rooms[room.Code] = room;

            // Update Redis
            try
            {
                if (IsRedisAvailable())
                {
                    SaveRoomToRedisAsync(room).GetAwaiter().GetResult();
                    _logger.LogDebug("Updated room {RoomCode} state and saved to Redis", room.Code);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error saving room {RoomCode} state to Redis", room.Code);
                // Continue without Redis - graceful degradation
            }
        }

        public Room? RefreshRoomFromRedis(int code)
        {
            // Clear memory cache to force reload from Redis
            _rooms.TryRemove(code, out _);
            
            // Load fresh from Redis
            return GetRoom(code);
        }

        private void CleanupExpiredRooms(object? state)
        {
            var expiredRooms = new List<int>();
            var cutoffTime = DateTime.UtcNow.AddHours(-2); // Rooms expire after 2 hours of inactivity

            foreach (var kvp in _rooms)
            {
                var room = kvp.Value;
                if (room.Connections.IsEmpty || room.CreatedAtUtc < cutoffTime)
                {
                    expiredRooms.Add(kvp.Key);
                }
            }

            foreach (var roomCode in expiredRooms)
            {
                _rooms.TryRemove(roomCode, out _);
                _logger.LogInformation("Cleaned up expired room {RoomCode}", roomCode);
            }

            if (expiredRooms.Any())
            {
                _logger.LogInformation("Cleaned up {Count} expired rooms", expiredRooms.Count);
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
        }

        private string GetRoomKey(int key)
        {
            return $"room:{key}";
        }
        
        private bool IsRedisAvailable()
        {
            try
            {
                if (_database == null || !_connectionMultiplexer.IsConnected)
                {
                    return false;
                }
                // Ping is synchronous, no need for await
                _database.Ping();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Redis is not available");
                return false;
            }
        }
        private string SerializeRoom(Room room)
        {
            if (room == null)
            {
                return "{}"; // Bo� JSON objesi veya null d�nd�rebilirsin
            }
            try
            {
                // Use JSON options that handle ConcurrentDictionary and tuples
                var options = new JsonSerializerOptions
                {
                    WriteIndented = false,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    // Handle reference cycles
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
                };

                return JsonSerializer.Serialize(room, options);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error serializing room {RoomCode}", room.Code);
                return "{}";
            }
        }
        private Room? DeserializeRoom(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    // Allow reading numbers from strings (for backward compatibility)
                    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                };

                var room = JsonSerializer.Deserialize<Room>(json, options);
                
                if (room != null)
                {
                    // Verify ConnectionsData was deserialized and populated Connections
                    var connectionCount = room.Connections?.Count ?? 0;
                    _logger.LogDebug("Deserialized room {RoomCode}, Connections dictionary has {Count} entries", 
                        room.Code, connectionCount);
                    
                    // If Connections is empty but JSON might have had connections, log a warning
                    if (connectionCount == 0 && json.Contains("connections", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("Room {RoomCode} deserialized but Connections dictionary is empty despite connections in JSON. JSON snippet: {JsonSnippet}", 
                            room.Code, json.Substring(0, Math.Min(200, json.Length)));
                    }
                }
                
                if (room != null)
                {
                    _logger.LogDebug("Deserialized room {RoomCode} from Redis with {ConnectionCount} connections", 
                        room.Code, room.Connections?.Count ?? 0);
                    
                    // Check if Connections is populated - if it's empty, JSON deserialization might have failed
                    // for the read-only dictionary. This is expected behavior with read-only properties.
                    // The connections are stored in Redis but might need manual population after deserialization.
                }

                // Connections dictionary is read-only and already initialized in Room class
                // JSON deserialization might not populate it correctly due to read-only property
                // We rely on the Connections being populated when the room is created/updated
                return room;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deserializing room from JSON: {Error}", ex.Message);
                return null;
            }
        }
        private async Task SaveRoomToRedisAsync(Room room)
        {
            if (room == null || !IsRedisAvailable())
            {
                return;
            }

            try
            {
                string key = GetRoomKey(room.Code);
                string jsonData = SerializeRoom(room);

                if (string.IsNullOrEmpty(jsonData))
                {
                    _logger.LogWarning("Failed to serialize room {RoomCode}", room.Code);
                    return;
                }

                // Log connection count before saving
                var connectionCount = room.Connections?.Count ?? 0;
                _logger.LogDebug("Saving room {RoomCode} to Redis with {ConnectionCount} connections. JSON length: {Length}", 
                    room.Code, connectionCount, jsonData.Length);
                
                // Verify connections are in JSON
                if (connectionCount > 0 && !jsonData.Contains("connections", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Room {RoomCode} has {ConnectionCount} connections but 'connections' not found in JSON!", 
                        room.Code, connectionCount);
                }

                TimeSpan expiry = TimeSpan.FromHours(2); // Match room expiration time
                bool success = await _database.StringSetAsync(key, jsonData, expiry);

                if (!success)
                {
                    _logger.LogWarning("Failed to save room {RoomCode} to Redis", room.Code);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving room {RoomCode} to Redis", room.Code);
                throw; // Re-throw to be caught by caller
            }
        }

        private async Task<Room?> LoadRoomFromRedis(int code)
        {
            if (code <= 0 || !IsRedisAvailable())
            {
                return null;
            }

            try
            {
                string key = GetRoomKey(code);
                var redisValue = await _database.StringGetAsync(key);

                if (!redisValue.HasValue || redisValue.IsNullOrEmpty)
                {
                    return null;
                }

                var jsonString = redisValue.ToString();
                _logger.LogDebug("Loading room {RoomCode} from Redis. JSON length: {Length}", code, jsonString.Length);
                
                Room? room = DeserializeRoom(jsonString);

                if (room != null)
                {
                    // Log connections count after deserialization
                    var connectionCount = room.Connections?.Count ?? 0;
                    _logger.LogInformation("Successfully loaded room {RoomCode} from Redis with {ConnectionCount} connections", 
                        code, connectionCount);
                    
                    // Log each connection for debugging
                    foreach (var conn in room.Connections)
                    {
                        _logger.LogDebug("Room {RoomCode} connection: {ConnectionId} -> {Username} ({Role})", 
                            code, conn.Key, conn.Value.Username, conn.Value.Role);
                    }
                }
                else
                {
                    _logger.LogWarning("Deserialized room {RoomCode} is null", code);
                }

                return room;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error loading room {RoomCode} from Redis", code);
                return null;
            }
        }
    }
}


