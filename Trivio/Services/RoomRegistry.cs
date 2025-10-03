using System.Collections.Concurrent;
using Trivio.Enums;
using Trivio.Models;

namespace Trivio.Services
{
    public class RoomRegistry : IRoomRegistry
    {
        private readonly ConcurrentDictionary<int, Room> _rooms = new();

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
    }
}


