using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using Roboto.Helpers;

namespace Roboto.Modules
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
        //removed - moved into telegramAPI and settings class. 
        //public List<mod_xyzzy_expectedReply> expectedReplies = new List<mod_xyzzy_expectedReply>(); //replies expected by the various chats
        //internal mod_xyzzy_coredata() { }



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

        public void startupChecks()
        {
            
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

            //now remove category from all cards that have a guid. 
            success = 0;
            
            foreach (mod_xyzzy_card q in questions.Where(x => x.packID != null)){ q.category = null; q.TempCategory = null; success++; }
            foreach (mod_xyzzy_card a in answers.Where(x => x.packID != null)) { a.category = null; a.TempCategory = null; success++; }
            #pragma warning restore 612, 618

            if (success + fail > 0)
            {
                log("DATAFIX: Wiped category from " + success + " cards successfully.", logging.loglevel.warn);
            }

            //lets see if packs with null Cardcast Pack codes can be populated by looking through our other packs
            foreach (cardcast_pack p in packs.Where(x => string.IsNullOrEmpty(x.packCode)))
            {
                List<cardcast_pack> matchingPacks = getPacks(p.name).Where(x => x.packID != p.packID).ToList();
                if (matchingPacks.Count >0 )
                {
                    p.packCode = matchingPacks[0].packCode;
                    log("DATAFIX: Orphaned pack " + p.name + " has been matched against an existing pack, and packcode set to " + p.packCode, logging.loglevel.warn);
                }

            }


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
                    
                    log("Finished merging " + packsMerged + " into master pack " + masterPack.name + ". " + cardsUpdated + " cards moved to master pack", logging.loglevel.high);
                    //1=1
                }







            }








            /* 
            ==========
            Should never get into this scenario any more - checking above
            ==========
            //check that a pack exists for each card in Q / A
            foreach (mod_xyzzy_card q in questions)
            {
                if (packs.Where(x => x.packID == q.packID).Count() == 0)
                {
                    log("Creating dummy pack for " + q.category, logging.loglevel.high);
                    packs.Add(new Helpers.cardcast_pack(q.category, "", q.category));
                }
            }
            foreach (mod_xyzzy_card a in answers)
            {
                if (packs.Where(x => x.name == a.category).Count() == 0)
                {
                    log("Creating dummy pack for " + a.category, logging.loglevel.high);
                    packs.Add(new Helpers.cardcast_pack(a.category, "", a.category));
                }
            }

            //remove any dupes. Must keep oldest pack first
            List<Helpers.cardcast_pack> newPackList = new List<cardcast_pack>();
            foreach (cardcast_pack p in packs)
            {
                if (newPackList.Where(x => x.name == p.name).Count() == 0) { newPackList.Add(p); }
            }
            if (packs.Count != newPackList.Count)
            {
                log("Deduped global packlist, was " + packs.Count() + " now " + newPackList.Count(), logging.loglevel.high);
                packs = newPackList;
            }*/
        }

        /// <summary>
        /// check for any packs that can be (and need to be) synced
        /// </summary>
        public void packSyncCheck()
        {
            
            int backlog = packs.Where(p => (p.packCode != null && p.packCode != "" && p.nextSync < DateTime.Now)).Count();
            log("There are " + backlog + " packs outstanding to sync. Picking first 15", logging.loglevel.normal );
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
                if (!success) { p.syncFailed(); }
                else { p.syncSuccess(); }
            }
            foreach (Helpers.cardcast_pack p in packs.Where(x => x.failCount > 5).ToList())
            {
                //TODO - lets remove the pack!

    
            }




        }



        /// <summary>
        /// Import / Sync a cardcast pack into the xyzzy localdata
        /// </summary>
        /// <param name="packFilter"></param>
        /// <returns>String containing details of the pack and cards added. String will be empty if import failed.</returns>
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

            
            try
            {
                log("Attempting to sync/import " + packCode);
                //Call the cardcast API. We should get an array of cards back (but in the wrong format)
                //note that this directly updates the pack object we are going to return - so need to shuffle around later if we sync a pack
                success = Helpers.cardCast.getPackCards(ref packCode, out pack, ref import_questions, ref import_answers);

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

                        //remove any cached questions that no longer exist. Add them to a list first to allow us to loop;
                        //List<mod_xyzzy_card> remove_cards = new List<mod_xyzzy_card>();
                        //ignore any cards that already exist in the cache. Add them to a list first to allow us to loop;
                        //List<mod_xyzzy_card> exist_cards = new List<mod_xyzzy_card>();


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

                        //Loop through everything that is in the import list, removing items as we go. 
                        while (import_questions.Count() > 0)
                        {
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
                        foreach (mod_xyzzy_card c in questionCache)
                        {
                            log("Card wasnt processed - disposing of " + c.text, logging.loglevel.warn);
                            List<mod_xyzzy_chatdata> addnBrokenChats = removeQCard(c, null);
                            brokenChats.AddRange(addnBrokenChats);
                        }

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




                        //Loop through everything that is in the import list, removing items as we go. 
                        while (import_answers.Count() > 0)
                        {
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





                        /*
                       
                            //if they do already exist, remove them from the import list (because they exist!)
                            else
                            {
                                exist_cards.Add(q);
                                log("Card EXISTS: " + q.text, logging.loglevel.verbose);
                            }
                        }
                        //now remove them from the localdata
                        foreach (mod_xyzzy_card q in remove_cards)
                        {
                            log("Question " + q.text + " no longer exists in cardcast, removing", logging.loglevel.warn);
                            questions.Remove(q);
                            //remove any cached questions
                            foreach(chat c in Roboto.Settings.chatData)
                            {
                                
                                mod_xyzzy_chatdata chatData = (mod_xyzzy_chatdata) c.getPluginData(typeof(mod_xyzzy_chatdata));
                                if (chatData != null)
                                {
                                    chatData.remainingQuestions.RemoveAll(x => x == q.uniqueID);
                                    //if we remove the current question, invalidate the chat. Will reask a question once the rest of the import is done. 
                                    if (chatData.currentQuestion == q.uniqueID)
                                    {
                                        log("The current question " + chatData.currentQuestion + " for chat " + c.chatID + " has been removed!");
                                        if (!brokenChats.Contains(chatData)) { brokenChats.Add(chatData); }
                                    }
                                }
                            }
                        }
                        //or remove from the import list (they should exist locally already, so we dont need to porcess further). 
                        foreach (mod_xyzzy_card q in exist_cards)
                        {
                            //try find a match. 
                            cardcast_question_card match = null;
                            try
                            {
                                List< cardcast_question_card> matchedCards = import_questions.Where(y => Helpers.common.cleanseText(y.question) == Helpers.common.cleanseText(q.text)).ToList();
                                if (matchedCards.Count > 0) { match = matchedCards[0]; } 
                                else
                                {
                                    //if we get down here, we probably removed a duplicate
                                    log("Local card couldnt be found (duplicate removed?) Tried to match " + q.text , logging.loglevel.normal);
                                }
                            }
                            catch (Exception e)
                            {
                                log("Error finding cleansed version of q card - " + e.Message, logging.loglevel.critical);
                            }

                            //assuming we found the card, update the card (if needed) so it exactly matches the one from cardcast. 
                            if (match != null && q.text != match.question)
                            {
                                try
                                {
                                    log("Question text updated from " + q.text + " to " + match.question);
                                    q.text = match.question;
                                    q.nrAnswers = match.nrAnswers;
                                    nr_rep++;
                                }
                                catch (Exception e)
                                {
                                    log("Error updating question text on qcard - " + e.Message, logging.loglevel.critical);
                                }
                            }
                            //remove the card from the import list (as we have processed it now)
                            try
                            {
                                int removed = import_questions.RemoveAll(x => x.question == q.text); //swallow this. 
                                log("Removed : " + removed + " cards with text " + q.text, removed != 1 ? logging.loglevel.high : logging.loglevel.verbose);
                            }
                            catch (Exception e)
                            {
                                log("Error removing qcard from importlist - " + e.Message, logging.loglevel.critical);
                            }
                        }
                        //add the rest to the localData
                        foreach (Helpers.cardcast_question_card q in import_questions)
                        {
                            mod_xyzzy_card x_question = new mod_xyzzy_card(q.question, pack.packID, q.nrAnswers);
                            questions.Add(x_question);
                        }
                        response += "\n\r" + "Qs: Removed " + remove_cards.Count() + " from local. Skipped " + exist_cards.Count() + " as already exist. Updated " + nr_rep + ". Added " + import_questions.Count() + " new / replacement cards";


            */












                        /*
                        //do the same for the answer cards
                        nr_rep = 0;
                        remove_cards.Clear();
                        exist_cards.Clear();
                        foreach (mod_xyzzy_card a in answers.Where(x => x.packID == l_packID))
                        {
                            //find existing cards which don't exist in our import pack
                            if ((import_answers.Where(y => Helpers.common.cleanseText(y.answer) == Helpers.common.cleanseText(a.text))).Count() == 0)
                            {
                                remove_cards.Add(a);
                                log("Card EXTRA: " + a.text, logging.loglevel.low);
                            }
                            //if they do already exist, remove them from the import list (because they exist!)
                            else
                            {
                                exist_cards.Add(a);
                                log("Card EXISTS: " + a.text, logging.loglevel.verbose);
                            }
                        }
                        //now remove them from the localdata (NB: Dont need to do all the stuff we do for Qs, as missing answers are less of a problem). 
                        foreach (mod_xyzzy_card a in remove_cards)
                        {
                            answers.Remove(a);
                            log("Answer " + a.text + " no longer exists in cardcast, removing", logging.loglevel.warn);
                        }

                        //or remove from the import list (they should exist locally already, so we dont need to porcess further). 
                        foreach (mod_xyzzy_card a in exist_cards)
                        {
                            //update the local text if it was a match-ish
                            cardcast_answer_card matcha = null;
                            List<cardcast_answer_card> amatches = import_answers.Where(y => Helpers.common.cleanseText(y.answer) == Helpers.common.cleanseText(a.text)).ToList();
                            if (amatches.Count > 0)
                            {
                                matcha = amatches[0];
                            }
                            else
                            {
                                //if we get down here, we probably removed a duplicate
                                log("Local card couldnt be found (duplicate removed?) Tried to match " + a.text, logging.loglevel.normal);
                            }

                            //assuming we found the card, update the card (if needed) so it exactly matches the one from cardcast. 
                            if (matcha != null && a.text != matcha.answer)
                            {
                                log("Answer text updated from " + a.text + " to " + matcha.answer);
                                a.text = matcha.answer;
                                nr_rep++;
                            }
                            
                            //remove the card from the import list  (as we have processed it now)
                            try
                            {
                                int aremoved = import_answers.RemoveAll(x => x.answer == a.text); //swallow this. 
                                log("Removed : " + aremoved + " cards with text " + a.text, aremoved != 1 ? logging.loglevel.high : logging.loglevel.verbose);
                            }
                            catch (Exception e)
                            {
                                log("Error removing acard from importlist - " + e.Message, logging.loglevel.critical);
                            }

                        }
                        

                        //add the rest to the localData
                        foreach (Helpers.cardcast_answer_card a in import_answers)
                        {
                            mod_xyzzy_card x_answer = new mod_xyzzy_card(a.answer, pack.packID);
                            answers.Add(x_answer);
                        }

                        
                        response += "\n\r" + "As: Removed " + remove_cards.Count() + " from local. Skipped " + exist_cards.Count() + " as already exist. Updated " + nr_rep + ". Added " + import_answers.Count() + " new / replacement cards";

            */

                        //Update the updatePack with the values from the imported pack
                        updatePack.description = pack.description;
                        updatePack.name = pack.name;
                                                
                        //swap over our return objet to the one returned from CC. 
                        pack = updatePack;
                        
                        Roboto.Settings.stats.logStat(new statItem("Packs Synced", typeof(mod_xyzzy)));
                        
                        success = true;
                    }
                    else
                    {
                        response += "Importing fresh pack " + pack.packCode + " - " + pack.name + " - " + pack.description;
                        foreach (Helpers.cardcast_question_card q in import_questions)
                        {
                            mod_xyzzy_card x_question = new mod_xyzzy_card(q.question, pack.packID, q.nrAnswers);
                            questions.Add(x_question);
                            nr_qs++;
                        }
                        foreach (Helpers.cardcast_answer_card a in import_answers)
                        {
                            mod_xyzzy_card x_answer = new mod_xyzzy_card(a.answer, pack.packID);
                            answers.Add(x_answer);
                            nr_as++;
                        }
                        
                        response += "\n\r" + "Next sync " + pack.nextSync.ToString("f") + ".";

                        response += "\n\r" + "Added " + nr_qs.ToString() + " questions and " + nr_as.ToString() + " answers.";
                        packs.Add(pack);
                        response += "\n\r" + "Added " + pack.name + " to filter list.";
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
                c.askQuestion(false);
            }

            log(response, logging.loglevel.normal);

            return success;

        }

        private List<mod_xyzzy_chatdata> removeACard(mod_xyzzy_card cardToRemove, string replacementGuid)
        {
            List<mod_xyzzy_chatdata> result = new List<mod_xyzzy_chatdata>();
            log("Answer " + cardToRemove.text + " no longer exists in cardcast, removing", logging.loglevel.warn);

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
                                p.cardsInHand.Add(replacementGuid);
                                log("Added " + replacementGuid + " to replace removed selected card", logging.loglevel.high);
                            }
                            else
                            {
                                //removed a played card without a replacement guid - likely to all end in failure. 
                                result.Add(chatData);
                            }

                        }


                    }


                    //if we remove the current question, invalidate the chat. Will reask a question once the rest of the import is done. 



                    //if (chatData.currentQuestion == cardToRemove.uniqueID)
                    //{
                    //    result.Add(chatData);
                    //    if (replacementGuid != null)
                    //    {
                    //        chatData.currentQuestion = replacementGuid;
                    //        log("The current question " + chatData.currentQuestion + " guid for chat " + c.chatID + " has been replaced.", logging.loglevel.high);
                    //    }
                    //    else
                    //    {
                    //        log("The current question " + chatData.currentQuestion + " for chat " + c.chatID + " has been removed!", logging.loglevel.warn);
                    //    }
                    //}
                }
            }
            return result;


        }

        private List<mod_xyzzy_chatdata> removeQCard(mod_xyzzy_card cardToRemove, string replacementGuid)
        {
            List<mod_xyzzy_chatdata> result = new List<mod_xyzzy_chatdata>();
            log("Question " + cardToRemove.text + " no longer exists in cardcast, removing", logging.loglevel.warn);
            //remove any cached questions
            foreach (chat c in Roboto.Settings.chatData)
            {
                mod_xyzzy_chatdata chatData = (mod_xyzzy_chatdata)c.getPluginData(typeof(mod_xyzzy_chatdata));
                if (chatData != null)
                {
                    chatData.remainingQuestions.RemoveAll(x => x == cardToRemove.uniqueID);
                    //if we remove the current question, invalidate the chat. Will reask a question once the rest of the import is done. 
                    if (chatData.currentQuestion == cardToRemove.uniqueID)
                    {
                        result.Add(chatData);
                        if (replacementGuid != null)
                        {
                            chatData.currentQuestion = replacementGuid;
                            log("The current question " + chatData.currentQuestion + " guid for chat " + c.chatID + " has been replaced.", logging.loglevel.high);
                        }
                        else
                        {

                            log("The current question " + chatData.currentQuestion + " for chat " + c.chatID + " has been removed!", logging.loglevel.warn);
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
