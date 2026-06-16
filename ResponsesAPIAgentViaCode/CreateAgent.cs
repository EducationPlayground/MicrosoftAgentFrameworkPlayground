using System;
using System.Collections.Generic;
using System.Text;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;

namespace ResponsesAPIAgentViaCode
{
    internal class CreateAgent
    {
        public async Task Create()
        {



            var ProjectEndpoint = Environment.GetEnvironmentVariable("Foundry_Project_Name");


            var deploymentName = "gpt-5-mini"; // supports all Foundry direct models

            AIAgent agent =
                new AIProjectClient(new Uri(ProjectEndpoint), new DefaultAzureCredential())
                    .AsAIAgent(
                        model: deploymentName,
                        instructions: "You are a helpful assistant.",
                        name: "Assistant");

            Console.WriteLine($"Agent: {await agent.RunAsync("Türkiyenin başkenti hangi şehirdir")}");
        }
    }
}
