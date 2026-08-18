using System.Xml.Serialization;
using Roboto.Migrator.Legacy;

namespace Roboto.Migrator.Tests;

/// <summary>
/// Builds a synthetic legacy export programmatically (constructs LegacySettings, then serializes
/// it with XmlSerializer - the same mechanism XmlImporter deserializes with) rather than hand-typing
/// raw XML text, which would risk silently-wrong tag names/xsi:type values going unnoticed. Written
/// to a real temp file since XmlImporter's own contract is "give me a path", not an in-memory
/// stream - this exercises the actual file-copy-then-deserialize path, not an approximation of it.
///
/// One deliberately comprehensive fixture (not several small ones) covering: durable data across
/// every module, a multi-answer ("Pick 2") question with a partial submission already in progress,
/// a Judging-status game, an unmappable card GUID reference, a resumable ("Question"/"Judging") and
/// an unresumable ("kick") pending reply - see MigrationScenarioTests for what each proves.
/// </summary>
public static class SyntheticXmlFixture
{
    public const string TelegramApiKey = "should-never-be-written-anywhere";
    public const string SteamApiKey = "s3cr3t-steam-key";
    public const long DurableChatId = 100;
    public const long QuestionChatId = 200;
    public const long JudgingChatId = 300;
    public const long QuestionPlayerId = 201;
    public const long QuestionJudgeId = 202;
    public const long JudgingJudgeId = 301;
    public const long JudgingAnswererId = 302;
    public const long JudgingKickTargetId = 303;
    public const long OrphanedReplyChatId = 999999; // deliberately not present in chatData

    public static string Write()
    {
        var settings = new LegacySettings
        {
            telegramAPIKey = TelegramApiKey,
            botUserName = "SyntheticTestBot",
            pluginData =
            [
                new LegacyWordcraftData { words = ["Foo", "Bar"] },
                new LegacySteamCoreData
                {
                    steamAPIKey = SteamApiKey,
                    games =
                    [
                        new LegacySteamGame
                        {
                            gameID = "440",
                            displayName = "TF2",
                            chievs = [new LegacySteamAchievement { achievement_code = "ACH1", displayName = "First Blood", description = "Get a kill." }],
                        },
                    ],
                },
                new LegacyXyzzyCoreData
                {
                    questions =
                    [
                        new LegacyXyzzyCard { uniqueID = "Q-GUID-1", text = "Pick 2: name two things", nrAnswers = 2 },
                        new LegacyXyzzyCard { uniqueID = "Q-GUID-2", text = "Single question?", nrAnswers = 1 },
                    ],
                    answers =
                    [
                        new LegacyXyzzyCard { uniqueID = "A-GUID-1", text = "Answer One" },
                        new LegacyXyzzyCard { uniqueID = "A-GUID-2", text = "Answer Two" },
                        new LegacyXyzzyCard { uniqueID = "A-GUID-3", text = "Answer Three" },
                    ],
                },
            ],
            chatData =
            [
                new LegacyChat
                {
                    chatID = DurableChatId,
                    chatTitle = "Durable Chat",
                    chatAdmins = [111],
                    chatData =
                    [
                        new LegacyQuoteChatData
                        {
                            autoQuoteEnabled = true,
                            autoQuoteHours = 24,
                            multiquotes = [new LegacyMultiQuote { on = new DateTime(2021, 1, 1), lines = [new LegacyQuoteLine { by = "Bob", text = "Hi" }] }],
                        },
                        new LegacyBirthdayChatData
                        {
                            birthdays = [new LegacyBirthday { name = "Alice", birthday = new DateTime(1990, 1, 1) }],
                        },
                        new LegacySteamChatData
                        {
                            players =
                            [
                                new LegacySteamPlayer
                                {
                                    playerID = "76561", playerName = "Gamer",
                                    chievs = [new LegacySteamChiev { chievName = "ACH1", appID = "440" }],
                                },
                            ],
                        },
                        new LegacyStandardChatData
                        {
                            x_quietHoursStartTime = new TimeSpan(22, 0, 0).Ticks,
                            x_quietHoursEndTime = new TimeSpan(8, 0, 0).Ticks,
                        },
                    ],
                },
                new LegacyChat
                {
                    chatID = QuestionChatId,
                    chatTitle = "Question Chat",
                    chatData =
                    [
                        new LegacyXyzzyChatData
                        {
                            players =
                            [
                                new LegacyXyzzyPlayer
                                {
                                    name = "Player One", playerID = QuestionPlayerId, wins = 2,
                                    // MISSING-GUID has no catalog entry - proves unmappable references drop cleanly.
                                    cardsInHand = ["A-GUID-1", "A-GUID-2", "MISSING-GUID"],
                                    selectedCards = ["A-GUID-1"], // 1 of the question's 2 required cards already picked
                                },
                                new LegacyXyzzyPlayer { name = "Judge Two", playerID = QuestionJudgeId, wins = 0 },
                            ],
                            lastPlayerAsked = 1, // Judge Two is the judge
                            status = "Question",
                            currentQuestion = "Q-GUID-1",
                            remainingQuestions = ["Q-GUID-2"],
                            remainingAnswers = ["A-GUID-3"],
                            maxWaitTimeHours = 12,
                            enteredQuestionCount = -1,
                        },
                    ],
                },
                new LegacyChat
                {
                    chatID = JudgingChatId,
                    chatTitle = "Judging Chat",
                    chatData =
                    [
                        new LegacyXyzzyChatData
                        {
                            players =
                            [
                                new LegacyXyzzyPlayer { name = "Judge", playerID = JudgingJudgeId, wins = 0 },
                                new LegacyXyzzyPlayer { name = "Answerer", playerID = JudgingAnswererId, wins = 1, selectedCards = ["A-GUID-1"] },
                                new LegacyXyzzyPlayer { name = "KickTarget", playerID = JudgingKickTargetId, wins = 0, selectedCards = ["A-GUID-2"] },
                            ],
                            lastPlayerAsked = 0, // Judge is the judge
                            status = "Judging",
                            currentQuestion = "Q-GUID-2",
                            maxWaitTimeHours = 12,
                            enteredQuestionCount = -1,
                        },
                    ],
                },
            ],
            expectedReplies =
            [
                new LegacyExpectedReply
                {
                    chatID = QuestionChatId, userID = QuestionPlayerId,
                    pluginType = "RobotoChatBot.Modules.mod_xyzzy", messageData = "Question",
                },
                new LegacyExpectedReply
                {
                    chatID = JudgingChatId, userID = JudgingJudgeId,
                    pluginType = "RobotoChatBot.Modules.mod_xyzzy", messageData = "Judging",
                },
                new LegacyExpectedReply
                {
                    // Admin sub-flow with no equivalent in the rewrite - proves the drop-by-reason path.
                    chatID = JudgingChatId, userID = JudgingKickTargetId,
                    pluginType = "RobotoChatBot.Modules.mod_xyzzy", messageData = "kick",
                },
                new LegacyExpectedReply
                {
                    // References a chat that doesn't exist anywhere in chatData - proves orphaned
                    // replies (a purged chat's stale leftover) are counted, not silently skipped by
                    // the chat-driven iteration that would otherwise never visit them.
                    chatID = OrphanedReplyChatId, userID = 999,
                    pluginType = "RobotoChatBot.Modules.mod_xyzzy", messageData = "Question",
                },
            ],
        };

        var extraTypes = new[]
        {
            typeof(LegacyQuoteChatData), typeof(LegacyQuoteCoreData),
            typeof(LegacyBirthdayChatData), typeof(LegacyBirthdayCoreData),
            typeof(LegacyWordcraftData),
            typeof(LegacySteamChatData), typeof(LegacySteamCoreData),
            typeof(LegacyStandardChatData), typeof(LegacyStandardData),
            typeof(LegacyXyzzyChatData), typeof(LegacyXyzzyCoreData),
        };

        var path = Path.Combine(Path.GetTempPath(), $"roboto-synthetic-{Guid.NewGuid():N}.xml");
        var serializer = new XmlSerializer(typeof(LegacySettings), extraTypes);
        using (var stream = File.Create(path))
        {
            serializer.Serialize(stream, settings);
        }

        return path;
    }
}
