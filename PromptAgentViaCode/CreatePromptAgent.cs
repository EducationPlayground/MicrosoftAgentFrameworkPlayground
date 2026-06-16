using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Identity;
using System;
using System.Collections.Generic;
using System.Text;

namespace PromptAgentViaCode
{
    internal class CreatePromptAgent
    {
        public void Create()
        {
            var ProjectEndpoint = "https://education-test-resource.services.ai.azure.com/api/projects/education";
            var AgentName = "PromptAgent";

            // Create project client to call Foundry API
            AIProjectClient projectClient = new(
                endpoint: new Uri(ProjectEndpoint),
                tokenProvider: new DefaultAzureCredential());

            // Create an agent with a model and instructions
            ProjectsAgentDefinition agentDefinition =
                new DeclarativeAgentDefinition("gpt-5-mini") // supports all Foundry direct models
                {
                    Instructions = "You are a helpful assistant that answers general questions",
                };

            ProjectsAgentVersion agent = projectClient.AgentAdministrationClient.CreateAgentVersion(
                AgentName,
                options: new(agentDefinition));
            Console.WriteLine($"Agent created (id: {agent.Id}, name: {agent.Name}, version: {agent.Version})");
        }
    }
}
