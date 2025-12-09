using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Trivio.Models;
using Trivio.Options;

public class TokenService
{
    private readonly JwtOptions _jwt;

    public TokenService(IOptions<JwtOptions> jwtOptions)
    {
        _jwt = jwtOptions.Value;
    }

    public string CreateRoomToken(UserRoomClaimsDTO dto)
    {
        // Ensure all values are non-null
        var roomCode = dto.RoomCode.ToString();
        var username = dto.Username ?? "Guest";
        var userId = dto.UserId ?? Guid.NewGuid().ToString();
        var roleString = dto.Role.ToString();
        
        var claims = new List<Claim>
        {
            new Claim("room", roomCode),
            new Claim("username", username),
            new Claim("userId", userId),
            new Claim("isAdmin", dto.IsAdmin.ToString()),
            new Claim("role", roleString), // Game role (Player, Spectator, etc.)
            // Add proper Role claim for ASP.NET Core authorization
            new Claim(ClaimTypes.Role, dto.IsAdmin ? "admin" : "player")
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_jwt.SecretKey)
        );

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
