var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.WebApplication_API>("webapplication-api");

builder.AddProject<Projects.WebApplication_RazorPages>("webapplication-razorpages");

builder.Build().Run();
