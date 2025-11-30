using System.Collections.Concurrent;
using System.Text.Json.Serialization;
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
        public bool IsPrivate { get; set; } = false;
        public string? Password { get; set; }
        // Read-only Connections dictionary (used at runtime)
        [JsonIgnore]
        public ConcurrentDictionary<string, (string Username, Roles Role)> Connections { get; } = new();
        
        // Serializable property for Connections (used for Redis serialization)
        [JsonPropertyName("connections")]
        public Dictionary<string, ConnectionInfo> ConnectionsData 
        { 
            get 
            {
                return Connections.ToDictionary(
                    kvp => kvp.Key, 
                    kvp => new ConnectionInfo { Username = kvp.Value.Username, Role = kvp.Value.Role }
                );
            }
            set
            {
                Connections.Clear();
                if (value != null)
                {
                    foreach (var kvp in value)
                    {
                        Connections[kvp.Key] = (kvp.Value.Username, kvp.Value.Role);
                    }
                }
            }
        }
        
        public DateTime RoundStartedAt { get; set; }
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
    
    // Helper class for serializing connections
    public class ConnectionInfo
    {
        public string Username { get; set; } = string.Empty;
        public Roles Role { get; set; }
    }
}
