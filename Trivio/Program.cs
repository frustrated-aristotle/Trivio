using Microsoft.AspNetCore.SignalR;
using Trivio.Filters;
using Trivio.Hubs;
using Trivio.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddSignalR(options =>
    { 
        options.AddFilter<RoomValidationFilter>();
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
        options.HandshakeTimeout = TimeSpan.FromSeconds(15);
    }
);
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
