var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.WebApplication_API>("webapplication-api");

builder.AddProject<Projects.WebApplication_RazorPages>("webapplication-razorpages")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
