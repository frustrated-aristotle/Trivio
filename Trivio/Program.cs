using Microsoft.AspNetCore.SignalR;
using Trivio.Filters;
using Trivio.Hubs;
using Trivio.Services;
using StackExchange.Redis;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Trivio.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
// Configure SignalR with Redis Backplane
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSignalR(options =>
    { 
        options.AddFilter<RoomValidationFilter>();
        options.AddFilter<WordRepeatValidationFilter>();
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
        options.HandshakeTimeout = TimeSpan.FromSeconds(15);
    }
).AddStackExchangeRedis(redisConnectionString, options =>
{
    // Configure Redis backplane options
    options.Configuration.ChannelPrefix = RedisChannel.Literal("Trivio"); // Prefix for Redis channels
    options.Configuration.AbortOnConnectFail = false; // Don't abort on connection failures
    options.Configuration.ConnectRetry = 3; // Retry connection attempts
    options.Configuration.ConnectTimeout = 5000; // 5 second connection timeout
});

// Add Redis services
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(connectionString);
});

// Register IDatabase for RoomRegistry
builder.Services.AddSingleton<IDatabase>(sp =>
{
    var redis = sp.GetRequiredService<IConnectionMultiplexer>();
    return redis.GetDatabase();
});

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = builder.Configuration["Redis:InstanceName"];
});

// Bind JWT options from configuration for TokenService
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

builder.Services.AddSingleton<IRoomRegistry, RoomRegistry>();
builder.Services.AddSingleton<RoomValidationFilter>();
builder.Services.AddSingleton<IWordService, WordService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtSection = builder.Configuration.GetSection("Jwt");

        var secretKey = jwtSection["SecretKey"] ?? throw new Exception("Missing SecretKey");
        var issuer = jwtSection["Issuer"] ?? throw new Exception("Missing Issuer");
        var audience = jwtSection["Audience"] ?? issuer;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = key,

            ClockSkew = TimeSpan.FromMinutes(5)
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];

                if (!string.IsNullOrEmpty(token) &&
                    context.Request.Path.StartsWithSegments("/gameHub"))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddSingleton<TokenService>();
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Map SignalR hub
app.MapHub<GameHub>("/gameHub");


app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
