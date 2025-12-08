using Trivio.Enums;
using Trivio.Models;

namespace Trivio.Services
{
    public interface IRoomRegistry : IDisposable
    {
        Room CreateRoom(int code, string ownerConnectionId, string ownerUsername, Roles ownerRole, int capacity = 8, bool isPrivate = false, string? password = null);
        Room? GetRoom(int code);

        bool TryAddConnection(int code, string connectionId, string userId, string username, string? password, Roles role, bool isAdmin, out string? reason);
        void RemoveConnection(int code, string connectionId);
        bool CloseRoom(int code);
        void UpdateRoomState(Room room);
        Room? RefreshRoomFromRedis(int code);
        List<RoomInfo> GetRoomInfos();
    }
}


