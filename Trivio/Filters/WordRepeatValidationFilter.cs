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

        public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext ctx, Func<HubInvocationContext, ValueTask<object?>> next)
        {
            if (ctx.HubMethodName == "SubmitGuess")
            {
                if (ctx.HubMethodArguments.Count == 0 || ctx.HubMethodArguments[0] is not GuessData guessData)
                {
                    return await next(ctx);
                }

                var roomCode = guessData.Code;
                var word = guessData.Guess;

                if (string.IsNullOrWhiteSpace(roomCode) || string.IsNullOrWhiteSpace(word))
                {
                    return await next(ctx);
                }

                if (!int.TryParse(roomCode, out var parsedCode))
                {
                    return await next(ctx);
                }

                var room = _roomRegistry.GetRoom(parsedCode);
                if (room != null && room.WordList != null && room.WordList.Contains(word))
                {
                    return new { error = "Submitted already" };
                }
            }
            return await next(ctx);

        }
    }
}
