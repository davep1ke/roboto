using Roboto.Bot.Persistence;

namespace Roboto.Bot.Chats;

public sealed class ChatRepository(IStateStore store)
{
    public async Task<ChatState> GetAsync(long chatId, CancellationToken cancellationToken)
    {
        return await store.LoadAsync<ChatState>(Key(chatId), cancellationToken)
               ?? new ChatState { ChatId = chatId };
    }

    public Task SaveAsync(ChatState chat, CancellationToken cancellationToken) =>
        store.SaveAsync(Key(chat.ChatId), chat, cancellationToken);

    private static string Key(long chatId) => $"chat:{chatId}";
}
