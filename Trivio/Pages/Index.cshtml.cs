using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Trivio.Enums;
using Trivio.Models;
using Trivio.Services;

namespace Trivio.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly IRoomRegistry _roomRegistry;
        private readonly TokenService _tokenService;

        public IndexModel(
            ILogger<IndexModel> logger,
            IRoomRegistry roomRegistry,
            TokenService tokenService)
        {
            _logger = logger;
            _roomRegistry = roomRegistry;
            _tokenService = tokenService;
        }

        public void OnGet()
        {
            var roomInfos = _roomRegistry.GetRoomInfos();
            ViewData["RoomInfos"] = roomInfos;
            _logger.LogInformation("Room infos: {RoomInfos}", roomInfos);
        }

        public IActionResult OnPostStartGame(
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
            _roomRegistry.CreateRoom(
                code,
                ownerConnectionId: string.Empty,
                ownerUsername: username ?? "Host",
                ownerRole: ownerRole == 0 ? Roles.Player : ownerRole,
                isPrivate: privateRoom,
                password: password
            );

            // Build claims DTO
            var dto = new UserRoomClaimsDTO
            {
                RoomCode = code,
                Username = username ?? "Host",
                UserId = Guid.NewGuid().ToString(),
                IsAdmin = isAdmin,
                Role = ownerRole
            };

            // Generate JWT token
            var token = _tokenService.CreateRoomToken(dto);

            // Store token for GamePage
            TempData["Token"] = token;

            return RedirectToPage("/GamePage", new { code });
        }

        public IActionResult OnPostAttend(
            string role,
            int roomCode,
            string username,
            string? password = null)
        {
            Console.WriteLine("username: " + username);

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
            var dto = new UserRoomClaimsDTO
            {
                RoomCode = roomCode,
                Username = username,
                UserId = Guid.NewGuid().ToString(),
                IsAdmin = false,
                Role = Roles.Player
            };

            // Generate token
            var token = _tokenService.CreateRoomToken(dto);
            TempData["Token"] = token;

            return RedirectToPage("/GamePage", new { code = roomCode });
        }
    }
}
