using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Trivio.Enums;
using Trivio.Hubs;
using Trivio.Models;
using Trivio.Services;

namespace Trivio.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly IRoomRegistry _roomRegistry;
        private readonly TokenService _tokenService;
        private readonly IHubContext<GameHub> _hubContext;
        
        public IndexModel(
            ILogger<IndexModel> logger,
            IRoomRegistry roomRegistry,
            TokenService tokenService,
            IHubContext<GameHub> hubContext)
        {
            _logger = logger;
            _roomRegistry = roomRegistry;
            _tokenService = tokenService;
            _hubContext = hubContext;
        }

        public void OnGet()
        {
            // Optional: Get initial room list for server-side rendering
            // Real-time updates will come via SignalR
            var roomInfos = _roomRegistry.GetRoomInfos();
            ViewData["RoomInfos"] = roomInfos;
            _logger.LogInformation("Room infos: {RoomInfos}", roomInfos);
        }

        public async Task<IActionResult> OnPostStartGame(
            bool isAdmin,
            string role,
            string username,
            bool privateRoom = false,
            string? password = null)
        {
            Console.WriteLine("username: " + username);

            // Store some values for after redirect
            TempData["IsAdmin"] = isAdmin;
            TempData["Role"] = role;
            TempData["Username"] = username;

            if (privateRoom && !string.IsNullOrEmpty(password))
                TempData["Password"] = password.Trim();

            // Generate room code
            var random = new Random();
            var code = random.Next(10000, 99999);

            Enum.TryParse<Roles>(role, true, out var ownerRole);

            // Create the room
            var room = _roomRegistry.CreateRoom(
                code,
                ownerConnectionId: string.Empty,
                ownerUsername: username ?? "Host",
                ownerRole: ownerRole == 0 ? Roles.Player : ownerRole,
                isPrivate: privateRoom,
                password: password
            );

            // Build claims DTO
            // Room creator is always admin
            var dto = new UserRoomClaimsDTO
            {
                RoomCode = code,
                Username = username ?? "Host",
                UserId = Guid.NewGuid().ToString(),
                IsAdmin = true, // Room creator is always admin
                Role = ownerRole
            };

            // Generate JWT token
            var token = _tokenService.CreateRoomToken(dto);

            // Store token for GamePage
            TempData["Token"] = token;
            
            // Broadcast room creation to lobby
            try
            {
                var roomData = new
                {
                    code = room.Code,
                    hostName = username ?? "Host",
                    playerCount = room.Connections.Count,
                    capacity = room.Capacity,
                    isPrivate = room.IsPrivate,
                    gameStarted = room.GameStarted,
                    createdAt = room.CreatedAtUtc.ToString("O")
                };
                await _hubContext.Clients.Group("lobby").SendAsync("RoomCreated", roomData);
                _logger.LogInformation("Broadcasted room creation to lobby for room {RoomCode}", code);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting room creation to lobby for room {RoomCode}", code);
            }

            return RedirectToPage("/GamePage", new { code });
        }

        public IActionResult OnPostAttend(
            string role,
            int roomCode,
            string username,
            string? password = null)
        {
            Console.WriteLine("username: " + username);

            // Validate and set defaults for required fields
            if (string.IsNullOrWhiteSpace(username))
            {
                ModelState.AddModelError("username", "Username is required.");
                return Page();
            }

            if (string.IsNullOrWhiteSpace(role))
            {
                role = "Player"; // Default role if not provided
            }

            TempData["IsAdmin"] = false;
            TempData["Role"] = role;
            TempData["Username"] = username;

            // Validate room code
            if (roomCode < 10000 || roomCode > 99999)
            {
                ModelState.AddModelError("roomCode", "Invalid room code.");
                return Page();
            }

            var room = _roomRegistry.GetRoom(roomCode);

            if (room == null)
            {
                ModelState.AddModelError("roomCode", "Room not found.");
                return Page();
            }

            // Check private password
            if (room.IsPrivate)
            {
                var trimmedPassword = string.IsNullOrWhiteSpace(password) ? null : password.Trim();
                var roomPassword = string.IsNullOrWhiteSpace(room.Password) ? null : room.Password.Trim();

                if (trimmedPassword != roomPassword)
                {
                    ModelState.AddModelError("password", "Invalid password for private room.");
                    TempData["ShowPasswordField"] = true;
                    return Page();
                }

                TempData["Password"] = trimmedPassword;
            }

            // Build claims DTO for attendee
            Enum.TryParse<Roles>(role, true, out var attendeeRole);
            if (attendeeRole == 0)
            {
                attendeeRole = Roles.Player; // Default to Player if parsing fails
            }

            var dto = new UserRoomClaimsDTO
            {
                RoomCode = roomCode,
                Username = username ?? "Guest",
                UserId = Guid.NewGuid().ToString(),
                IsAdmin = false,
                Role = attendeeRole
            };

            // Generate token
            var token = _tokenService.CreateRoomToken(dto);
            TempData["Token"] = token;

            return RedirectToPage("/GamePage", new { code = roomCode });
        }
    }
}
