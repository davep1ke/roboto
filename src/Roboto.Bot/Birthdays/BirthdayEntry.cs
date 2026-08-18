namespace Roboto.Bot.Birthdays;

public sealed class BirthdayEntry
{
    public string Name { get; set; } = "";

    /// <summary>Only the day/month matter for the yearly reminder - the year is kept (matches
    /// legacy) purely because DateTime.TryParse needs one, and it's harmless to display.</summary>
    public DateTime Birthday { get; set; }
}
