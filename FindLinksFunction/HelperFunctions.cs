/*
 * LambdaSharp (λ#)
 * Copyright (C) 2018-2019
 * lambdasharp.net
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
using System.Text.RegularExpressions;
using System.Linq;

namespace Challenge.TheWikiGame {

    internal static class HelperFunctions {

        //--- Class Fields ---
        private static Regex aTagRegex = new Regex("(<a.*?>.*?</a>)", RegexOptions.Singleline | RegexOptions.Compiled);
        private static Regex hrefRegex = new Regex("href=\"(.*?)\"", RegexOptions.Singleline | RegexOptions.Compiled);

        //--- Class Methods ---
        public static IEnumerable<string> FindLinks(string html) {
            var list = new List<string>();
            foreach(Match addressElementMatch in aTagRegex.Matches(html)) {
                var addressElementAttributes = addressElementMatch.Groups[0].Value;
                var hrefAttributeMatch = hrefRegex.Match(addressElementAttributes);
                if(hrefAttributeMatch.Success) {
                    var href = hrefAttributeMatch.Groups[1].Value;
                    list.Add(href);
                }
            }
            return list;
        }

        public static string FixInternalLink(string link, Uri origin) {
            if(link.StartsWith("//")) {
                link = $"{origin.Scheme}:{link}";
            } else if(link.StartsWith("/")) {
                link = $"{origin.Scheme}://{origin.Host}{link}";
            }
            return link;
        }
    }
}