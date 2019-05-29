// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.BotBuilderSamples
{
    /// <summary>
    /// Main entry point and orchestration for bot.
    /// </summary>
    public class BasicBot : IBot
    {
        // Supported LUIS Intents
        public const string GreetingIntent = "Greeting";
        public const string CancelIntent = "Cancel";
        public const string HelpIntent = "Help";
        public const string NoneIntent = "None";
        public const string BuyIntent = "주식매수";
        public const string SellIntent = "주식매도";
        public const string ModifyIntent = "주식정정";
        public const string BalanceIntent = "주식잔고";
        public const string SkinBalanceIntent = "껍데기_잔고";

        /// <summary>
        /// Key in the bot config (.bot file) for the LUIS instance.
        /// In the .bot file, multiple instances of LUIS can be configured.
        /// </summary>
        public static readonly string LuisConfiguration = "BasicBotLuisApplication";

        private readonly IStatePropertyAccessor<GreetingState> _greetingStateAccessor;
        private readonly IStatePropertyAccessor<DialogState> _dialogStateAccessor;
        private readonly UserState _userState;
        private readonly ConversationState _conversationState;
        private readonly BotServices _services;

        /// <summary>
        /// Initializes a new instance of the <see cref="BasicBot"/> class.
        /// </summary>
        /// <param name="botServices">Bot services.</param>
        /// <param name="accessors">Bot State Accessors.</param>
        public BasicBot(BotServices services, UserState userState, ConversationState conversationState, ILoggerFactory loggerFactory)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            _services = services ?? throw new ArgumentNullException(nameof(services));
            _userState = userState ?? throw new ArgumentNullException(nameof(userState));
            _conversationState = conversationState ?? throw new ArgumentNullException(nameof(conversationState));

            _greetingStateAccessor = _userState.CreateProperty<GreetingState>(nameof(GreetingState));
            _dialogStateAccessor = _conversationState.CreateProperty<DialogState>(nameof(DialogState));

            // Verify LUIS configuration.
            if (!_services.LuisServices.ContainsKey(LuisConfiguration))
            {
                throw new InvalidOperationException($"The bot configuration does not contain a service type of `luis` with the id `{LuisConfiguration}`.");
            }

            Dialogs = new DialogSet(_dialogStateAccessor);
            Dialogs.Add(new GreetingDialog(_greetingStateAccessor, loggerFactory));
        }

        private DialogSet Dialogs { get; set; }

        /// <summary>
        /// Run every turn of the conversation. Handles orchestration of messages.
        /// </summary>
        /// <param name="turnContext">Bot Turn Context.</param>
        /// <param name="cancellationToken">Task CancellationToken.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken)
        {
            var activity = turnContext.Activity;

            // Create a dialog context
            var dc = await Dialogs.CreateContextAsync(turnContext);

            if (activity.Type == ActivityTypes.Message)
            {
                // Perform a call to LUIS to retrieve results for the current activity message.
                var luisResults = await _services.LuisServices[LuisConfiguration].RecognizeAsync(dc.Context, cancellationToken);

                // If any entities were updated, treat as interruption.
                // For example, "no my name is tony" will manifest as an update of the name to be "tony".
                var topScoringIntent = luisResults?.GetTopScoringIntent();

                var topIntent = topScoringIntent.Value.intent;

                // update greeting state with any entities captured
                await UpdateGreetingState(luisResults, dc.Context);

                // Handle conversation interrupts first.
                var interrupted = await IsTurnInterruptedAsync(dc, topIntent);
                if (interrupted)
                {
                    // Bypass the dialog.
                    // Save state before the next turn.
                    await _conversationState.SaveChangesAsync(turnContext);
                    await _userState.SaveChangesAsync(turnContext);
                    return;
                }

                // Continue the current dialog
                var dialogResult = await dc.ContinueDialogAsync();

                // See if LUIS found and used an entity to determine user intent.
                var entityFound = ParseLuisForEntities(luisResults);

                // if no one has responded,
                if (!dc.Context.Responded)
                {
                    // examine results from active dialog
                    switch (dialogResult.Status)
                    {
                        case DialogTurnStatus.Empty:
                            switch (topIntent)
                            {
                                case GreetingIntent:
                                    await dc.BeginDialogAsync(nameof(GreetingDialog));
                                    break;

                                case NoneIntent:
                                default:
                                    // Help or no intent identified, either way, let's provide some help.
                                    // to the user
                                    await dc.Context.SendActivityAsync("I didn't understand what you just said to me.");
                                    break;

                                case BuyIntent:
                                    //await dc.Context.SendActivityAsync(topIntent);

                                    // Inform the user if LUIS used an entity.
                                    if (entityFound.ToString() != string.Empty)
                                    {
                                        string[] cutEntity = entityFound.Split("|SEP|");
                                        //await turnContext.SendActivityAsync($"==>LUIS String: {entityFound}\n");
                                        int count = 0;
                                        bool entityCheck = false;
                                        foreach (var cutEntityValue in cutEntity)
                                        {
                                            if (cutEntityValue != string.Empty)
                                            {
                                                count++;
                                                if (count == 3)
                                                {
                                                    entityCheck = true;
                                                }
                                            }
                                        }

                                        if (entityCheck)
                                        {
                                            
                                            foreach (var cutEntityValue in cutEntity)
                                            {
                                                await turnContext.SendActivityAsync($"==>LUIS Entity: {cutEntityValue}\n");
                                            }
                                            await turnContext.SendActivityAsync($"==>LUIS Entity Found: {entityFound}\n");
                                            /*
                                            // 카드는 html에서 출력
                                            var buyCard = CreateBuyCardAttachment(@".\Dialogs\BuyIntent\Resources\buyCard.json", entityFound);
                                            var response = CreateResponse(activity, buyCard);
                                            await dc.Context.SendActivityAsync(response);
                                            */

                                            // html에 인텐트+엔티티 전달
                                            Activity buyReply = activity.CreateReply();
                                            buyReply.Type = ActivityTypes.Event;
                                            buyReply.Name = "buystock";
                                            buyReply.Value = entityFound.ToString();
                                            await dc.Context.SendActivityAsync(buyReply);
                                        }
                                        else{
                                            await turnContext.SendActivityAsync($"종목, 수량, 단가를 모두 입력해주세요.\n(예시:\"신한지주 1주 현재가로 매수해줘\")");
                                        }
                                    }
                                    else
                                    {
                                        await turnContext.SendActivityAsync($"종목, 수량, 단가를 모두 입력해주세요.\n(예시:\"신한지주 1주 현재가로 매수해줘\")");
                                        
                                    }

                                    

                                    break;

                                case SellIntent:
                                    //await dc.Context.SendActivityAsync(topIntent);

                                    // Inform the user if LUIS used an entity.
                                    if (entityFound.ToString() != string.Empty)
                                    {
                                        string[] cutEntity = entityFound.Split("|SEP|");
                                        //await turnContext.SendActivityAsync($"==>LUIS Count: {cutEntity.Length}\n");
                                        foreach (var cutEntityValue in cutEntity)
                                        {
                                            //await turnContext.SendActivityAsync($"==>LUIS Entity: {cutEntityValue}\n");
                                        }
                                        await turnContext.SendActivityAsync($"==>LUIS Entity Found: {entityFound}\n");
                                        var sellCard = CreateSellCardAttachment(@".\Dialogs\BuyIntent\Resources\buyCard.json", entityFound);
                                        var sell_response = CreateResponse(activity, sellCard);
                                        await dc.Context.SendActivityAsync(sell_response);

                                        // html에 인텐트+엔티티 전달
                                        Activity sellReply = activity.CreateReply();
                                        sellReply.Type = ActivityTypes.Event;
                                        sellReply.Name = "sellstock";
                                        sellReply.Value = entityFound.ToString();
                                        await dc.Context.SendActivityAsync(sellReply);
                                    }
                                    else
                                    {
                                        await turnContext.SendActivityAsync($"종목, 수량, 단가를 모두 입력해주세요.\n(예시:\"신한지주 1주 현재가로 매도해줘\")");
                                    }

                                   
                                    break;

                                case BalanceIntent:
                                    await dc.Context.SendActivityAsync(topIntent);

                                    // html에 인텐트+엔티티 전달
                                    Activity balanceReply = activity.CreateReply();
                                    balanceReply.Type = ActivityTypes.Event;
                                    balanceReply.Name = "balancestock";
                                    balanceReply.Value = entityFound.ToString();
                                    await dc.Context.SendActivityAsync(balanceReply);

                                    break;

                                case SkinBalanceIntent:
                                    // html에 인텐트+엔티티 전달
                                    Activity balanceSkinReply = activity.CreateReply();
                                    balanceSkinReply.Type = ActivityTypes.Event;
                                    balanceSkinReply.Name = "balanceskin";
                                    balanceSkinReply.Value = entityFound.ToString();
                                    await dc.Context.SendActivityAsync(balanceSkinReply);

                                    // 잔고인텐트 일때는 잔고 카드 전달
                                    var balanceCard = CreateWelcomeCardAttachment(@".\Dialogs\BalanceIntent\Resources\balanceCard.json");
                                    var response = CreateResponse(activity, balanceCard);
                                    await dc.Context.SendActivityAsync(response);
                                    break;
                            }

                            break;

                        case DialogTurnStatus.Waiting:
                            // The active dialog is waiting for a response from the user, so do nothing.
                            break;

                        case DialogTurnStatus.Complete:
                            await dc.EndDialogAsync();
                            break;

                        default:
                            await dc.CancelAllDialogsAsync();
                            break;
                    }
                }
            }
            else if (activity.Type == ActivityTypes.ConversationUpdate)
            {
                if (activity.MembersAdded != null)
                {
                    // Iterate over all new members added to the conversation.
                    foreach (var member in activity.MembersAdded)
                    {
                        // Greet anyone that was not the target (recipient) of this message.
                        // To learn more about Adaptive Cards, see https://aka.ms/msbot-adaptivecards for more details.
                        if (member.Id == activity.Recipient.Id)
                        {
                            var welcomeCard = CreateWelcomeCardAttachment(@".\Dialogs\Welcome\Resources\welcomeCard.json");
                            var response = CreateResponse(activity, welcomeCard);
                            await dc.Context.SendActivityAsync(response);
                        }
                    }
                }
            }

            await _conversationState.SaveChangesAsync(turnContext);
            await _userState.SaveChangesAsync(turnContext);
        }

        // Determine if an interruption has occurred before we dispatch to any active dialog.
        private async Task<bool> IsTurnInterruptedAsync(DialogContext dc, string topIntent)
        {
            // See if there are any conversation interrupts we need to handle.
            if (topIntent.Equals(CancelIntent))
            {
                if (dc.ActiveDialog != null)
                {
                    await dc.CancelAllDialogsAsync();
                    await dc.Context.SendActivityAsync("Ok. I've canceled our last activity.");
                }
                else
                {
                    await dc.Context.SendActivityAsync("I don't have anything to cancel.");
                }

                return true;        // Handled the interrupt.
            }

            if (topIntent.Equals(HelpIntent))
            {
                await dc.Context.SendActivityAsync("Let me try to provide some help.");
                await dc.Context.SendActivityAsync("I understand greetings, being asked for help, or being asked to cancel what I am doing.");
                if (dc.ActiveDialog != null)
                {
                    await dc.RepromptDialogAsync();
                }

                return true;        // Handled the interrupt.
            }

            return false;           // Did not handle the interrupt.
        }

        // Create an attachment message response.
        private Activity CreateResponse(Activity activity, Attachment attachment)
        {
            var response = activity.CreateReply();
            response.Attachments = new List<Attachment>() { attachment };
            return response;
        }

        // Load attachment from file.
        private Attachment CreateWelcomeCardAttachment(string JsonDirectory)
        {
            var WelcomeCard = File.ReadAllText(JsonDirectory, Encoding.GetEncoding(51949));
            return new Attachment()
            {
                ContentType = "application/vnd.microsoft.card.adaptive",
                Content = JsonConvert.DeserializeObject(WelcomeCard),
            };
        }

        private Attachment CreateBuyCardAttachment(string jsonDirectory, string entity)
        {
            var adaptiveCard = File.ReadAllText(jsonDirectory, Encoding.GetEncoding(51949));        // 51949: euc-kr
            System.Text.Encoding euckr = System.Text.Encoding.GetEncoding(51949);

            var json = JObject.Parse(adaptiveCard);
            var jsonContext = new JObject();
            var jsonButton1 = new JObject();
            var jsonButton2 = new JObject();
            var actions = new JArray();
            var body = new JArray();
       
            string text = string.Empty;
            string titleButton1 = "계좌 변경하기";
            string titleButton2 = "이대로 매수하기";
            string urlButton1 = "\"ns://webpop.shinhaninvest.com?data=";
            string urlButton2 = "\"ns://webpop.shinhaninvest.com?data=";
            string price = string.Empty;
            if (entity.ToString() != string.Empty)
            {
                string[] arr_Entity = entity.Split("|SEP|");    // 수량, 종목, 가격
                if (!arr_Entity[1].Equals("nostock"))
                {
                    text = text + arr_Entity[1]+" ";
                }

                if (!arr_Entity[0].Equals("noquantity"))
                {
                    text = text + arr_Entity[0] + "주 ";
                }

                if (!arr_Entity[2].Equals("noprice"))
                {
                    price = arr_Entity[2];
                    if (price.Contains("원"))
                    {
                        price = price.Replace("원", "");
                    }
                    else if (price.Contains("시장가"))
                    {
                        price = price.Replace("시장가", "mp");
                    }
                    else if (price.Contains("현재가"))
                    {
                        price = price.Replace("현재가", "cp");
                    }
                    else if (price.Contains("하한가"))
                    {
                        price = price.Replace("하한가", "lp");
                    }
                    else if (price.Contains("상한가"))
                    {
                        price = price.Replace("상한가", "hp");
                    }
                    else if (price.Contains("시간외단일가"))
                    {
                        price = price.Replace("시간외단일가", "tp");
                    }

                    text = text + arr_Entity[2];
                }

                text += " 매수하시겠어요?";
                urlButton2 = arr_Entity[0] + "|SEP|" + arr_Entity[1] + "|SEP|" + price + "|SEP|";
            }
            urlButton1 += "&isPop=Y&path=naev850003\"";
            urlButton2 += "&isPop=Y&path=naev850003\"";

            // 카드본문 세팅
            jsonContext.Add("type", "TextBlock");
            jsonContext.Add("size", "default");
            jsonContext.Add("wrap", true);
            jsonContext.Add("maxLines", 0);
            jsonContext.Add("text", euckr.GetString(euckr.GetBytes(text)));

            // 버튼1 세팅
            jsonButton1.Add("type", "Action.OpenUrl");
            jsonButton1.Add("title", euckr.GetString(euckr.GetBytes(titleButton1)));
            jsonButton1.Add("url", urlButton1);

            // 버튼2 세팅
            jsonButton2.Add("type", "Action.OpenUrl");
            jsonButton2.Add("title", euckr.GetString(euckr.GetBytes(titleButton2)));
            jsonButton2.Add("url", urlButton2);

            // 카드본문, 버튼들 추가
            body.Add(jsonContext);
            actions.Add(jsonButton1);
            actions.Add(jsonButton2);

            json.Add("body", body);
            json.Add("actions", actions);
            adaptiveCard = json.ToString();

            return new Attachment()
            {
                ContentType = "application/vnd.microsoft.card.adaptive",
                Content = JsonConvert.DeserializeObject(adaptiveCard),
            };
        }

        private Attachment CreateSellCardAttachment(string jsonDirectory, string entity)
        {
            var adaptiveCard = File.ReadAllText(jsonDirectory, Encoding.GetEncoding(51949));        // 51949: euc-kr
            System.Text.Encoding euckr = System.Text.Encoding.GetEncoding(51949);

            var json = JObject.Parse(adaptiveCard);
            var jsonContext = new JObject();
            var jsonButton1 = new JObject();
            var jsonButton2 = new JObject();
            var actions = new JArray();
            var body = new JArray();

            string text = string.Empty;
            string titleButton1 = "계좌 변경하기";
            string titleButton2 = "이대로 매도하기";
            string urlButton1 = "\"ns://webpop.shinhaninvest.com?data=";
            string urlButton2 = "\"ns://webpop.shinhaninvest.com?data=";
            string price = string.Empty;
            if (entity.ToString() != string.Empty)
            {
                string[] arr_Entity = entity.Split("|SEP|");    // 수량, 종목, 가격
                if (!arr_Entity[1].Equals("nostock"))
                {
                    text = text + arr_Entity[1] + " ";
                }

                if (!arr_Entity[0].Equals("noquantity"))
                {
                    text = text + arr_Entity[0] + "주 ";
                }

                if (!arr_Entity[2].Equals("noprice"))
                {
                    price = arr_Entity[2];
                    if (price.Contains("원"))
                    {
                        price = price.Replace("원", "");
                    }
                    else if (price.Contains("시장가"))
                    {
                        price = price.Replace("시장가", "mp");
                    }
                    else if (price.Contains("현재가"))
                    {
                        price = price.Replace("현재가", "cp");
                    }
                    else if (price.Contains("하한가"))
                    {
                        price = price.Replace("하한가", "lp");
                    }
                    else if (price.Contains("상한가"))
                    {
                        price = price.Replace("상한가", "hp");
                    }
                    else if (price.Contains("시간외단일가"))
                    {
                        price = price.Replace("시간외단일가", "tp");
                    }

                    text = text + arr_Entity[2];
                }

                text += " 매도하시겠어요?";
                urlButton2 = arr_Entity[0] + "|SEP|" + arr_Entity[1] + "|SEP|" + price + "|SEP|";
            }

            // 카드본문 세팅
            jsonContext.Add("type", "TextBlock");
            jsonContext.Add("size", "default");
            jsonContext.Add("wrap", true);
            jsonContext.Add("maxLines", 0);
            jsonContext.Add("text", euckr.GetString(euckr.GetBytes(text)));

            // 버튼1 세팅
            jsonButton1.Add("type", "Action.OpenUrl");
            jsonButton1.Add("title", euckr.GetString(euckr.GetBytes(titleButton1)));
            jsonButton1.Add("url", urlButton1);

            // 버튼2 세팅
            jsonButton2.Add("type", "Action.OpenUrl");
            jsonButton2.Add("title", euckr.GetString(euckr.GetBytes(titleButton2)));
            jsonButton2.Add("url", urlButton2);

            // 카드본문, 버튼들 추가
            body.Add(jsonContext);
            actions.Add(jsonButton1);
            actions.Add(jsonButton2);

            json.Add("body", body);
            json.Add("actions", actions);
            adaptiveCard = json.ToString();

            return new Attachment()
            {
                ContentType = "application/vnd.microsoft.card.adaptive",
                Content = JsonConvert.DeserializeObject(adaptiveCard),
            };
        }

        /// <summary>
        /// Helper function to update greeting state with entities returned by LUIS.
        /// </summary>
        /// <param name="luisResult">LUIS recognizer <see cref="RecognizerResult"/>.</param>
        /// <param name="turnContext">A <see cref="ITurnContext"/> containing all the data needed
        /// for processing this conversation turn.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        private async Task UpdateGreetingState(RecognizerResult luisResult, ITurnContext turnContext)
        {
            if (luisResult.Entities != null && luisResult.Entities.HasValues)
            {
                // Get latest GreetingState
                var greetingState = await _greetingStateAccessor.GetAsync(turnContext, () => new GreetingState());
                var entities = luisResult.Entities;

                // Supported LUIS Entities
                string[] userNameEntities = { "userName", "userName_patternAny" };
                string[] userLocationEntities = { "userLocation", "userLocation_patternAny" };

                // Update any entities
                // Note: Consider a confirm dialog, instead of just updating.
                foreach (var name in userNameEntities)
                {
                    // Check if we found valid slot values in entities returned from LUIS.
                    if (entities[name] != null)
                    {
                        // Capitalize and set new user name.
                        var newName = (string)entities[name][0];
                        greetingState.Name = char.ToUpper(newName[0]) + newName.Substring(1);
                        break;
                    }
                }

                foreach (var city in userLocationEntities)
                {
                    if (entities[city] != null)
                    {
                        // Capitalize and set new city.
                        var newCity = (string)entities[city][0];
                        greetingState.City = char.ToUpper(newCity[0]) + newCity.Substring(1);
                        break;
                    }
                }

                // Set the new values into state.
                await _greetingStateAccessor.SetAsync(turnContext, greetingState);
            }
        }

        private string ParseLuisForEntities(RecognizerResult recognizerResult)
        {
            var result = string.Empty;

            // recognizerResult.Entities returns type JObject.
            foreach (var entity in recognizerResult.Entities)
            {
                // Parse JObject for a known entity types: Appointment, Meeting, and Schedule.
                var stockPrice = JObject.Parse(entity.Value.ToString())["단가"];
                var stockQuantity = JObject.Parse(entity.Value.ToString())["수량"];
                var stockName = JObject.Parse(entity.Value.ToString())["종목"];
                
                // use JsonConvert to convert entity.Value to a dynamic object.
                dynamic o = JsonConvert.DeserializeObject<dynamic>(entity.Value.ToString());

                // We will return info on the first entity found.
                if (stockQuantity != null)
                {
                    if (o.수량[0] != null)
                    {
                        string tempQ = o.수량[0].text;
                        if (tempQ.Contains("주"))
                        {
                            tempQ = tempQ.Replace("주", "");
                        }
                        else if (tempQ.Contains("개"))
                        {
                            tempQ = tempQ.Replace("개", "");
                        }
                        result += tempQ;
                        result += "|SEP|";
                    }
                }
                else
                {
                    //result += "noquantity"; //엔티티 제대로 안들어오면 값 안넘겨줌
                    result += "|SEP|";
                }

                if (stockName != null)
                {
                    if (o.종목[0] != null)
                    {
                        string tempN = o.종목[0].text;
                        tempN = tempN.Replace(" ", "");
                        result += tempN;
                        result += "|SEP|";
                    }
                }
                else
                {
                    //result += "nostock";//엔티티 제대로 안들어오면 값 안넘겨줌
                    result += "|SEP|";
                }

                if (stockPrice != null)
                {
                    if (o.단가[0] != null)
                    {
                        string tempQ = o.단가[0].text;
                        result += tempQ;
                        result += "|SEP|";
                    }
                }
                else
                {
                    //result += "noprice";//엔티티 제대로 안들어오면 값 안넘겨줌
                    result += "|SEP|";
                }

                //문자열 끝 구분자(|SEP|) 5자리 제거
                result = result.Substring(0, result.Length - 5);
                return result;
            }
            // No entities were found, empty string returned.
            //문자열 끝 구분자(|SEP|) 5자리 제거
            result = result.Substring(0, result.Length - 5);
            return result;
        }
    }
}
