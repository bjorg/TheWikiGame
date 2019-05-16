using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Amazon.Lambda.DynamoDBEvents;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.SQS;
using Amazon.SQS.Model;
using LambdaSharp;
using LambdaSharp.SimpleQueueService;
using Newtonsoft.Json;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace LambdaSharp.Challenge.TheWikiGame {

    public class Function : ALambdaQueueFunction<Message> {

        //--- Class Fields ---
        private static HttpClient _httpClient = new HttpClient();

        //--- Fields ---
        private IAmazonDynamoDB _dynamoDbClient;
        private IAmazonSQS _sqsClient;
        private IAmazonSimpleNotificationService _snsClient;
        private string _queueUrl;
        private string _topic;
        private Table _table;

        //--- Methods ---
        public override async Task InitializeAsync(LambdaConfig config) {

            // initialize AWS clients
            _dynamoDbClient = new AmazonDynamoDBClient();
            _sqsClient = new AmazonSQSClient();
            _snsClient = new AmazonSimpleNotificationServiceClient();

            // read settings
            _queueUrl = config.ReadSqsQueueUrl("AnalyzePageQueue");
            _table = Table.LoadTable(_dynamoDbClient, config.ReadDynamoDBTableName("LinksTable"));
            _topic = config.ReadText("NotifyFoundTopic");
        }

        public override async Task ProcessMessageAsync(Message message) {

            // validate message
            if(string.IsNullOrEmpty(message.Origin)) {
                LogInfo("empty FROM field");
                return;
            }
            if(string.IsNullOrEmpty(message.Target)) {
                LogInfo("empty TO field");
                return;
            }

            // check if current URL is set
            if(message.Current == null) {
                message.Current = message.Origin;
            }

            // check if origin link is already in the path
            if(message.Path.Count == 0) {
                message.Path.Add(message.Origin);
            }

            // check if route has already been solved
            var route = await GetRecord<Route>($"{message.Origin}::{message.Target}");
            if(route != null) {
                LogInfo($"CACHED => {string.Join(" -> ", route.Path)}");
                return;
            }

            // check if destination was found
            if(message.Current == message.Target) {
                await FoundRoute(message);
                return;
            }

            // stop processing because we have reached the maximum depth
            if(message.Depth <= 0) {
                LogInfo($"STOP => ignoring URL '{message.Current}' because we have reached the maximum depth");
                return;
            }

            // check if page has already been parsed
            var page = await GetRecord<Page>(message.Current);
            if(page == null) {
                page = new Page {
                    ID = message.Current
                };

                // attempt to parse page
                try {

                    // get page contents of wikipedia article
                    var response = await _httpClient.GetAsync(message.Current);
                    var html = await response.Content.ReadAsStringAsync();

                    // go over all links in page
                    var uri = new Uri(message.Current);
                    var foundLinks = new HashSet<string>();
                    foreach(var link in HelperFunctions.FindLinks(html)) {

                        // format internal links and attempt to parse for validity
                        Uri pageLink = null;
                        try {
                            pageLink = new Uri(HelperFunctions.FixInternalLink(link, uri));
                        } catch {
                            continue;
                        }

                        // ignore external links
                        if(!pageLink.Host.Equals(uri.Host)) {
                            continue;
                        }

                        // check if link is a "Category:" or "File:" link
                        if(pageLink.AbsolutePath.Contains(":")) {
                            continue;
                        }

                        // remove query and fragment when present
                        if((pageLink.Fragment != "") || (pageLink.Query != "")) {
                            pageLink = new UriBuilder(pageLink) {
                                Fragment = "",
                                Query = ""
                            }.Uri;
                        }

                        // and new page link
                        foundLinks.Add(pageLink.ToString());
                    }
                    page.Links.AddRange(foundLinks);
                } catch(Exception e) {

                    // store page without links so we can move one and don't repeat this error
                    LogErrorAsWarning(e);
                }
                await PutRecord(page);

                // check if the page we analyzed contains the destination
                if(page.Links.Contains(message.Target)) {
                    await FoundRoute(new Message {
                        Origin = message.Origin,
                        Target = message.Target,
                        Current = message.Target,
                        Path = message.Path.Append(message.Target).ToList()
                    });

                    // exit without exploring more links
                    return;
                }
            }

            // submit all links for analysis
            await Task.WhenAll(page.Links.Select(link => _sqsClient.SendMessageAsync(new SendMessageRequest {
                QueueUrl = _queueUrl,
                MessageBody = SerializeJson(new Message {
                    Origin = message.Origin,
                    Target = message.Target,
                    Current = link,
                    Depth = message.Depth - 1,
                    Path = message.Path.Append(link).ToList()
                })})
            ));
        }

        private async Task<T> GetRecord<T>(string id) {
            var record = await _table.GetItemAsync(id);
            return (record == null)
                ? default(T)
                : DeserializeJson<T>(record.ToJson());
        }

        private async Task PutRecord<T>(T record)
            => await _table.PutItemAsync(Document.FromJson(SerializeJson(record)));

        private Task FoundRoute(Message message) {
            var text = $"FOUND => {string.Join(" -> ", message.Path)}";
            LogInfo(text);
            return Task.WhenAll(new Task[] {

                // write solution record
                PutRecord(new Route {
                    ID = $"{message.Origin}::{message.Target}",
                    Path = message.Path
                }),

                // send solution notification
                _snsClient.PublishAsync(_topic, text)
            });
        }
    }
}
