using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using OpenAI.Responses;
using System;
using System.Collections.Generic;
using System.Text;

namespace PromptAgentViaCode
{
    internal class ChatWithAgent
    {
        public void Chat()
        {
            var ProjectEndpoint = Environment.GetEnvironmentVariable("FoundryProjectName");
            var AgentName = "PromptAgent";

            // Create project client to call Foundry API
            AIProjectClient projectClient = new(
                endpoint: new Uri(ProjectEndpoint),
                tokenProvider: new DefaultAzureCredential());

            // Create a conversation for multi-turn chat
            ProjectConversation conversation = projectClient.ProjectOpenAIClient.GetProjectConversationsClient()
                .CreateProjectConversation();

            // Chat with the agent to answer questions
            ProjectResponsesClient responsesClient =
                projectClient.ProjectOpenAIClient.GetProjectResponsesClientForAgent(
                    defaultAgent: AgentName,
                    defaultConversationId: conversation.Id);
            var response = responsesClient.CreateResponse("Türkiyenin nüfusu nedir?");
            Console.WriteLine(response.Value.GetOutputText());

            // Ask a follow-up question in the same conversation
            var response2 = responsesClient.CreateResponse("Ve başkenti neresidir?");
            Console.WriteLine(response2.Value.GetOutputText());
        }
    }
}
