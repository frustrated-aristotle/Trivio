using Trivio.Enums;
using Trivio.Models;

namespace Trivio.Services
{
    public interface IRoomRegistry
    {
        Room CreateRoom(int code, string ownerConnectionId, string ownerUsername, Roles ownerRole, int capacity = 8);
        Room? GetRoom(int code);
        bool TryAddConnection(int code, string connectionId, string username, Roles role, out string? reason);
        void RemoveConnection(int code, string connectionId);
        bool CloseRoom(int code);
    }
}


