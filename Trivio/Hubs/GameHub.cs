using Microsoft.AspNetCore.SignalR;
using Trivio.Enums;
using Trivio.Services;

namespace Trivio.Hubs
{


    public class User
    {
        public string ConnectionId { get; set; }
        public string Username { get; set; } // Önemli: Gerçek ad
        public string Role { get; set; }     // player veya spectator
    }
    public class GameHub : Hub
    {
        private readonly IRoomRegistry _roomRegistry;

        public GameHub(IRoomRegistry roomRegistry)
        {
            _roomRegistry = roomRegistry;
        }

        public static readonly Dictionary<string, List<User>> GameUsers = new();

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
                throw new HubException(reason ?? "Join failed");
            }

            // If owner not set, first joiner becomes owner
            var room = _roomRegistry.GetRoom(code);
            if (room != null && string.IsNullOrEmpty(room.OwnerConnectionId))
            {
                room.OwnerConnectionId = Context.ConnectionId;
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
            Console.WriteLine("Role: " + role);
            //Then, add them to a role-based group within that room.
            if(role == "player")
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"{code}-players");
                
            }
            else if (role == "spectator")
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"{code}-spectators");
            }

            Console.WriteLine("UserJoined: " + username + " - " + role);
            await Clients.Group(code.ToString()).SendAsync("UserJoined", username, role);
            await Clients.Caller.SendAsync("JoinSuccess", code, role);
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
