﻿using System;
using System.Windows.Media;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.PeerToPeer.Collaboration;

namespace RobotoChatBot.Modules
{

    public abstract class RobotoModuleTemplate
    {
        public bool chatHook = false;
        public bool chatEvenIfAlreadyMatched = false;
        public bool chatIfMuted = false;
        public int chatPriority = 5;

        public bool backgroundHook = false;
        public int backgroundMins = 10;

        public Type pluginDataType;
        public Type pluginChatDataType;

        //cache the plugin data locally. May need casting in implementations
        internal RobotoModuleDataTemplate localData { get; set; }


        /// <summary>
        /// Initialise any code
        /// </summary>
        public virtual void init() { }
        /*// <summary>
        /// Initialise general data
        /// </summary>
        public virtual void initData() { }*/
        /// <summary>
        /// Return the list of valid commands for the plugin (returned during /operations, and on startup for sending to BotFather
        /// </summary>
        /// <returns></returns>
        public virtual string getMethodDescriptions()
        {
            log("No methods set for " + GetType().ToString() + " plugin. You should override getMethodDescriptions", logging.loglevel.high);
            return "";
        }
        /// <summary>
        /// Return the list of valid commands for the plugin (returned during /operations, and on startup for sending to BotFather
        /// </summary>
        /// <returns></returns>
        public virtual string getWelcomeDescriptions()
        {
            log("No methods set for " + GetType().ToString() + " plugin. You should override getMethodDescriptions", logging.loglevel.high);
            return null;
        }


        /// <summary>
        /// Return some text indicating the current level of use of the plugin
        /// </summary>
        /// <returns></returns>
        public virtual string getStats()
        {
            return "";
        }
        /// <summary>
        /// Perform any startup consistency checks / datafix type operations
        /// </summary>
        public virtual void startupChecks() { }
        /// <summary>
        /// Initialise chat specific data. Chatdata object should already exist. 
        /// </summary>

        public virtual void sampleData() { }
        /// <summary>
        /// Called whenever a chat message is sent, if Settings.RegisterChatHook has been called during init. 
        /// </summary>
        /// <param name="chatID"></param>
        /// <param name="chatString"></param>
        /// <param name="userName"></param>
        /// <returns>Boolean indicating if the message was processed by the selected plugin</returns>
        public virtual bool chatEvent(message m, chat c = null)
        {
            return false;
        }


        /// <summary>
        /// A reply that was expected from a call to getExpectedReply
        /// </summary>
        /// <param name="e"></param>
        /// <param name="m"></param>
        /// <returns>A boolean indicating whether the plugin dealt with the reply or not. </returns>
        public virtual bool replyReceived(ExpectedReply e, message m, bool messageFailed = false)
        {
            log("Plugin " + GetType().ToString() + " received a reply, but doesnt override replyReceived", logging.loglevel.critical);
            return false;
        }
        /// <summary>
        /// Called periodically, if Settings.RegisterBackgroundHook has been called during init
        /// </summary>
        protected virtual void backgroundProcessing() { }

        public void callBackgroundProcessing(bool force)
        {
            DateTime lastCall = getLastUpdate();
            if (force || DateTime.Now > lastCall.AddMinutes(backgroundMins))
            {
                log("Background Processing for " + GetType().ToString());
                backgroundProcessing();
                setLastUpdate(DateTime.Now);
            }
        }



        /// <summary>
        /// Logging wrapper
        /// </summary>
        /// <param name="text"></param>
        /// <param name="level"></param>
        /// <param name="colour"></param>
        /// <param name="noLineBreak"></param>
        public void log(string text, logging.loglevel level = logging.loglevel.normal, Color? colour = null, bool noLineBreak = false)
        {
            Roboto.log.log(text, level, colour, noLineBreak, false, false, false, 2);
        }

        //Helper Methods
        /*public T getPluginData<T>()
        {
            return Plugins.getPluginData<T>();
        }*/
        public RobotoModuleDataTemplate getPluginData()
        {
            return localData;
        }




        public DateTime getLastUpdate()
        {
            RobotoModuleDataTemplate data = getPluginData();
            if (data == null)
            {
                Roboto.log.log("Error - background processing requires a LocalData object to be defined!", logging.loglevel.critical);
                return DateTime.MinValue;
            }
            return data.lastBackgroundUpdate;

        }

        public void setLastUpdate(DateTime update)
        {
            RobotoModuleDataTemplate data = getPluginData();
            data.lastBackgroundUpdate = update;

        }

        /// <summary>
        /// Create a pluginData object and stash locally & in the Plugins / Settings module so it can be saved.  
        /// </summary>
        internal void initPluginData()
        {
            if (this.localData != null) { return; } //already valid. 
            else
            {
                //see if it exists and we can assign it
                RobotoModuleDataTemplate data = Plugins.getPluginData(pluginDataType);
                if (data != null)
                {
                    localData = data;
                }
                else
                {

                    //otherwise create a brand new set
                    localData = (Modules.RobotoModuleDataTemplate)Activator.CreateInstance(pluginDataType);
                    Plugins.registerData(localData);
                    Roboto.log.log("Created & Stored Plugin Data " + localData.GetType().ToString() + " for module " + this.GetType().ToString(), logging.loglevel.low);
                }
            }
        }


        public RobotoModuleChatDataTemplate initChatPluginData(chat chat)
        {

            if (pluginChatDataType != null)
            {
                return (Modules.RobotoModuleChatDataTemplate)Activator.CreateInstance(pluginChatDataType);
            }
            else
            {
                return null;
            }
        }
    }
}
