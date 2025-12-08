using Trivio.Enums;

namespace Trivio.Models
{
    public class UserRoomClaimsDTO
    {
        public int RoomCode { get; set; }
        public string Username { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty; //GUID
        public bool IsAdmin { get; set; }
        public Roles Role { get; set; }
    }
}
