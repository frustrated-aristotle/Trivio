using Microsoft.AspNetCore.SignalR;
using Trivio.Hubs;
using Trivio.Services;

namespace Trivio.Filters
{
    public class WordRepeatValidationFilter: IHubFilter
    {
        private readonly IRoomRegistry _roomRegistry;

        public WordRepeatValidationFilter(IRoomRegistry roomRegistry)
        {
            _roomRegistry = roomRegistry;
        }

        public async ValueTask<object> InvokeMethodAsync(HubInvocationContext ctx, Func<HubInvocationContext, ValueTask<object>> next)
        {
            if(ctx.HubMethodName=="SubmitGuess")
            {
                GuessData guessData = ctx.HubMethodArguments[0] as GuessData;
                string roomCode = guessData.Code;
                string word = guessData.Guess;
                var room = _roomRegistry.GetRoom(Int32.Parse(roomCode));
                if(room.WordList.Contains(word))
                {
                    return new { error = "Submitted already" };
                }
            }
            return await next(ctx);

        }
    }
}
