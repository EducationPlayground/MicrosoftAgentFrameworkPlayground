using System;
using System.Reflection;
using Microsoft.Agents.AI;
class Program { static void Main() { 
    var methods = typeof(AIAgent).GetMethods();
    foreach(var m in methods) {
        if(m.Name == "CreateSessionAsync") {
            Console.WriteLine(m.Name + " " + string.Join(", ", Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name + " " + p.Name)));
        }
    }
} }
