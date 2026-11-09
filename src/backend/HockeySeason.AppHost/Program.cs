var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.HockeySeason_Api>("api")
    .WithEnvironment("PORT", "")
    .WithHttpHealthCheck("/readyz");

builder.AddJavaScriptApp("frontend", "../../frontend", "start")
    .WithNpm(install: false)
    .WithEnvironment("API_BACKEND_URL", api.GetEndpoint("http"))
    .WithHttpEndpoint(port: 4200, targetPort: 4200, env: "PORT", isProxied: false)
    .WaitFor(api);

builder.Build().Run();
