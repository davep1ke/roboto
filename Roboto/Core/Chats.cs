using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using RobotoChatBot.Modules;


namespace RobotoChatBot
{
    public static class Chats
    {
        //TODO - migrate Chats here from Settings, add load/save

        /// <summary>
        /// Check for dormant chats & plugins to purge
        /// </summary>
        public static void removeDormantChats()
        {
            // Snapshot-then-release, not held for the whole pass: tryPurgeData()/logStat() below
            // don't touch the top-level chatData list itself, only each chat's own data - so the
            // lock only needs to cover the two things that actually do (the initial snapshot, and
            // each Remove) rather than this whole potentially-long dormant-chat sweep, which would
            // otherwise block live message dispatch for its entire duration.
            //
            // Also picks up any chat TelegramAPI.EnsureNotAdminInAnyChat has confirmed is gone
            // (chat not found / bot kicked), regardless of lastupdate age - found live (2026-08-24,
            // see MIGRATION.md): a confirmed-unreachable chat still had to wait out the full
            // purgeInactiveChatsAfterXDays window otherwise, getting needlessly re-hit by every
            // background check in the meantime. Still goes through the same tryPurgeData() veto
            // below as any other dormant candidate - this only removes the age floor for a chat
            // already proven unreachable, it doesn't force a purge past a plugin's own objection.
            List<chat> dormant;
            using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey))
            {
                dormant = Roboto.Settings.chatData.Where(x =>
                    x.lastupdate < DateTime.Now.Subtract(new TimeSpan(Roboto.Settings.purgeInactiveChatsAfterXDays, 0, 0, 0))
                    || x.getPluginData<mod_standard_chatdata>()?.confirmedGone == true
                ).ToList();
            }

            Stopwatch sw = Stopwatch.StartNew();

            Roboto.log.log("Checking for Purgable chats / chat data", logging.loglevel.high, false, true);
            foreach (chat c in dormant)
            {
                //check all plugins and remove data if no longer reqd - locked per-chat, same
                //chokepoint live message dispatch for this chat locks against.
                using (ChatKeyedLock.Acquire(c.chatID))
                {
                    bool isPurgable = c.tryPurgeData();

                    //if all plugins are purged, delete the chat
                    if (isPurgable)
                    {
                        Roboto.log.log("Purging all data for chat " + c.chatID);
                        Roboto.Settings.stats.logStat(new statItem("Chats Purged", typeof(Roboto)));
                        using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey))
                        {
                            Roboto.Settings.chatData.Remove(c);
                        }
                    }
                    else
                    {
                        Roboto.log.log("Skipping purge of chat " + c.chatID + " as one or more plugins reported they shouldn't be purged");
                    }
                }
            }

            sw.Stop();
            Roboto.Settings.stats.logStat(new statItem("Dormant Chat Check Duration (ms)", typeof(Chats), (int)sw.ElapsedMilliseconds));
            Roboto.Settings.stats.logStat(new statItem("Dormant Chats Checked", typeof(Chats), dormant.Count));
        }



        /// <summary>
        /// find a chat by its chat ID
        /// </summary>
        /// <param name="chat_id"></param>
        /// <returns></returns>
        public static chat getChat(long chat_id)
        {
            using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey))
            {
                foreach (chat c in Roboto.Settings.chatData)
                {
                    if (c.chatID == chat_id)
                    {
                        return c;
                    }
                }
                return null;
            }
        }

        /// <summary>
        /// Add data about a chat to the store.
        /// </summary>
        /// <param name="chat_id"></param>
        public static chat addChat(long chat_id, string chatTitle)
        {
            using (ChatKeyedLock.Acquire(ChatKeyedLock.GlobalListsKey))
            {
                if (getChat(chat_id) == null)
                {
                    Console.WriteLine("Creating data for chat " + chat_id.ToString());
                    chat chatObj = new chat(chat_id, chatTitle);
                    Roboto.Settings.chatData.Add(chatObj);
                    return chatObj;
                }
                else
                {
                    throw new InvalidDataException("Chat already exists!");
                }
            }
        }

    }
}

