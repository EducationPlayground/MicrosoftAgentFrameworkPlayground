using ResponsesAPIAgentViaCode;

Console.WriteLine("Hello, World!");

CreateAgent createAgent = new CreateAgent();
createAgent.Create().Wait();
