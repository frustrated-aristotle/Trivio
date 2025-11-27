using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using Trivio.Enums;
using Trivio.Hubs;

namespace Trivio.Pages
{
    public class GamePageModel : PageModel
    {
        public int Code { get; set; }

        public bool IsAdmin { get; set; }
        public Roles Role { get; set; }
        public string Username { get; set; } = string.Empty;
        public void OnGet(int code)
        {
            Code = code;
            // Read from TempData (one-time). Provide defaults if missing.
            if (TempData.ContainsKey("IsAdmin"))
            {
                IsAdmin = bool.TryParse(TempData["IsAdmin"]?.ToString(), out var b) && b;
            }

            if (TempData.ContainsKey("Role"))
            {
                Enum.TryParse<Roles>(TempData["Role"]?.ToString(), true, out var parsedRole);
                Role = parsedRole;
            }
            if (TempData.ContainsKey("Username"))
            {
                Username = TempData["Username"]?.ToString() ?? "Guest";
            }
            else
            {
                Username = "Guest"; // Default username if none provided
            }
            //Code validation here. 
        }
    }
}
