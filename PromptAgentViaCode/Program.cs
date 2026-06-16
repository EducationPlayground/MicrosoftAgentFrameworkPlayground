using Azure.Identity;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using PromptAgentViaCode;

Console.WriteLine("Prompt Agent");


//CreatePromptAgent createAgent = new CreatePromptAgent();
//createAgent.Create();

ChatWithAgent agent = new ChatWithAgent();

agent.Chat();

