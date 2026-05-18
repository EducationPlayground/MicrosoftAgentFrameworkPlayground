using System;
using Microsoft.Agents.AI;
class Program { static void Main() { 
    var session = new AgentSession("my-id");
    Console.WriteLine(session.Id);
} }
