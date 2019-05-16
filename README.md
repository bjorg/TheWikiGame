# λ# Challenge - The Wiki Game

This is a solution to the [λ# hackathon challenge](https://www.meetup.com/lambdasharp/) from May 16th, 2019.

## Overview

This challenge is based around beating the [Wiki Game](https://www.thewikigame.com/). The premise is, given two wikipedia articles, go from one article to the other only clicking the internal links available on the articles.

For example:
> Origin = Apple
>
> Target = Earth
>
> Path = Apple -> Fruit -> Seed -> Earth.

## Solution

For this solution, we decided to go with an SQS queue to scale out our Lambda function to analyze Wikipedia articles. When the Lambda function is invoked, it checks if the current article is the target. If not, it checks is the article has already been analyzed. If not, it fetches the article from Wikipedia and extracts all the links from it. The found links are then submitted back into the SQS queue for analysis until the target article is found.

The solution uses the following AWS resources:
* [DynamoDB Table](https://aws.amazon.com/dynamodb/) for storing analyzed articles and the found solution.
* [SQS Queue](https://aws.amazon.com/sqs/) for submitting new articles to analyze.
* [Lambda Function](https://aws.amazon.com/lambda/) will hold all the business logic.
* [SNS Topic](https://aws.amazon.com/sns/) for sending out a notification of a found solution.

To kick off a search, submit a JSON to the SQS queue specifying the origin and target Wikipedia articles.

```json
{
  "Origin": "https://en.wikipedia.org/wiki/Apple",
  "Target": "https://en.wikipedia.org/wiki/Earth",
  "Depth": 3
}
```

To receive a notification about the found path, subscribe to the SNS topic created by the module.

## Setup

* [Sign up for AWS account](https://aws.amazon.com/)
* [Install .Net 2.1 or later](https://dotnet.microsoft.com/download/dotnet-core/2.1)
* [Install AWS CLI](https://aws.amazon.com/cli/)
* [Install the λ# Tool](https://github.com/LambdaSharp/LambdaSharpTool)

### Clone the GitHub Repository

```
git clone git@github.com:bjorg/TheWikiGame.git
```

### Deploy Solution
NOTE: If you need to setup λ# before you can deploy. See the [λ# website](https://lambdasharp.net) for instructions.

```
cd TheWikiGame
lash deploy
```

