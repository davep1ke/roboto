using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Media;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using RobotoChatBot.Modules;

namespace RobotoChatBot
{
    class Plugins
    {

        //module list. Static, as dont want to serialise the plugins, just the data.
        public static List<Modules.RobotoModuleTemplate> plugins = new List<Modules.RobotoModuleTemplate>();

        //TODO - add PluginData & ChatPluginData data here, migrate & add load/save
        /// <summary>
        /// Load all the plugins BEFORE loading the settings file. We need to be able to enumerate the extra types when loading the XML. 
        /// </summary>
        public static void initPluginAssemblies()
        {
            //load all plugins by looking for all objects derived from the abstract class. 
            Assembly currAssembly = Assembly.GetExecutingAssembly();

            foreach (Type type in currAssembly.GetTypes())
            {

                if (type.IsClass && !type.IsAbstract
                    //is this a subclass of the module template
                    && type.IsSubclassOf(typeof(Modules.RobotoModuleTemplate))
                    //is our plugin filter disabled?
                    && (Roboto.pluginFilter.Count == 0
                    //or is this plugin listed?
                    || Roboto.pluginFilter.Contains(type.Name))
                    )
                {

                    Roboto.log.log("Registering plugin " + type.Name, logging.loglevel.low);

                    if (!pluginExists(type))
                    {
                        Modules.RobotoModuleTemplate plugin = (Modules.RobotoModuleTemplate)Activator.CreateInstance(type);
                        Roboto.log.log("Added " + plugin.GetType().ToString(), logging.loglevel.low);
                        Plugins.plugins.Add(plugin);
                        plugin.init();
                    }

                }
            }
            Roboto.log.log("All Plugins initialised", logging.loglevel.high, Colors.White, false, true);
        }


        public static void startupChecks()
        {
            


            Roboto.log.log((Modules.mod_standard.getAllMethodDescriptions()));

            foreach (Modules.RobotoModuleTemplate plugin in plugins)
            {
                plugin.initPluginData();
                //plugin.initData(); //this data probably already exists if loaded by XML, but if not, allow the plugin to create it. 
            }

            logging.longOp lo_modules = new logging.longOp("Module Startup Checks", plugins.Count() * 2);
            foreach (Modules.RobotoModuleTemplate plugin in plugins)
            {
                Roboto.log.log("Startup Checks for " + plugin.ToString(), logging.loglevel.warn);

                //moduledata and chatData startup checks
                //TODO - move chat stuff to chats class
                Roboto.log.log("Checking chatdata for " + plugin.ToString(), logging.loglevel.warn);
                int i = Roboto.Settings.chatData.Count();
                foreach (chat c in Roboto.Settings.chatData)
                {
                    i--;
                    if (i % 100 == 0) { Roboto.log.log(i.ToString() + " remaining", logging.loglevel.verbose); }
                    c.initPlugins();
                    if (plugin.pluginChatDataType != null)
                    {
                        RobotoModuleChatDataTemplate cd = c.getPluginData(plugin.pluginChatDataType);
                        if (cd != null) { cd.startupChecks(); }
                    }
                }
                Roboto.log.log("Checking coredata for " + plugin.ToString(), logging.loglevel.warn);
                plugin.getPluginData().startupChecks();
                lo_modules.addone();
                Roboto.log.log("Checking module for " + plugin.ToString(), logging.loglevel.warn);
                plugin.startupChecks();
                lo_modules.addone();

            }
            lo_modules.complete();


        }

        /// <summary>
        /// General background processing loop. Called 
        /// </summary>
        public static void backgroundProcessing(bool force)
        {
            foreach (Modules.RobotoModuleTemplate plugin in plugins)
            {
                if (plugin.backgroundHook)
                {
                    try
                    {
                        plugin.callBackgroundProcessing(force);
                    }
                    catch (Exception e)
                    {
                        Console.Out.WriteLine("-----------------");
                        Console.Out.WriteLine("Error During Plugin " + plugin.GetType().ToString() + " background processing");
                        Console.Out.WriteLine(e.Message);
                    }
                }
            }
        }



        /// <summary>
        /// Get all the custom types used, for serialising / deserialising data to XML.
        /// </summary>
        /// <returns></returns>
        public static Type[] getPluginDataTypes()
        {
            //put into a list first
            List<Type> customTypes = new List<Type>();
            foreach (Modules.RobotoModuleTemplate plugin in Plugins.plugins)
            {
                if (plugin.pluginDataType != null) { customTypes.Add(plugin.pluginDataType); }
                if (plugin.pluginChatDataType != null) { customTypes.Add(plugin.pluginChatDataType); }
            }

            return customTypes.ToArray();
        }

        public static RobotoModuleTemplate getPlugin(Type type)
        {
            foreach (RobotoModuleTemplate t in plugins)
            {
                if (t.GetType() == type)
                {
                    return t;
                }
            }

            return null;

        }




        public static void registerData(Modules.RobotoModuleDataTemplate data)
        {

            if (typeDataExists(data.GetType()) == false)
            {
                Roboto.Settings.pluginData.Add(data);
                Console.WriteLine("Added data of type " + data.GetType().ToString());
            }
            else
            {
                Console.WriteLine("Plugin data of type " + data.GetType().ToString() + " already exists!");
            }

        }

        /// <summary>
        /// Check if a plugins datastore exists
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public static bool typeDataExists(Type t)
        {
            bool found = false;
            foreach (Modules.RobotoModuleDataTemplate existing in Roboto.Settings.pluginData)
            {
                if (t.GetType() == existing.GetType())
                {

                    found = true;
                }
            }
            return found;
        }

        /// <summary>
        /// check if a plugin Type exists
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public static bool pluginExists(Type t)
        {
            bool found = false;
            foreach (Modules.RobotoModuleTemplate existing in plugins)
            {
                if (t.GetType() == existing.GetType())
                {

                    found = true;
                }
            }
            return found;
        }

        public static T getPluginData<T>()
        {
            foreach (Modules.RobotoModuleDataTemplate existing in Roboto.Settings.pluginData)
            {
                if (existing.GetType() == typeof(T))
                {
                    //Console.WriteLine("Plugin data of type " + data.GetType().ToString() + " already exists!");
                    T retVal = (T)Convert.ChangeType(existing, typeof(T));
                    return retVal;
                }
            }

            Console.WriteLine("Couldnt find plugin data of type " + typeof(T).ToString());
            throw new InvalidDataException("Couldnt find plugin data of type " + typeof(T).ToString());

        }



        public static Modules.RobotoModuleDataTemplate getPluginData(Type pluginDataType)
        {
            foreach (Modules.RobotoModuleDataTemplate existing in Roboto.Settings.pluginData)
            {
                if (existing.GetType() == pluginDataType)
                {
                    return existing;
                }
            }

            Console.WriteLine("Couldnt find plugin data of type " + pluginDataType.ToString());
            throw new InvalidDataException("Couldnt find plugin data of type " + pluginDataType.ToString());
        }
    }
}
