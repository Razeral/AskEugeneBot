using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Bot.Builder.Azure;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.CognitiveServices.QnAMaker;
using Microsoft.Bot.Connector;

using System.Collections.Generic;
using System.Net;
using System.IO;
using Newtonsoft.Json;

using Microsoft.Azure;
using Microsoft.Azure.Storage;
using Microsoft.Azure.CosmosDB.Table;

namespace Microsoft.Bot.Sample.QnABot
{
    public class AcronymEntity : TableEntity
    {
        public AcronymEntity(string pkey, string rkey)
        {
            this.PartitionKey = pkey;
            this.RowKey = rkey;
        }

        public AcronymEntity() { }

        public string longName { get; set; }
        public string description { get; set; }
    }
    public class ConnectToTableStorage
    {
        public static CloudTable Connect(string TableName)
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                    CloudConfigurationManager.GetSetting("TableStorageConnString"));
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            return tableClient.GetTableReference(TableName);
        }
    }
    


    [Serializable]
    public class RootDialog : IDialog<object>
    {
        public async Task StartAsync(IDialogContext context)
        {
            await context.PostAsync("Hello");
            context.Wait(this.MessageReceivedAsync);
        }

        private async Task MessageReceivedAsync(IDialogContext context, IAwaitable<IMessageActivity> result)
        {
            var message = await result;
            //System.Diagnostics.Trace.TraceInformation("Message received - " + message.Text);
            //string json = JsonConvert.SerializeObject(message, Formatting.Indented);
            //System.Diagnostics.Trace.TraceInformation("<<<MESSAGE>>> - " + json);

            var msg = "";
            var isFirstWordTeach = (message.Text.Split(' '))[0].ToLower() == "teach";
            var wordCount = (message.Text.Split(' ')).Length;

            if (isFirstWordTeach)
            {
                if (wordCount <= 1)
                {
                    await context.PostAsync("Please say \"teach\" followed by the word you want me to learn (e.g. teach ABC)");
                    context.Wait(MessageReceivedAsync);
                }
                else
                {
                    var newAcronym = message.Text.Substring(("teach").Length + 1).Trim().ToUpper();

                    Random rnd = new Random();
                    int i = rnd.Next(1, 3);
                    var question = "";

                    switch(i)
                    {
                        case 1:
                            question = "What does " + newAcronym + " mean?";
                            break;
                        case 2:
                            question = "So... " + newAcronym + " means what?";
                            break;
                        case 3:
                            question = "Ummm. Ok... " + newAcronym + " is?";
                            break;
                    }
                    
                    await context.PostAsync(question);
                    context.UserData.SetValue("newAcronym", newAcronym.ToUpper());
                    context.Wait(TeachAcronymAsync);
                }
            }
            else
            {
                try
                {
                    CloudTable table = ConnectToTableStorage.Connect("acronyms");

                    TableQuery<AcronymEntity> query = new TableQuery<AcronymEntity>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, "JTC"));
                    foreach (AcronymEntity entity in table.ExecuteQuery(query))
                    {
                        if (entity.RowKey.ToUpper() == message.Text.Trim().ToUpper())
                        {
                            if (msg == "")
                                msg += entity.longName;
                            else
                                msg += " or " + entity.longName;
                        }
                    }

                    if (msg.Length > 0)
                    {
                        Random rnd = new Random();
                        int i = rnd.Next(1, 3);
                        var reply = "";

                        switch (i)
                        {
                            case 1:
                                reply = "From what I know... " + message.Text + " = " + msg;
                                break;
                            case 2:
                                reply = "I think someone told me that " + message.Text + " means " + msg;
                                break;
                            case 3:
                                reply = "So simple... " + msg + " lor.";
                                break;
                        }
                        await context.PostAsync(reply);
                    }
                    else
                        await context.PostAsync("I didn't understand that! :D Ask me an acronym (e.g. JTC) or say \"teach\" followed by a new word to help me learn! (e.g. teach JTC)");
                }
                catch (Exception e)
                {
                    System.Diagnostics.Trace.TraceError(e.Message);
                    await context.PostAsync("Sorry, there was an error [12]");
                }
                context.Wait(MessageReceivedAsync);
            }
            
            /*var qnaSubscriptionKey = Utils.GetAppSetting("QnASubscriptionKey");
            var qnaKBId = Utils.GetAppSetting("QnAKnowledgebaseId");

            // QnA Subscription Key and KnowledgeBase Id null verification
            if (!string.IsNullOrEmpty(qnaSubscriptionKey) && !string.IsNullOrEmpty(qnaKBId))
                await context.Forward(new BasicQnAMakerDialog(), AfterAnswerAsync, message, CancellationToken.None);
            else
                await context.PostAsync("Please set QnAKnowledgebaseId and QnASubscriptionKey in App Settings. Get them at https://qnamaker.ai.");
            */
        }

        private async Task AfterAnswerAsync(IDialogContext context, IAwaitable<IMessageActivity> result)
        {
            context.Wait(MessageReceivedAsync);
        }

        private async Task TeachAcronymAsync(IDialogContext context, IAwaitable<IMessageActivity> result)
        {
            var message = await result;
            var newAcronym = "";
            context.UserData.TryGetValue("newAcronym", out newAcronym);
            context.UserData.SetValue("newAcronymMeaning", message.Text);

            PromptDialog.Confirm(
                context,
                StoreAcronymAsync,
                "So " + newAcronym + " stands for " + message.Text + "?",
                "Choose one of the choices can?",
                promptStyle: PromptStyle.Auto);
        }

        private async Task StoreAcronymAsync(IDialogContext context, IAwaitable<bool> result)
        {
            var confirm = await result;
            if (confirm)
            {
                await context.PostAsync("What's the password?");
                //context.Wait(StoreConfirmedAcronymAsync);
                context.Wait(ConfirmConfirmedAcronymAsync);
            }
            else
            {
                await context.PostAsync("Then talk so much for what...");
                context.Wait(MessageReceivedAsync);
            }
        }

        private async Task ConfirmConfirmedAcronymAsync(IDialogContext context, IAwaitable<IMessageActivity> result)
        {
            var message = await result;
            if (message.Text.ToLower() == "jtcivsd1")
            {
                string newAcronym = "";
                context.UserData.TryGetValue("newAcronym", out newAcronym);

                string longName = "";

                CloudTable table = ConnectToTableStorage.Connect("acronyms");

                System.Diagnostics.Trace.TraceInformation("before row lookup");

                try
                {
                    TableQuery<AcronymEntity> query = new TableQuery<AcronymEntity>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, "JTC"));
                    foreach (AcronymEntity entity in table.ExecuteQuery(query))
                    {
                        if (entity.RowKey.ToUpper() == newAcronym.ToUpper())
                            longName = entity.longName;
                    }


                    if (longName.Length > 0) // Means already in db
                    {
                        PromptDialog.Confirm(
                            context,
                            StoreConfirmedAcronymAsync,
                            "But got people say that " + newAcronym + " means " + longName + " already leh? You sure yours correct ah...",
                            "Choose one of the choices can?",
                            promptStyle: PromptStyle.Auto);
                    }
                    else // Means new - see how to refactor this fn
                    {
                        try
                        {
                            //string newAcronym = "";
                            string newAcronymMeaning = "";

                            context.UserData.TryGetValue("newAcronym", out newAcronym);
                            context.UserData.TryGetValue("newAcronymMeaning", out newAcronymMeaning);

                            //CloudTable table = ConnectToTableStorage.Connect("acronyms");

                            AcronymEntity record = new AcronymEntity("JTC", newAcronym);
                            record.longName = newAcronymMeaning;
                            record.description = "-";

                            TableOperation insertOperation = TableOperation.InsertOrMerge(record);

                            table.Execute(insertOperation);
                            await context.PostAsync("Great! Learnt something new today!");
                        }
                        catch (Exception e)
                        {
                            System.Diagnostics.Trace.TraceInformation(e.Message + " || " + e.StackTrace);
                            await context.PostAsync("Sorry something went wrong!");
                        }
                    }
                }
                catch (Exception e)
                {
                    System.Diagnostics.Trace.TraceInformation(e.Message + " || " + e.StackTrace);
                    await context.PostAsync("Sorry something went wrong!");
                }
            }
            else
                await context.PostAsync("Nope, wrong password.");
            context.Wait(MessageReceivedAsync);

        }

        private async Task StoreConfirmedAcronymAsync(IDialogContext context, IAwaitable<bool> result)
        {
            var confirm = await result;
            if (confirm)
            {
                try
                {
                    string newAcronym = "";
                    string newAcronymMeaning = "";

                    context.UserData.TryGetValue("newAcronym", out newAcronym);
                    context.UserData.TryGetValue("newAcronymMeaning", out newAcronymMeaning);

                    CloudTable table = ConnectToTableStorage.Connect("acronyms");

                    AcronymEntity record = new AcronymEntity("JTC", newAcronym);
                    record.longName = newAcronymMeaning;
                    record.description = "-";

                    TableOperation insertOperation = TableOperation.InsertOrMerge(record);

                    table.Execute(insertOperation);
                    await context.PostAsync("Great! Learnt something new today!");
                }
                catch (Exception e)
                {
                    System.Diagnostics.Trace.TraceInformation(e.Message + " || " + e.StackTrace);
                    await context.PostAsync("Sorry something went wrong!");
                }
            }
            else
            {
                await context.PostAsync("...");
            }
            context.Wait(MessageReceivedAsync);
        }
    }

    [Serializable]
    public class BasicQnAMakerDialog : QnAMakerDialog
    {
        // Go to https://qnamaker.ai and feed data, train & publish your QnA Knowledgebase.        
        // Parameters to QnAMakerService are:
        // Required: subscriptionKey, knowledgebaseId, 
        // Optional: defaultMessage, scoreThreshold[Range 0.0 – 1.0]
        public BasicQnAMakerDialog() : base(new QnAMakerService(new QnAMakerAttribute(Utils.GetAppSetting("QnASubscriptionKey"), Utils.GetAppSetting("QnAKnowledgebaseId"), "No good match in FAQ.", 0.5)))
        {}
    }
}