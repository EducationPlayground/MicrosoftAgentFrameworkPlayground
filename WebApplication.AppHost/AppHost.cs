var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.WebApplication_API>("webapplication-api");

builder.AddProject<Projects.WebApplication_Web>("webapplication-web");

builder.Build().Run();
