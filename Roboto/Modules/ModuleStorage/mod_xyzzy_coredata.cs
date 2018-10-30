using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using RobotoChatBot.Helpers;

namespace RobotoChatBot.Modules
{
    /// <summary>
    /// General Data to be stored in the plugin XML store.
    /// </summary>
    [XmlType("mod_xyzzy_coredata")]
    [Serializable]
    public class mod_xyzzy_coredata : RobotoModuleDataTemplate
    {
        public DateTime lastDayProcessed = DateTime.MinValue;
        public int maxPacksToSyncInOneGo = 5;
        public int backgroundChatsToProcess = 5;
        public int backgroundChatsToMiniProcess = 100;
        public List<mod_xyzzy_card> questions = new List<mod_xyzzy_card>();
        public List<mod_xyzzy_card> answers = new List<mod_xyzzy_card>();
        public List<Helpers.cardcast_pack> packs = new List<Helpers.cardcast_pack>();

        public mod_xyzzy_card getQuestionCard(string cardUID)
        {
            foreach (mod_xyzzy_card c in questions)
            {
                if (c.uniqueID == cardUID) { return c; }
            }
            return null;
        }

        public mod_xyzzy_card getAnswerCard(string cardUID)
        {
            foreach (mod_xyzzy_card c in answers)
            {
                if (c.uniqueID == cardUID)
                {
                    return c;
                }
            }
            return null;
        }

        public List<Helpers.cardcast_pack> getPackFilterList()
        {
            return packs;
        }

        public Helpers.cardcast_pack getPack (string packTitle)
        {
            List<Helpers.cardcast_pack> matches = getPacks(packTitle);
            if (matches.Count > 0) { return matches[0]; }
            return null;
        }

        public Helpers.cardcast_pack getPack(Guid packID)
        {
            List<Helpers.cardcast_pack> matches = getPacks(packID);
            if (matches.Count > 0) { return matches[0]; }
            return null;
        }

        public List<Helpers.cardcast_pack> getPacks(string packTitle)
        {
            return packs.Where(x => x.name == packTitle).ToList();
        }

        public List<Helpers.cardcast_pack> getPacks(Guid packID)
        {
            return packs.Where(x => x.packID == packID).ToList();
        }

        public override void startupChecks()
        {
            //start a logging longop
            logging.longOp lo_startup = new logging.longOp("XYZZY - Coredata Startup", 10);

            //DATAFIX: allocate any cards without a pack guid the correct guid
            int success = 0;
            int fail = 0;

            //Disable warnings for use of deprecated category field - this is a datafix to ensure it is properly wiped. 
            #pragma warning disable 612, 618
            foreach (mod_xyzzy_card q in questions.Where(x => x.packID == Guid.Empty) )
            {
                cardcast_pack pack = getPack(q.category);
                if (pack != null) { q.packID = pack.packID; success++; }
                //log - will be checked every startup
                else { log("Datafix failed - couldnt find pack for card " + q.text + " from pack " + q.category, logging.loglevel.high); fail++; }
            }
            foreach (mod_xyzzy_card a in answers.Where(x => x.packID == Guid.Empty))
            {
                cardcast_pack pack = getPack(a.category);
                if (pack != null) { a.packID = pack.packID; success++; }
                //log - will be checked every startup
                else { log("Datafix failed - couldnt find pack for card " + a.text + " from pack " + a.category, logging.loglevel.high); fail++; }
            }
            if (success + fail > 0)
            {
                log("DATAFIX: " + success + " cards have had packIDs populated, " + fail + " couldn't find pack");
            }
            lo_startup.addone();

            //now remove category from all cards that have a guid. 
            success = 0;
            
            foreach (mod_xyzzy_card q in questions.Where(x => x.packID != null)){ q.category = null; q.TempCategory = null; success++; }
            foreach (mod_xyzzy_card a in answers.Where(x => x.packID != null)) { a.category = null; a.TempCategory = null; success++; }
            #pragma warning restore 612, 618

            if (success + fail > 0)
            {
                log("DATAFIX: Wiped category from " + success + " cards successfully.", logging.loglevel.warn);
            }
            lo_startup.addone();

            //lets see if packs with null Cardcast Pack codes can be populated by looking through our other packs
            foreach (cardcast_pack p in packs.Where(x => string.IsNullOrEmpty(x.packCode)))
            {
                List<cardcast_pack> matchingPacks = getPacks(p.name).Where(x => x.packID != p.packID).ToList();
                if (matchingPacks.Count >0 )
                {
                    log("DATAFIX: Orphaned pack " + p.name + " has been matched against an existing pack, and packcode set to " + p.packCode, logging.loglevel.warn);
                    p.packCode = matchingPacks[0].packCode;
                }
            }
            lo_startup.addone();

            //now find any packs where the pack ID exists more than once. Start by getting unique list of packs
            List<string> uniqueCodes = new List<string>();
            foreach (cardcast_pack p in packs) { if (!uniqueCodes.Contains(p.packCode) && p.packCode != "") { uniqueCodes.Add(p.packCode); } }
            log("Found " + uniqueCodes.Count() + " unique pack codes against " + packs.Count() + " packs");

            //loop through. 
            foreach (string packCode in uniqueCodes)
            {
                //Find the master pack
                List<cardcast_pack> matchingPacks = packs.Where(x => (! string.IsNullOrEmpty(x.packCode)) && (  x.packCode == packCode)).ToList();


                if (matchingPacks.Count() == 0)
                {
                    log("Couldnt find any packs matching '" + packCode + "' for some reason!", logging.loglevel.critical);
                }
                else if (matchingPacks.Count() == 1)
                {
                    log("One valid pack for " + packCode, logging.loglevel.verbose);
                }
                else
                {
                    
                    int cardsUpdated = 0;
                    int packsMerged = 0;
                    cardcast_pack masterPack = matchingPacks[0];

                    log("Found " + matchingPacks.Count() + " packs for " + packCode + " - merging into " + masterPack.ToString(), logging.loglevel.critical);

                    //need to merge the other packs into the first one.
                    foreach (cardcast_pack p in matchingPacks.Where(x => x != masterPack))
                    {
                        log("Merging pack " + p.name + "(" + p.packID + ") into " + masterPack.name + "(" + masterPack.packID + ")", logging.loglevel.high);
                        //overwrite the guids on the child cards. Note that we will now be left with a huge amount of duplicate cards - should be sorted by the pack sync.
                        foreach (mod_xyzzy_card c in answers.Where(y => y.packID == p.packID )) { c.packID = masterPack.packID; cardsUpdated++; }
                        foreach (mod_xyzzy_card c in questions.Where(y => y.packID == p.packID)) { c.packID = masterPack.packID; cardsUpdated++; }
                        
                        //update any pack filters
                        foreach (chat c in  Roboto.Settings.chatData)
                        {
                            mod_xyzzy_chatdata cd = c.getPluginData<mod_xyzzy_chatdata>();
                            if (cd != null)
                            {
                                //remove old packs from filter
                                int recsUpdated = cd.setPackFilter(p.packID, mod_xyzzy_chatdata.packAction.remove);
                                //add new if we removed
                                if (recsUpdated > 0)
                                {
                                    log("Removed pack " + p.name + "(" + p.packID + ") from chat " + c.ToString() + " filter");
                                    recsUpdated = cd.setPackFilter(masterPack.packID, mod_xyzzy_chatdata.packAction.add);
                                    log("Added master pack " + p.name + "(" + p.packID + ") - " + recsUpdated + "records updated");
                                }
                            }
                        }

                        //remove the child packs from the main list
                        packs.Remove(p);

                        packsMerged++;
                    }
                    masterPack.nextSync = DateTime.MinValue;  //flag these for an immediateish sync.
                    log("Finished merging " + packsMerged + " into master pack " + masterPack.name + ". " + cardsUpdated + " cards moved to master pack", logging.loglevel.high);
                    //1=1
                }
                
            }
            lo_startup.addone();

            //find any cards that dont match a pack and remove
            log("Removing orphaned answers", logging.loglevel.warn);
            int i = 0;
            int removed = 0;
            logging.longOp lo_answers = new logging.longOp("Remove Orphan Answers", answers.Count()/100, lo_startup);

            while ( i < answers.Count())
            {

                if (i%100 == 0) { log("Remaining " + (answers.Count() - i) + ". Removed " + removed, logging.loglevel.high); lo_answers.addone(); }
                if (getPacks(answers[i].packID).Count() == 0)
                {
                    //log("Removing " + answers[i].text, logging.loglevel.verbose);
                    removed++;
                    answers.RemoveAt(i);
                    //answers.Remove(answers[i]);
                }
                else { i++; }
            }
            log("Removed " + removed + " orphaned answers", logging.loglevel.warn);
            lo_answers.complete();
            lo_startup.addone();

            //find any cards that dont match a pack and remove
            log("Removing orphaned questions", logging.loglevel.high);
            i = 0;
            removed = 0;
            logging.longOp lo_questions = new logging.longOp("Remove Orphan Questions", questions.Count() / 100, lo_startup);

            while (i < questions.Count())
            {
                if (i % 100 == 0) { log("Remaining " + (questions.Count() - i) + ". Removed " + removed, logging.loglevel.high);lo_questions.addone(); }
                if (getPacks(questions[i].packID).Count() == 0)
                {
                    //log("Removing " + questions[i].text, logging.loglevel.verbose);
                    removed++;
                    questions.RemoveAt(i);
                    //questions.Remove(questions[i]);
                }
                else { i++; }
            }
            log("Removed " + removed + " orphaned questions", logging.loglevel.high);
            lo_startup.addone();
            lo_questions.complete();

            //Dump the packlist and stats to the log window in verbose mode. Flag anything removable
            logging.longOp lo_dump = new logging.longOp("Dump packlist", packs.Count());
            List<cardcast_pack> removablePacks = new List<cardcast_pack>();
            log("Packs Loaded:", logging.loglevel.verbose);
            log("Code \tlastPickedDate\t\tPicks\tAllFlt\tActFlt\tHotFlt\tQCards\tACards\tName", logging.loglevel.verbose);
            foreach (cardcast_pack p in packs.OrderBy(x => x.lastPickedDate))
            {
                //find out how many packs added to#
                int packFiltersAddedTo = 0;
                int activePackFiltersAddedTo = 0;
                int hotPackFiltersAddedTo = 0;
                foreach (chat c in Roboto.Settings.chatData)
                {
                    mod_xyzzy_chatdata cd = c.getPluginData<mod_xyzzy_chatdata>();
                    if (cd != null && cd.packFilterIDs.Contains(p.packID))     {   packFiltersAddedTo++;       }
                    if (cd != null && cd.packFilterIDs.Contains(p.packID) && cd.status != xyzzy_Statuses.Stopped) { activePackFiltersAddedTo++; }
                    if (cd != null && cd.packFilterIDs.Contains(p.packID) && cd.status != xyzzy_Statuses.Stopped && cd.statusChangedTime > DateTime.Now.Subtract(TimeSpan.FromDays(30) )) { hotPackFiltersAddedTo++; }


                }

                log(p.packCode + "\t" + p.lastPickedDate + "\t" + p.totalPicks + "\t" + packFiltersAddedTo + "\t" + activePackFiltersAddedTo + "\t" + hotPackFiltersAddedTo + "\t" + questions.Where(x => x.packID == p.packID).Count() + "\t" + answers.Where(x => x.packID == p.packID).Count() + "\t" +  p.name, logging.loglevel.verbose);

                if (activePackFiltersAddedTo == 0
                    && p.packID != mod_xyzzy.primaryPackID
                    && p.lastPickedDate < (DateTime.Now.Subtract(TimeSpan.FromDays(30)))
                    && packs.Count - removablePacks.Count > 50)
                { 
                    log(p.packCode + " is potentially removable", logging.loglevel.verbose);
                    removablePacks.Add(p);
                }
                lo_dump.addone();
            }
            lo_dump.complete();
            lo_startup.addone();

            //Remove the packs
            log(removablePacks.Count().ToString() + " removable packs found", logging.loglevel.high);
            i = 0;

            //TODO - make this a variable, and move this all to a background job. 
            logging.longOp lo_remove = new logging.longOp("Remove dead packs", 5, lo_startup);
            while ( i < 5 && removablePacks.Count() > 0 )
            {
                i++;
                log("Removing pack " + i + " - " + removablePacks[0].name + ", there are up to " + removablePacks.Count() + " remaining", logging.loglevel.high);
                removePack(removablePacks[0]);
                removablePacks.RemoveAt(0);
                lo_remove.addone();
            }
            lo_remove.complete();
            lo_startup.complete();

        }

        private void removePack(cardcast_pack p)
        {
            log("Removing pack " + p.name + " - " + p.packID + " - " + p.packCode, logging.loglevel.high);
            logging.longOp lo_remove = new logging.longOp("Remove " + p.name, questions.Where(x => x.packID == p.packID).Count() + answers.Where(x => x.packID == p.packID).Count());
            int q = 0; int a = 0; int cn = 0;
            //remove from all filters
            foreach (chat c in Roboto.Settings.chatData)
            {
                mod_xyzzy_chatdata cd = c.getPluginData<mod_xyzzy_chatdata>();
                cn += cd.setPackFilter(p.packID, mod_xyzzy_chatdata.packAction.remove);
            }
            //remove all qcards
            List<mod_xyzzy_card> qcards = questions.Where(x => x.packID == p.packID).ToList(); 
            foreach (mod_xyzzy_card c in qcards)
            {
                q++;
                lo_remove.addone();
                removeQCard(c, null);
            }

            //remove all acards
            List<mod_xyzzy_card> acards = answers.Where(x => x.packID == p.packID).ToList();
            foreach (mod_xyzzy_card c in acards)
            {
                lo_remove.addone();
                a++;
                removeACard(c, null);
            }

            //remove pack
            packs.Remove(p);
            log("Removed pack " + p.packCode + " with " + cn + " filters, " + q + " questions and " + a + " answers", logging.loglevel.high);
            lo_remove.complete();
        }
        
        /// <summary>
        /// check for any packs that can be (and need to be) synced
        /// </summary>
        public void packSyncCheck()
        {
            
            int backlog = packs.Where(p => (p.packCode != null && p.packCode != "" && p.nextSync < DateTime.Now)).Count();
            log("There are " + backlog + " packs outstanding to sync. Picking first " + maxPacksToSyncInOneGo, logging.loglevel.normal );

            logging.longOp lo_sync = new logging.longOp("XYZZY Background Pack Sync", maxPacksToSyncInOneGo);

            Roboto.Settings.stats.logStat(new statItem("Background Wait (Pack Sync)", typeof(mod_xyzzy), backlog));

            //take a subset of packs for now. 
            //Packs is already a list, but there is a chance that the importCardCast will update / remove it - so re-list it to prevent mutation errors
            foreach (Helpers.cardcast_pack p in packs.Where (p => ( p.packCode  != null && p.packCode != "" 
            && p.nextSync < DateTime.Now
            )).OrderBy(x => x.nextSync).Take(maxPacksToSyncInOneGo).ToList())
            {
                log("Syncing " + p.name + " - Sync target was " + p.nextSync.ToString() );
                Roboto.Settings.stats.logStat(new statItem("Packs Synced", typeof(mod_xyzzy)));
                Helpers.cardcast_pack outpack;
                string response;
                bool success = importCardCastPack(p.packCode, out outpack, out response);
                log("Pack sync complete - returned " + response, logging.loglevel.warn);
                lo_sync.addone();
                if (!success) { p.syncFailed(); }
                else { p.syncSuccess(); }
            }
            lo_sync.complete();

            foreach (Helpers.cardcast_pack p in packs.Where(x => x.failCount > 5).ToList())
            {
                //TODO - lets remove the pack!

    
            }




        }

        
        /// <summary>
        /// Import / Sync a cardcast pack into the xyzzy localdata
        /// </summary>
        /// <param name="packCode"></param>
        /// <param name="pack"></param>
        /// <param name="response"> String containing details of the pack and cards added.String will be empty if import failed.</param>
        /// <returns>success/fil</returns>
        public bool importCardCastPack(string packCode, out Helpers.cardcast_pack pack, out string response)
        {
            response = "";
            pack = new Helpers.cardcast_pack();
            bool success = false;
            int nr_qs = 0;
            int nr_as = 0;
            int nr_rep = 0;
            List<Helpers.cardcast_question_card> import_questions = new List<Helpers.cardcast_question_card>();
            List<Helpers.cardcast_answer_card> import_answers = new List<Helpers.cardcast_answer_card>();
            List<mod_xyzzy_chatdata> brokenChats = new List<mod_xyzzy_chatdata>();

            logging.longOp lo_sync = new logging.longOp("XYZZY - Packsync", 5);
            
            try
            {
                log("Attempting to sync/import " + packCode);
                //Call the cardcast API. We should get an array of cards back (but in the wrong format)
                //note that this directly updates the pack object we are going to return - so need to shuffle around later if we sync a pack
                success = Helpers.cardCast.getPackCards(ref packCode, out pack, ref import_questions, ref import_answers);

                lo_sync.addone();

                if (!success)
                {
                    response = "Failed to import pack from cardcast. Check that the code is valid";
                }
                else
                {
                    //lets just check if the pack already exists? 
                    log("Retrieved " + import_questions.Count() + " questions and " + import_answers.Count() + " answers from Cardcast");
                    Guid l_packID = pack.packID;
                    List<cardcast_pack> matchingPacks = getPackFilterList().Where(x => x.packCode == packCode).ToList();

                    

                    if (matchingPacks.Count > 1)  // .Contains(pack.name))
                    {
                        log("Multiple packs found for " + l_packID + " - aborting!", logging.loglevel.critical);
                        response += "/n/r" + "Aborting sync!";

                    }
                    else if (matchingPacks.Count == 1)
                    {
                        cardcast_pack updatePack = matchingPacks[0];

                        //sync the pack.
                        response = "Pack " + pack.name + " (" + packCode + ") exists, syncing cards";
                        log("Pack " + pack.name + "(" + packCode + ") exists, syncing cards", logging.loglevel.normal);

                        //===================
                        //QUESTIONS
                        //===================

                        //NB: Used to do a first pass to remove any matching cards from the import list. This WONT work as it will remove the ability
                        //to find out how many copies of a card we should have! Instead, take a backup copy, remove items from that, and then delete 
                        //anything that is remaining at the end. 
                        List<mod_xyzzy_card> questionCache = questions.Where(x => (x.packID == updatePack.packID)).ToList();
                        log("=============================", logging.loglevel.high);
                        log("Procesing " + questionCache.Count() + " QUESTION cards", logging.loglevel.high);
                        log("=============================", logging.loglevel.high);
                        logging.longOp lo_q = new logging.longOp("Questions", questionCache.Count());

                        //Loop through everything that is in the import list, removing items as we go. 
                        while (import_questions.Count() > 0)
                        {
                            lo_q.updateLongOp(questionCache.Count()); //go backwards. This is just the remaining nr cards. 
                            cardcast_question_card currentCard = import_questions[0];
                            //find how many other matches in the import list we have. 
                            List<cardcast_question_card> matchingImportCards = import_questions.Where(x => Helpers.common.cleanseText(x.question) == Helpers.common.cleanseText(currentCard.question)).ToList();
                            if (matchingImportCards.Count() == 0)
                            {
                                log("Error! No matches found for card '" + currentCard.question + "' - expect at least 1", logging.loglevel.critical);
                            }
                            else
                            {
                                log("Processing " + matchingImportCards.Count() + " cards matching " + currentCard.question, logging.loglevel.verbose);
                                List<mod_xyzzy_card> matchingLocalCards = questions.Where(x => (x.packID == updatePack.packID) && (Helpers.common.cleanseText(x.text) == Helpers.common.cleanseText(currentCard.question))).ToList();
                                log("Found " + matchingLocalCards.Count() + " local cards", logging.loglevel.verbose);

                                //assume the first cards should match the cards coming from CardCast. Update so we have exact text
                                int j = 0;
                                while (j < matchingLocalCards.Count() && j < matchingImportCards.Count())
                                {
                                    if (matchingLocalCards[j].text != matchingImportCards[j].question || matchingLocalCards[j].nrAnswers != matchingImportCards[j].nrAnswers)
                                    {
                                        try
                                        {
                                            log("Local question card updated from " + matchingLocalCards[j].text + "(" + matchingLocalCards[j].nrAnswers  +  ") to " + matchingImportCards[j].question + " (" + matchingImportCards[j].nrAnswers + ")", logging.loglevel.high);
                                            matchingLocalCards[j].text = matchingImportCards[j].question;
                                            matchingLocalCards[j].nrAnswers = matchingImportCards[j].nrAnswers;
                                            nr_rep++;
                                        }
                                        catch (Exception e)
                                        {
                                            log("Error updating question text on qcard - " + e.Message, logging.loglevel.critical);
                                        }
                                    }
                                    j++;
                                }

                                //for the remainder, decide how to progress:
                                if (matchingLocalCards.Count() == matchingImportCards.Count())
                                {
                                    //log("Count matches, nothing more to do!", logging.loglevel.verbose);
                                }
                                //Need to add some new cards
                                else if (matchingLocalCards.Count() < matchingImportCards.Count())
                                {
                                    log("Not enough local cards, adding " + (matchingLocalCards.Count() - matchingImportCards.Count()) + " extra", logging.loglevel.verbose);
                                    for (int i = matchingLocalCards.Count(); i < matchingImportCards.Count(); i++)
                                    {
                                        log("Adding card " + i + " " + matchingImportCards[0].question, logging.loglevel.verbose);
                                        mod_xyzzy_card x_question = new mod_xyzzy_card(matchingImportCards[0].question, pack.packID, matchingImportCards[0].nrAnswers);
                                        questions.Add(x_question);
                                    }
                                }
                                //need to remove some existing cards. Retire cards by merging data into the first card
                                else
                                {
                                    for (int i = matchingImportCards.Count(); i < matchingLocalCards.Count(); i++)
                                    {
                                        //merge the card from the master list, and flag any chats as broken
                                        List<mod_xyzzy_chatdata> newBrokenChats = removeQCard(matchingLocalCards[i],  matchingLocalCards[0].uniqueID);
                                        if (newBrokenChats.Count() > 0 )
                                        {
                                            log("Marking " + newBrokenChats.Count() + " chats as requiring checking", logging.loglevel.high);
                                        }
                                        brokenChats.AddRange(newBrokenChats);
                                    }
                                }
                                
                            }
                            //now remove all the processed import cards from our import list, and our cache
                            foreach (cardcast_question_card c in matchingImportCards)
                            {
                                import_questions.Remove(c);
                                log("Removed card from import list: " + c.question, logging.loglevel.verbose);
                            }
                            int matches = questionCache.RemoveAll(x => Helpers.common.cleanseText(x.text) == Helpers.common.cleanseText(currentCard.question));
                            log("Removed " + matches + " from temporary local cache", logging.loglevel.verbose);
                        }

                        //now remove anything left in the cache from the master question list.
                        lo_sync.addone();
                        foreach (mod_xyzzy_card c in questionCache)
                        {
                            log("Card wasnt processed - disposing of " + c.text, logging.loglevel.warn);
                            //remove the card
                            List<mod_xyzzy_chatdata> addnBrokenChats = removeQCard(c, null);
                            brokenChats.AddRange(addnBrokenChats);
                        }
                        lo_q.complete();
                        lo_sync.addone();
                        //===================
                        //ANSWERS
                        //===================

                        //NB: Used to do a first pass to remove any matching cards from the import list. This WONT work as it will remove the ability
                        //to find out how many copies of a card we should have! Instead, take a backup copy, remove items from that, and then delete 
                        //anything that is remaining at the end. 

                        List<mod_xyzzy_card> answerCache = answers.Where(x => (x.packID == updatePack.packID)).ToList();
                        log("=============================", logging.loglevel.high);
                        log("Procesing " + answerCache.Count() + " ANSWER cards", logging.loglevel.high);
                        log("=============================", logging.loglevel.high);
                        logging.longOp lo_a = new logging.longOp("Answers", answerCache.Count());



                        //Loop through everything that is in the import list, removing items as we go. 
                        while (import_answers.Count() > 0)
                        {
                            lo_a.updateLongOp(answerCache.Count());

                            cardcast_answer_card currentCard = import_answers[0];
                            //find how many other matches in the import list we have. 
                            List<cardcast_answer_card> matchingImportCards = import_answers.Where(x => Helpers.common.cleanseText(x.answer) == Helpers.common.cleanseText(currentCard.answer)).ToList();
                            if (matchingImportCards.Count() == 0)
                            {
                                log("Error! No matches found for card '" + currentCard.answer + "' - expect at least 1", logging.loglevel.critical);
                            }
                            else
                            {
                                log("Processing " + matchingImportCards.Count() + " cards matching " + currentCard.answer, logging.loglevel.verbose);
                                List<mod_xyzzy_card> matchingLocalCards = answers.Where(x => (x.packID == updatePack.packID) && (Helpers.common.cleanseText(x.text) == Helpers.common.cleanseText(currentCard.answer))).ToList();
                                log("Found " + matchingLocalCards.Count() + " local cards", logging.loglevel.verbose);

                                //assume the first cards should match the cards coming from CardCast. Update so we have exact text
                                int j = 0;
                                while (j < matchingLocalCards.Count() && j < matchingImportCards.Count())
                                {
                                    if (matchingLocalCards[j].text != matchingImportCards[j].answer)
                                    {
                                        try
                                        {
                                            log("Local answer card updated from " + matchingLocalCards[j].text + " to " + matchingImportCards[j].answer , logging.loglevel.high);
                                            matchingLocalCards[j].text = matchingImportCards[j].answer;
                                            // matchingLocalCards[j].nrAnswers = matchingImportCards[j].nrAnswers; <- automatically set to -1 for an answer card. 
                                            nr_rep++;
                                        }
                                        catch (Exception e)
                                        {
                                            log("Error updating answer text on acard - " + e.Message, logging.loglevel.critical);
                                        }
                                    }
                                    j++;
                                }

                                //for the remainder, decide how to progress:
                                if (matchingLocalCards.Count() == matchingImportCards.Count())
                                {
                                    //log("Count matches, nothing more to do!", logging.loglevel.verbose);
                                }
                                //Need to add some new cards (either doesnt exist locally - new card - or they have added another copy remotely)
                                else if (matchingLocalCards.Count() < matchingImportCards.Count())
                                {
                                    log("Not enough local cards, adding " + (matchingImportCards.Count() - matchingLocalCards.Count()) + " extra", logging.loglevel.verbose);
                                    for (int i = matchingLocalCards.Count(); i < matchingImportCards.Count(); i++)
                                    {
                                        log("Adding card " + i + " - " + matchingImportCards[0].answer, logging.loglevel.verbose);
                                        mod_xyzzy_card x_answer = new mod_xyzzy_card(matchingImportCards[0].answer, pack.packID, matchingImportCards[0].nrAnswers);
                                        answers.Add(x_answer);
                                    }
                                }
                                //need to remove some existing cards. Retire cards by merging data into the first card
                                else
                                {
                                    for (int i = matchingImportCards.Count(); i < matchingLocalCards.Count(); i++)
                                    {
                                        List<mod_xyzzy_chatdata> newBrokenChats = removeACard(matchingLocalCards[i], matchingLocalCards[0].uniqueID);
                                        if (newBrokenChats.Count() > 0)
                                        {
                                            log("Marking " + newBrokenChats.Count() + " chats as requiring checking", logging.loglevel.high);
                                        }
                                        brokenChats.AddRange(newBrokenChats);
                                    }
                                }

                            }
                            //now remove all the processed import cards from our import list, and our cache
                            lo_sync.addone();
                            foreach (cardcast_answer_card c in matchingImportCards)
                            {
                                import_answers.Remove(c);
                                log("Removed card from import list: " + c.answer, logging.loglevel.verbose);
                            }
                            int matches = answerCache.RemoveAll(x => Helpers.common.cleanseText(x.text) == Helpers.common.cleanseText(currentCard.answer));
                            log("Removed " + matches + " from temporary local cache", logging.loglevel.verbose);
                        }

                        //now remove anything left in the cache from the master answer list. 
                        foreach (mod_xyzzy_card c in answerCache)
                        {
                            log("Card wasnt processed - suggests it was removed from the cardcast pack. Disposing of " + c.text, logging.loglevel.warn);
                            List<mod_xyzzy_chatdata> addnBrokenChats = removeQCard(c, null);
                            brokenChats.AddRange(addnBrokenChats);
                        }
                        lo_a.complete();
                        lo_sync.addone();


                        //Update the updatePack with the values from the imported pack
                        updatePack.description = pack.description;
                        updatePack.name = pack.name;
                                                
                        //swap over our return objet to the one returned from CC. 
                        pack = updatePack;
                        
                        Roboto.Settings.stats.logStat(new statItem("Packs Synced", typeof(mod_xyzzy)));
                        lo_sync.addone();
                        
                        success = true;
                    }
                    else
                    {
                        response += "Importing fresh pack " + pack.packCode + " - " + pack.name + " - " + pack.description;
                        logging.longOp lo_import = new logging.longOp("Import", import_answers.Count + import_questions.Count, lo_sync );
                        foreach (Helpers.cardcast_question_card q in import_questions)
                        {
                            mod_xyzzy_card x_question = new mod_xyzzy_card(q.question, pack.packID, q.nrAnswers);
                            questions.Add(x_question);
                            nr_qs++;
                            lo_import.addone();
                        }
                        foreach (Helpers.cardcast_answer_card a in import_answers)
                        {
                            mod_xyzzy_card x_answer = new mod_xyzzy_card(a.answer, pack.packID);
                            answers.Add(x_answer);
                            nr_as++;
                            lo_import.addone();
                        }
                        
                        response += "\n\r" + "Next sync " + pack.nextSync.ToString("f") + ".";

                        response += "\n\r" + "Added " + nr_qs.ToString() + " questions and " + nr_as.ToString() + " answers.";
                        packs.Add(pack);
                        response += "\n\r" + "Added " + pack.name + " to filter list.";
                        lo_import.complete();
                    }

                    


                }
            }
            catch (Exception e)
            {
                log("Failed to import pack " + e.ToString(), logging.loglevel.critical);
                success = false;
            }

            foreach (mod_xyzzy_chatdata c in brokenChats.Distinct())
            {
                c.check(true);
            }

            lo_sync.complete();
            log(response, logging.loglevel.normal);

            return success;

        }

        /// <summary>
        /// Remove an answer card. 
        /// </summary>
        /// <param name="cardToRemove"></param>
        /// <param name="replacementGuid"></param>
        /// <returns>Returns a list of chats that were potentially broken by the removal. </returns>
        private List<mod_xyzzy_chatdata> removeACard(mod_xyzzy_card cardToRemove, string replacementGuid)
        {
            List<mod_xyzzy_chatdata> result = new List<mod_xyzzy_chatdata>();
            
            //remove from the master list
            bool success = answers.Remove(cardToRemove);
            log("Answer " + cardToRemove.text + (success ? "successfully": "FAILED") + " to remove from master list", success ? logging.loglevel.normal:logging.loglevel.critical);

            //remove any cached answers / cards in hand
            foreach (chat c in Roboto.Settings.chatData)
            {
                mod_xyzzy_chatdata chatData = (mod_xyzzy_chatdata)c.getPluginData(typeof(mod_xyzzy_chatdata));
                if (chatData != null)
                {
                    //remove any cached answers
                    chatData.remainingAnswers.RemoveAll(x => x == cardToRemove.uniqueID);

                    //remove answers from player hands. 
                    foreach (mod_xyzzy_player p in chatData.players )
                    {
                        int i = p.cardsInHand.RemoveAll(x => x == cardToRemove.uniqueID);
                        if (i > 0 )
                        {
                            log("Removed " + i + " copies of card " + cardToRemove.text + " from player " + p.name + "'s hand", logging.loglevel.high);
                            if (replacementGuid != null)
                            {
                                p.cardsInHand.Add(replacementGuid);
                                log("Added " + replacementGuid + " to replace removed card", logging.loglevel.high);
                            }

                        }

                        //check if they have played the card. 
                        i = p.selectedCards.RemoveAll(x => x == cardToRemove.uniqueID);
                        if (i > 0)
                        {
                            log("Removed " + i + " copies of card " + cardToRemove.text + " from player " + p.name + "'s selected cards", logging.loglevel.high);
                            if (replacementGuid != null)
                            {
                                p.selectedCards.Add(replacementGuid);
                                log("Added " + replacementGuid + " to replace removed selected card", logging.loglevel.high);
                            }
                            else
                            {
                                //removed a played card without a replacement guid - likely to all end in failure. 
                                result.Add(chatData);
                            }

                        }


                    }



                }
            }
            return result;


        }

        private List<mod_xyzzy_chatdata> removeQCard(mod_xyzzy_card cardToRemove, string replacementGuid)
        {
            List<mod_xyzzy_chatdata> result = new List<mod_xyzzy_chatdata>();

            //remove from the master list
            bool success = questions.Remove(cardToRemove);
            log("Question " + cardToRemove.text + (success ? "successfully" : "FAILED") + " to remove from master list", success ? logging.loglevel.normal : logging.loglevel.critical);


            //remove any cached questions
            foreach (chat c in Roboto.Settings.chatData)
            {
                mod_xyzzy_chatdata chatData = (mod_xyzzy_chatdata)c.getPluginData(typeof(mod_xyzzy_chatdata));
                if (chatData != null)
                {
                    int recsRemoved = chatData.remainingQuestions.RemoveAll(x => x == cardToRemove.uniqueID);
                    if (recsRemoved >0 )
                    {
                        log("Removed question card from remaining question " + recsRemoved + " times from " + c.ToString(), logging.loglevel.warn);
                    }
                    //if we remove the current question, invalidate the chat. Will reask a question once the rest of the import is done. 
                    if (chatData.currentQuestion == cardToRemove.uniqueID)
                    {
                        result.Add(chatData);
                        if (replacementGuid != null)
                        {
                            chatData.currentQuestion = replacementGuid;
                            log("The current question " + chatData.currentQuestion + " guid for chat " + c.ToString() + " has been replaced.", logging.loglevel.high);
                        }
                        else
                        {

                            log("The current question " + chatData.currentQuestion + " for " + chatData.status + " chat " + c.ToString() + " has been removed!", chatData.status == xyzzy_Statuses.Stopped? logging.loglevel.normal: logging.loglevel.high);
                        }
                    }
                }
            }
            return result;
        }

        /* - Dont do this as it will remove valid duplicate cards. Rely on the regular Sync instead,

        public void removeDupeCards()
        {
            //loop through each pack. Pack filter should be up-to date even if this is called from the startup-checks.
            foreach (cardcast_pack pack in packs)
            {
                //add each card to one of these lists depending on whether it has been seen or not. Remove the removal ones afterwards.
                List<mod_xyzzy_card> validQCards = new List<mod_xyzzy_card>();
                List<mod_xyzzy_card> removeQCards = new List<mod_xyzzy_card>();
                foreach (mod_xyzzy_card c in questions.Where(y => y.category == pack.name) )
                {
                    //is there a matching card already?
                    List<mod_xyzzy_card> matchList = validQCards.Where(x => (x.category == pack.name && Helpers.common.cleanseText(x.text) == Helpers.common.cleanseText(c.text))).ToList();
                    if (matchList.Count() > 1)
                    {
                        removeQCards.Add(c);
                        //updating any references in active games. 
                        replaceCardReferences(c, matchList[0], "Q");
                    }
                    else
                    {
                        validQCards.Add(c);
                    }
                }
                //remove any flagged cards
                foreach (mod_xyzzy_card c in removeQCards) { questions.Remove(c); }

                //Repeat for answers
                List<mod_xyzzy_card> validACards = new List<mod_xyzzy_card>();
                List<mod_xyzzy_card> removeACards = new List<mod_xyzzy_card>();
                foreach (mod_xyzzy_card c in answers.Where(y => y.category == pack.name))
                {
                    //is there a matching card already?
                    List<mod_xyzzy_card> matchList = validACards.Where(x => (x.category == pack.name && Helpers.common.cleanseText(x.text) == Helpers.common.cleanseText(c.text))).ToList();
                    if (matchList.Count() > 1)
                    {
                        removeACards.Add(c);
                        //updating any references in active games. 
                        replaceCardReferences(c, matchList[0], "A");
                    }
                    else
                    {
                        validACards.Add(c);
                    }
                }
                //remove any flagged cards
                int total = removeQCards.Count() + removeACards.Count();
                foreach (mod_xyzzy_card c in removeACards) { answers.Remove(c); }
                log("Removed " + removeQCards.Count() + " / " + removeACards.Count() 
                    + " duplicate q/a from " + pack.name 
                    + " new totals are " + questions.Where(y => y.category == pack.name).Count() 
                    + " / " + answers.Where(y => y.category == pack.name).Count() + " q/a."
                    , total > 0? logging.loglevel.warn:logging.loglevel.verbose);


            }
        }*/

        private void replaceCardReferences(mod_xyzzy_card old, mod_xyzzy_card newcard, string cardType)
        {
            foreach (chat c in Roboto.Settings.chatData)
            {
                mod_xyzzy_chatdata chatdata = (mod_xyzzy_chatdata)c.getPluginData(typeof(mod_xyzzy_chatdata));
                if (chatdata != null)
                {
                    chatdata.replaceCard(old, newcard, cardType);
                }
            }
        }
    }
}
