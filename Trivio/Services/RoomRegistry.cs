using System.Collections.Concurrent;
using Trivio.Enums;
using Trivio.Models;

namespace Trivio.Services
{
    public class RoomRegistry : IRoomRegistry
    {
        private readonly ConcurrentDictionary<int, Room> _rooms = new();
        private readonly Timer _cleanupTimer;
        private readonly ILogger<RoomRegistry> _logger;

        public RoomRegistry(ILogger<RoomRegistry> logger)
        {
            _logger = logger;
            // Run cleanup every 5 minutes
            _cleanupTimer = new Timer(CleanupExpiredRooms, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
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

            _rooms[code] = room;
            room.Connections[ownerConnectionId] = (ownerUsername, ownerRole);
            return room;
        }

        public Room? GetRoom(int code)
        {
            _rooms.TryGetValue(code, out var room);
            return room;
        }

        public bool TryAddConnection(int code, string connectionId, string username, Roles role, out string? reason)
        {
            reason = null;
            if (!_rooms.TryGetValue(code, out var room))
            {
                reason = "Room not found";
                return false;
            }

            if (room.IsClosed)
            {
                reason = "Room is closed";
                return false;
            }

            if (room.Connections.Count >= room.Capacity && role == Roles.Player)
            {
                reason = "Room is full";
                return false;
            }

            room.Connections[connectionId] = (username, role);
            return true;
        }

        public void RemoveConnection(int code, string connectionId)
        {
            if (_rooms.TryGetValue(code, out var room))
            {
                room.Connections.TryRemove(connectionId, out _);
            }
        }

        public bool CloseRoom(int code)
        {
            if (_rooms.TryGetValue(code, out var room))
            {
                room.IsClosed = true;
                return true;
            }
            return false;
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
    }
}


