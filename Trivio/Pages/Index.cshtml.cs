using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Trivio.Enums;
using Trivio.Services;

namespace Trivio.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly IRoomRegistry _roomRegistry;

        public IndexModel(ILogger<IndexModel> logger, IRoomRegistry roomRegistry)
        {
            _logger = logger;
            _roomRegistry = roomRegistry;
        }

        public void OnGet()
        {

        }

        public IActionResult OnPostStartGame(bool isAdmin, string role, string username, bool privateRoom = false, string? password = null)
        {
            Console.WriteLine("username: " + username);

            // Persist values for one redirect without exposing in URL
            TempData["IsAdmin"] = isAdmin;
            TempData["Role"] = role;
            TempData["Username"] = username;
            TempData["Password"] = password;

            // Generate code on server to keep flow consistent and tamper-proof
            var random = new Random();
            var code = random.Next(10000, 99999);
            // Create room without binding an owner connection yet; owner set on first JoinRoom
            Enum.TryParse<Roles>(role, true, out var ownerRole);
            _roomRegistry.CreateRoom(code, ownerConnectionId: string.Empty, ownerUsername: username ?? "Host", ownerRole: ownerRole == 0 ? Roles.Player : ownerRole, isPrivate: privateRoom, password: password);
            return RedirectToPage("/GamePage", new { code });
        }

        public IActionResult OnPostAttend(string role, int roomCode, string username, string? password = null)
        {
            Console.WriteLine("username: " + username);
            TempData["IsAdmin"] = false; // attending users are not admins
            TempData["Role"] = role;
            TempData["Username"] = username;
            // Store password in TempData so it can be used when joining via SignalR
            if (!string.IsNullOrEmpty(password))
            {
                TempData["Password"] = password;
            }
            
            // Validate room code lightly
            var code = roomCode;
            if (code < 10000 || code > 99999)
            {
                ModelState.AddModelError("roomCode", "Invalid room code.");
                return Page();
            }

            // Check if room exists and if it's private
            var room = _roomRegistry.GetRoom(code);
            if (room == null)
            {
                ModelState.AddModelError("roomCode", "Room not found.");
                return Page();
            }

            // If room is private, validate password
            if (room.IsPrivate)
            {
                if (string.IsNullOrEmpty(password) || password != room.Password)
                {
                    ModelState.AddModelError("password", "Invalid password for private room.");
                    TempData["ShowPasswordField"] = true;
                    return Page();
                }
            }

            return RedirectToPage("/GamePage", new { code });
        }
    }
}
