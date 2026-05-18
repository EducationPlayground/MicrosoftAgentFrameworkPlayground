using System;
using System.Reflection;
using Microsoft.Agents.AI;
class Program { static void Main() { 
    foreach(var p in typeof(AgentSession).GetProperties()) {
        Console.WriteLine(p.Name);
    }
} }
