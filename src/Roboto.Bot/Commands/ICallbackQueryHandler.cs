using Telegram.Bot;
using Telegram.Bot.Types;

namespace Roboto.Bot.Commands;

/// <summary>
/// A handler for inline-keyboard button taps (CallbackQuery updates). Mirrors IBotCommand's
/// reflection-discovery pattern - implementations are found automatically by AddRobotoBot(), no
/// manual registration. Generic, not xyzzy-specific: any future module that wants tappable buttons
/// can add one of these rather than mod_xyzzy owning the whole callback-query pipeline.
/// </summary>
public interface ICallbackQueryHandler
{
    /// <summary>Whether this handler owns the given callback_data (typically a fixed prefix).</summary>
    bool CanHandle(string callbackData);

    /// <summary>
    /// Handles the tap and returns the short toast text to show the user (Telegram's
    /// answerCallbackQuery popup) - CallbackQueryRouter sends it, not this method, so every tap
    /// gets answered exactly once even if a handler throws or no handler matches (see
    /// CallbackQueryRouter's doc comment for why that matters).
    /// </summary>
    Task<string?> HandleAsync(ITelegramBotClient bot, CallbackQuery query, CancellationToken cancellationToken);
}
