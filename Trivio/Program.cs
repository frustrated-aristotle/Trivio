using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Trivio.Filters;
using Trivio.Hubs;
using Trivio.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

// Configure SignalR with Redis Backplane
var redisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSignalR(options =>
    { 
        options.AddFilter<RoomValidationFilter>();
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

builder.Services.AddSingleton<IRoomRegistry, RoomRegistry>();
builder.Services.AddSingleton<RoomValidationFilter>();
builder.Services.AddSingleton<IWordService, WordService>();

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

app.UseAuthorization();
app.UseEndpoints(endpoints => 
{
    endpoints.MapRazorPages();
    endpoints.MapHub<GameHub>("/gameHub");
});
app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
