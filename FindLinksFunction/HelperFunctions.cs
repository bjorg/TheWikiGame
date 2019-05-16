using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;

namespace LambdaSharp.Challenge.TheWikiGame {

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