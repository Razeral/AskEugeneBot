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

        public string LongName { get; set; }
        public string Description { get; set; }
    }

    [Serializable]
    public class RootDialog : IDialog<object>
    {
        public async Task StartAsync(IDialogContext context)
        {
            /* Wait until the first message is received from the conversation and call MessageReceviedAsync 
            *  to process that message. */
            context.Wait(this.MessageReceivedAsync);
        }

        private async Task MessageReceivedAsync(IDialogContext context, IAwaitable<IMessageActivity> result)
        {
            /* When MessageReceivedAsync is called, it's passed an IAwaitable<IMessageActivity>. To get the message,
            *  await the result. */
            var message = await result;
            System.Diagnostics.Trace.TraceInformation("Message received - " + message.Text);
            string json = JsonConvert.SerializeObject(message, Formatting.Indented);
            System.Diagnostics.Trace.TraceInformation("<<<MESSAGE>>> - " + json);

            var msg = "";

            //System.Diagnostics.Trace.TraceInformation(">>0>> " + (message.Text.Split(' '))[0]);
            //System.Diagnostics.Trace.TraceInformation(">>1>> " + (message.Text.Split(' '))[1]);

            if ((message.Text.Split(' '))[0] == "teach" && (message.Text.Split(' ')).Length >= 2)
            {
                try
                {
                    System.Diagnostics.Trace.TraceInformation((message.Text.Split(' '))[0] + " new word " + (message.Text.Split(' '))[1]);
                    context.Wait(TeachAcronymAsync);
                }
                catch (Exception e)
                {
                    System.Diagnostics.Trace.TraceInformation(e.Message);
                    context.Wait(MessageReceivedAsync);
                }
            }
            else
            {
                try
                {
                    //await context.PostAsync("Your query is " + message.ChannelData.query);

                    CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                    CloudConfigurationManager.GetSetting("TableStorageConnString"));

                    // Create the table client.
                    CloudTableClient tableClient = storageAccount.CreateCloudTableClient();

                    // Create the CloudTable object that represents the "people" table.
                    CloudTable table = tableClient.GetTableReference("acronyms");

                    // Create a new customer entity.
                    //TableEntity customer1 = new TableEntity("JTC", "Test2");

                    TableQuery<AcronymEntity> query = new TableQuery<AcronymEntity>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, "JTC"));
                    foreach (AcronymEntity entity in table.ExecuteQuery(query))
                    {
                        if (entity.RowKey.ToUpper() == message.Text.Trim().ToUpper())
                            msg += " " + entity.LongName;
                    }
                    System.Diagnostics.Trace.TraceInformation("5");
                    if (msg.Length > 0)
                    {
                        await context.PostAsync(message.Text + " = " + msg);
                    }
                    else
                    {
                        await context.PostAsync("Tell me the acronym only please. e.g. JTC");
                    }
                }
                catch (Exception e)
                {
                    System.Diagnostics.Trace.TraceInformation(e.Message);
                    await context.PostAsync("There was an error");
                }
                context.Wait(MessageReceivedAsync);
            }

            // Create the TableOperation object that inserts the customer entity.
            //TableOperation insertOperation = TableOperation.Insert(customer1);

            // Execute the insert operation.
            //table.Execute(insertOperation);


            /*var qnaSubscriptionKey = Utils.GetAppSetting("QnASubscriptionKey");
            var qnaKBId = Utils.GetAppSetting("QnAKnowledgebaseId");

            // QnA Subscription Key and KnowledgeBase Id null verification
            if (!string.IsNullOrEmpty(qnaSubscriptionKey) && !string.IsNullOrEmpty(qnaKBId))
            {
                await context.Forward(new BasicQnAMakerDialog(), AfterAnswerAsync, message, CancellationToken.None);
            }
            else
            {
                await context.PostAsync("Please set QnAKnowledgebaseId and QnASubscriptionKey in App Settings. Get them at https://qnamaker.ai.");
            }*/


        }

        private async Task AfterAnswerAsync(IDialogContext context, IAwaitable<IMessageActivity> result)
        {
            // wait for the next user message
            context.Wait(MessageReceivedAsync);
        }

        private async Task TeachAcronymAsync(IDialogContext context, IAwaitable<IMessageActivity> result)
        {
            System.Diagnostics.Trace.TraceInformation("MSG IN TeachAcronymAsync -> Before result");
            var message = await result;
            System.Diagnostics.Trace.TraceInformation("MSG IN TeachAcronymAsync -> " + message.Text);
            var newAcronym = message.Text.Substring(("teach").Length + 1).Trim();
            await context.PostAsync("What does " + newAcronym + " mean?");
            context.UserData.SetValue("newAcronym", newAcronym);


            //context.Wait(MessageReceivedAsync);
            context.Wait(UnderstandAcronymAsync);
        }

        private async Task UnderstandAcronymAsync(IDialogContext context, IAwaitable<IMessageActivity> result)
        {
            System.Diagnostics.Trace.TraceInformation("MSG IN UnderstandAcronymAsync -> Before result");
            var message = await result;
            System.Diagnostics.Trace.TraceInformation("MSG IN UnderstandAcronymAsync -> " + message.Text);
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
            System.Diagnostics.Trace.TraceInformation("MSG IN StoreAcronymAsync -> Before result");
            var confirm = await result;

            if (confirm)
            {
                await context.PostAsync("What's the password?");
                context.Wait(StoreConfirmedAcronymAsync);
            }
            else
            {
                await context.PostAsync("Then talk so much for what...");
                context.Wait(MessageReceivedAsync);
            }
        }

        private async Task StoreConfirmedAcronymAsync(IDialogContext context, IAwaitable<IMessageActivity> result)
        {
            System.Diagnostics.Trace.TraceInformation("MSG IN StoreConfirmedAcronymAsync -> Before result");
            var message = await result;
            System.Diagnostics.Trace.TraceInformation("MSG IN StoreConfirmedAcronymAsync -> " + message.Text);

            if (message.Text.ToLower() == "jtcivsd1")
            {
                try
                {
                    string newAcronym = "";
                    string newAcronymMeaning = "";
                    
                    context.UserData.TryGetValue("newAcronym", out newAcronym);
                    context.UserData.TryGetValue("newAcronymMeaning", out newAcronymMeaning);
                    System.Diagnostics.Trace.TraceInformation("MSG IN StoreConfirmedAcronymAsync -> " + newAcronym + " ||| " + newAcronymMeaning);

                    CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                        CloudConfigurationManager.GetSetting("TableStorageConnString"));

                    // Create the table client.
                    CloudTableClient tableClient = storageAccount.CreateCloudTableClient();

                    // Create the CloudTable object that represents the "people" table.
                    CloudTable table = tableClient.GetTableReference("acronyms");

                    // Create a new customer entity.
                    AcronymEntity record = new AcronymEntity("JTC", newAcronym);
                    record.LongName = newAcronymMeaning;
                    record.Description = "-";
                    //TableEntity record = new TableEntity("JTC", "WHATISWRONG");

                    // Create the TableOperation object that inserts the customer entity.
                    TableOperation insertOperation = TableOperation.Insert(record);

                    System.Diagnostics.Trace.TraceInformation("123");
                    // Execute the insert operation.
                    table.Execute(insertOperation);
                    System.Diagnostics.Trace.TraceInformation("1234");

                    await context.PostAsync("Great! Learnt something new today!");
                }
                catch (StorageException ex)
                {
                    var requestInformation = ex.RequestInformation;
                    System.Diagnostics.Trace.TraceInformation(requestInformation.HttpStatusCode.ToString());
                    System.Diagnostics.Trace.TraceInformation(requestInformation.HttpStatusMessage);
                    System.Diagnostics.Trace.TraceInformation(requestInformation.ExtendedErrorInformation.ErrorCode);
                    System.Diagnostics.Trace.TraceInformation(requestInformation.ExtendedErrorInformation.ErrorMessage);
                }
                catch (Exception e)
                {
                    System.Diagnostics.Trace.TraceInformation(e.Message + " || " + e.StackTrace);
                    await context.PostAsync("Sorry something went wrong!");
                }
            }
            else
            {
                await context.PostAsync("Nope, wrong password.");
            }
            context.Wait(MessageReceivedAsync);
        }
    }

// For more information about this template visit http://aka.ms/azurebots-csharp-qnamaker
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