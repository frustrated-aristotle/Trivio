using System.Security.Principal;
using Microsoft.AspNetCore.SignalR;
using Trivio.Services;

namespace Trivio.Filters
{
    public class RoomValidationFilter : IHubFilter
    {
        IRoomRegistry roomRegistry;

        public RoomValidationFilter(IRoomRegistry roomRegistry)
        {
            this.roomRegistry = roomRegistry;
        }
        //First, we check if there is that room. Then we check its status. Capacity and username uniqueness comes right after.
        //TODO: We need room password too. 
        public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext ctx, Func<HubInvocationContext, ValueTask<object?>> next)
        {
            if (ctx.HubMethodName == "JoinRoom" && ctx.HubMethodArguments.Count > 0 && ctx.HubMethodArguments[0] is int roomCode)
            {
                string username = ctx.HubMethodArguments.Count > 2 && ctx.HubMethodArguments[2] is string u ? u : "Guest";
                var room = roomRegistry.GetRoom(roomCode);
                if (room == null)
                {
                    Console.WriteLine("No room found.");
                        throw new HubException("Room not found");
                }
                if (room.IsClosed)
                {
                    throw new HubException("Room is closed");
                }
                if (room.Connections.Count >= room.Capacity)
                {
                    throw new HubException("Room is full");
                }
                // Check if username is already taken in this room
                
            }
            return await next(ctx);
        }
    }
}
// We have used this filter as a global filter. 
// Its best to use an attribute instead since 
// validation only works when JoinRoom is called