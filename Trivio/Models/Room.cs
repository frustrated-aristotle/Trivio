using System.Collections.Concurrent;
using Trivio.Enums;

namespace Trivio.Models
{
    public class Room
    {
        public int Code { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAtUtc { get; set; }
        public int Capacity { get; set; } = 8;
        public string? OwnerConnectionId { get; set; }
        public Roles OwnerRole { get; set; } = Roles.Player;
        public ConcurrentDictionary<string, (string Username, Roles Role)> Connections { get; } = new();
        public bool IsClosed { get; set; }
        
        // Game state properties
        public List<string> WordList { get; set; } = new();
        public int CurrentWordIndex { get; set; } = 0;
        public string? CurrentWord { get; set; }
        public bool GameStarted { get; set; } = false;
        public bool GameCompleted { get; set; } = false;
        
        // New consonant game properties
        public List<char> CurrentConsonants { get; set; } = new();
        public int RoundNumber { get; set; } = 1;
    }
}
