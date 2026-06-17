using Azure.AI.Projects;
using Azure.Identity;


Console.WriteLine("Deploying hosted agent...");
// Microsoft Foundry'ye KOD ile hosted agent deploy eden örnek.
// Python örneğinin (project.agents.create_version + HostedAgentDefinition) C# karşılığı.
// Bkz: https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/deploy-hosted-agent

// Format: "https://resource_name.services.ai.azure.com/api/projects/project_name"
// Ortam değişkeninden okunur; yoksa aşağıdaki placeholder kullanılır.
// var projectEndpoint =
//     Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")
//     ?? "your_project_endpoint";
//
// // ACR imajı (agent.yaml'daki image ile aynı olmalı).
// var containerImage =
//     Environment.GetEnvironmentVariable("AGENT_CONTAINER_IMAGE")
//     ?? "educationfoundry.azurecr.io/deployfoundrycustomeragent:latest";
//
// // Model deployment adı.
// var modelDeploymentName =
//     Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME")
//     ?? "gpt-5-mini";
//
// // Agent adı.
// var agentName =
//     Environment.GetEnvironmentVariable("AGENT_NAME")
//     ?? "deployfoundrycustomeragent";
//
// // Project client oluştur (managed identity / az login -> DefaultAzureCredential).
// var credential = new DefaultAzureCredential();
// var project = new AIProjectClient(new Uri(projectEndpoint), credential);
//
// // Hosted agent tanımı: responses protokolü, 1 cpu / 2Gi, container imajı ve env var.
// var definition = new HostedAgentDefinition
// {
//     ProtocolVersions =
//     {
//         new ProtocolVersionRecord(AgentProtocol.Responses, "1.0.0")
//     },
//     Cpu = "1",
//     Memory = "2Gi",
//     ContainerConfiguration = new ContainerConfiguration(containerImage),
//     EnvironmentVariables =
//     {
//         ["MODEL_DEPLOYMENT_NAME"] = modelDeploymentName
//     }
// };
//
// // Yeni bir hosted agent versiyonu oluştur (immutable).
// var agent = await project.Agents.CreateVersionAsync(
//     agentName: agentName,
//     definition: definition);
//
// Console.WriteLine($"Agent created: {agent.Value.Name}, version: {agent.Value.Version}");