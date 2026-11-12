var builder = DistributedApplication.CreateBuilder(args);

// The Functions host runs through the Core Tools (`func start`) so no Docker /
// Azurite is needed: every trigger is HTTP, hence no storage account is required.
// `func` is an npm shim on Windows, so it is launched through the shell.
var functionsDirectory = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "HockeySeason.Functions"));
const int functionsPort = 7071;

var functions = (OperatingSystem.IsWindows()
        ? builder.AddExecutable("functions", "cmd", functionsDirectory, "/c", "func", "start", "--port", functionsPort.ToString())
        : builder.AddExecutable("functions", "func", functionsDirectory, "start", "--port", functionsPort.ToString()))
    .WithHttpEndpoint(port: functionsPort, isProxied: false)
    .WithEnvironment("AZURE_FUNCTIONS_ENVIRONMENT", "Development")
    .WithEnvironment("FUNCTIONS_WORKER_RUNTIME", "dotnet-isolated")
    .WithHttpHealthCheck("/api/healthz");

builder.AddJavaScriptApp("frontend", "../../frontend", "start")
    .WithNpm(install: false)
    .WithEnvironment("API_BACKEND_URL", functions.GetEndpoint("http"))
    .WithHttpEndpoint(port: 6767, targetPort: 6767, env: "PORT", isProxied: false)
    .WaitFor(functions);

builder.Build().Run();
