using System.Collections.Generic;
using Amazon.Lambda.Core;

namespace LambdaSharp.Challenge.TheWikiGame {

    public class Route {

        //--- Properties ---

        // ID = $"{fromUrl}::{toUrl}"
        public string ID { get; set; }
        public List<string> Path { get; set; } = new List<string>();
    }

    public class Article {

        //--- Properties ---

        // ID = $"{articleUrl}"
        public string ID { get; set; }
        public List<string> Links { get; set; } = new List<string>();
    }

    public class Message {

        //--- Properties ---
        public string Origin { get; set; }
        public string Target { get; set; }
        public int Depth { get; set; }
        public List<string> Path { get; set; } = new List<string>();
    }
}
