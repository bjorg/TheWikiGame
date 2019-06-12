/*
 * MindTouch λ#
 * Copyright (C) 2018-2019 MindTouch, Inc.
 * www.mindtouch.com  oss@mindtouch.com
 *
 * For community documentation and downloads visit mindtouch.com;
 * please review the licensing section.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

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
            _queueUrl = config.ReadSqsQueueUrl("AnalyzeQueue");
            _table = Table.LoadTable(_dynamoDbClient, config.ReadDynamoDBTableName("LinksTable"));
            _topic = config.ReadText("NotifyFoundTopic");
        }

        public override async Task ProcessMessageAsync(Message message) {

            // validate message
            if(string.IsNullOrEmpty(message.Origin)) {
                LogInfo("empty ORIGIN field");
                return;
            }
            if(string.IsNullOrEmpty(message.Target)) {
                LogInfo("empty TARGET field");
                return;
            }

            // check if route has already been solved
            var route = await GetRecord<Route>($"{message.Origin}::{message.Target}");
            if(route != null) {
                LogInfo($"CACHED => {string.Join(" -> ", route.Path)}");

                // check if this was a seed message
                if(!message.Path.Any()) {
                    await FoundRoute(new Message {
                        Origin = message.Origin,
                        Target = message.Target,
                        Path = route.Path
                    });
                }
                return;
            }

            // check if origin link is already in the path
            if(!message.Path.Any()) {
                message.Path.Add(message.Origin);
            }

            // check if destination was found
            var current = message.Path.Last();
            if(current == message.Target) {
                await FoundRoute(message);
                return;
            }

            // stop processing because we have reached the maximum depth
            if(message.Depth <= 0) {
                LogInfo($"STOP => ignoring URL '{current}' because we have reached the maximum depth");
                return;
            }

            // check if article has already been parsed
            var article = await GetRecord<Article>(current);
            if(article == null) {
                article = new Article {
                    ID = current
                };

                // attempt to parse article
                try {

                    // get article contents of wikipedia article
                    var response = await HttpClient.GetAsync(current);
                    var html = await response.Content.ReadAsStringAsync();

                    // go over all links in article
                    var uri = new Uri(current);
                    var foundLinks = new HashSet<string>();
                    foreach(var link in HelperFunctions.FindLinks(html)) {

                        // format internal links and attempt to parse for validity
                        Uri articleLink = null;
                        try {
                            articleLink = new Uri(HelperFunctions.FixInternalLink(link, uri));
                        } catch {
                            continue;
                        }

                        // ignore external links
                        if(!articleLink.Host.Equals(uri.Host)) {
                            continue;
                        }

                        // check if link is a "Category:" or "File:" link
                        if(articleLink.AbsolutePath.Contains(":")) {
                            continue;
                        }

                        // remove query and fragment when present
                        if((articleLink.Fragment != "") || (articleLink.Query != "")) {
                            articleLink = new UriBuilder(articleLink) {
                                Fragment = "",
                                Query = ""
                            }.Uri;
                        }

                        // add new article link
                        foundLinks.Add(articleLink.ToString());
                    }
                    article.Links.AddRange(foundLinks);
                } catch(Exception e) {

                    // store article without links so we can move one and don't repeat this error
                    LogErrorAsWarning(e);
                }
                await PutRecord(article);

                // check if the article we analyzed contains the destination
                if(article.Links.Contains(message.Target)) {
                    await FoundRoute(new Message {
                        Origin = message.Origin,
                        Target = message.Target,
                        Path = message.Path.Append(message.Target).ToList()
                    });

                    // exit without exploring more links
                    return;
                }
            }

            // submit all links for analysis
            await Task.WhenAll(article.Links.Select(link => _sqsClient.SendMessageAsync(new SendMessageRequest {
                QueueUrl = _queueUrl,
                MessageBody = SerializeJson(new Message {
                    Origin = message.Origin,
                    Target = message.Target,
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
