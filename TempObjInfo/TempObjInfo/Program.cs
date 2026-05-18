using System;
using System.Reflection;
using System.Linq;
using Microsoft.Agents.AI;
class Program { static void Main() { 
    foreach(var m in typeof(AIAgent).GetMethods().Where(m => m.Name.Contains("Session"))) {
        Console.WriteLine(m.Name + " " + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)));
    }
} }
