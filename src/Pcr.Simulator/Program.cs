using System.Diagnostics;
using System.Text.Json;
using Pcr.Core;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Pcr.Simulator scenario.json [--php php] [--runtime php/runtime.php] [--output result.json]");
    return 2;
}
var workers = new List<Worker>();
try
{
    string Option(string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i < 0 ? fallback : i + 1 < args.Length ? args[i + 1] : throw new ArgumentException("Missing " + name);
    }
    var path = Path.GetFullPath(args[0]);
    var scenario = Json.Load(path);
    if (scenario.MaxTicks < 1) throw new ArgumentException("maxTicks must be positive");
    var sim = new Simulation(scenario);
    var runtime = Path.GetFullPath(Option("--runtime", "php/runtime.php"));
    foreach (var robot in scenario.Robots.OrderBy(r => r.Id, StringComparer.Ordinal))
    {
        var program = Path.GetFullPath(robot.Program, Path.GetDirectoryName(path)!);
        workers.Add(new Worker(robot.Id, Option("--php", "php"), runtime, program, scenario.Seed));
    }
    var events = new List<ActionResult>();
    void Record(ActionResult result)
    {
        events.Add(result);
        Console.WriteLine($"Tick {result.Tick} {result.RobotId} {result.Action} {(result.Success ? "OK" : result.Error)} cost={result.Cost} entity={result.Entity ?? "-"}");
    }
    while (workers.Any(w => !w.Done) || sim.HasPending)
    {
        foreach (var worker in workers.Where(w => !w.Done && !w.Pending))
        {
            // Querying or handling immediate failures cannot starve the simulation forever.
            int requests = 0;
            while (!worker.Done && !worker.Pending)
            {
                if (++requests > 10000) throw new InvalidOperationException($"{worker.Id}: request budget exceeded without an action");
                using var message = JsonDocument.Parse(await worker.Read());
                var root = message.RootElement;
                string op = root.GetProperty("op").GetString()!;
                if (op == "done") { worker.Done = true; break; }
                if (op == "error") throw new InvalidOperationException($"{worker.Id}: {root.GetProperty("message").GetString()}");
                if (op == "action")
                {
                    var name = root.GetProperty("action").GetString();
                    if (!Enum.TryParse<ActionKind>(name, out var action) || !Enum.IsDefined(action))
                        throw new ArgumentException("Invalid action opcode");
                    var failure = sim.Begin(worker.Id, action);
                    if (failure is null) worker.Pending = true;
                    else { Record(failure); await worker.Send(new { ok = false, error = failure.Error, tick = sim.Tick, cost = 0 }); }
                }
                else if (op == "query")
                {
                    object data = root.GetProperty("query").GetString() switch
                    {
                        "robot" => sim.RobotState(worker.Id), "tasks" => sim.Tasks(), "zones" => sim.Zones(),
                        "materials" => sim.Materials(), "robots" => sim.Robots(),
                        "scan" => sim.Scan(worker.Id, root.TryGetProperty("direction", out var d) ? d.GetString()! : "FRONT"),
                        _ => throw new ArgumentException("Invalid query opcode")
                    };
                    await worker.Send(new { ok = true, data, tick = sim.Tick });
                }
                else throw new ArgumentException("Invalid protocol opcode");
            }
        }
        if (!sim.HasPending) continue;
        if (sim.Tick >= scenario.MaxTicks) throw new InvalidOperationException("Scenario tick limit reached");
        foreach (var result in sim.Advance())
        {
            Record(result);
            var worker = workers.Single(w => w.Id == result.RobotId);
            worker.Pending = false;
            await worker.Send(new { ok = result.Success, error = result.Error, tick = result.Tick, cost = result.Cost });
        }
    }
    foreach (var worker in workers) await worker.Finish();
    var output = Option("--output", "");
    if (output.Length > 0)
    {
        var target = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, Json.Encode(new { tick = sim.Tick, events, blocks = sim.Blocks(), tasks = sim.Tasks(), materials = sim.Materials(), zones = sim.Zones(), robots = sim.Robots() }));
    }
    Console.WriteLine($"Completed: tick={sim.Tick}, tasks={sim.Tasks().Count(t => t.Status == "COMPLETED")}/{sim.Tasks().Length}");
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
finally { foreach (var worker in workers) worker.Dispose(); }

sealed class Worker : IDisposable
{
    private readonly Process process;
    private readonly Task<string> stderr;
    public string Id { get; }
    public bool Pending { get; set; }
    public bool Done { get; set; }
    public Worker(string id, string php, string runtime, string program, int seed)
    {
        Id = id;
        var start = new ProcessStartInfo(php) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(program)! };
        foreach (var arg in new[] { "-d", "display_errors=stderr", runtime, program, seed.ToString(System.Globalization.CultureInfo.InvariantCulture) }) start.ArgumentList.Add(arg);
        process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start PHP");
        stderr = DrainErrors();
    }
    private async Task<string> DrainErrors()
    {
        var retained = new System.Text.StringBuilder();
        var buffer = new char[2048];
        int count;
        while ((count = await process.StandardError.ReadAsync(buffer)) > 0)
            if (retained.Length < 16000) retained.Append(buffer, 0, Math.Min(count, 16000 - retained.Length));
        return retained.ToString();
    }
    public async Task<string> Read()
    {
        // A bounded line reader also handles a script that writes without newlines.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var line = new System.Text.StringBuilder();
        var ch = new char[1];
        try
        {
            while (await process.StandardOutput.ReadAsync(ch.AsMemory(), timeout.Token) > 0)
            {
                if (ch[0] == '\n') return line.ToString();
                line.Append(ch[0]);
                if (line.Length > 1048576) throw new InvalidOperationException($"{Id}: protocol message too large");
            }
        }
        catch (OperationCanceledException) { throw new InvalidOperationException($"{Id}: PHP response timed out (5s)"); }
        throw new InvalidOperationException($"{Id}: PHP exited before completion");
    }
    public async Task Send(object data)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.StandardInput.WriteLineAsync(Json.Encode(data).AsMemory(), timeout.Token);
        await process.StandardInput.FlushAsync(timeout.Token);
    }
    public async Task Finish()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(timeout.Token);
        var errors = await stderr;
        if (errors.Length > 0) Console.Error.WriteLine($"{Id}: {errors.Trim()}");
        if (process.ExitCode != 0) throw new InvalidOperationException($"{Id}: PHP exit code {process.ExitCode}");
    }
    public void Dispose()
    {
        if (!process.HasExited) process.Kill(true);
        process.Dispose();
    }
}
