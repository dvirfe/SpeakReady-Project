// Test-only local server: replaces the paid provider, never deploy this project.
using SpeakReady.Tests;
using var factory = new PilotFactory();
factory.UseKestrel(5214);
using var client = factory.CreateClient();
Console.WriteLine("Browser test API listening on http://localhost:5214 with synthetic AI responses.");
await Task.Delay(Timeout.Infinite);
