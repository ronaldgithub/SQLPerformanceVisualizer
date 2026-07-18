using System.Diagnostics;
using System.Text;

namespace SQLPerformanceVisualizer.Services;

public class PlanAnalysisService : IPlanAnalysisService
{
    public string GetPlansFolder(string database)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "SQLPerformanceVisualizer", "plans", SanitizeFileName(database));
        Directory.CreateDirectory(folder);
        return folder;
    }

    public async Task<string> SavePlanAsync(string database, long queryId, string planXml, string suffix, CancellationToken ct = default)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var path = Path.Combine(GetPlansFolder(database), $"query_{queryId}_{stamp}{suffix}.sqlplan");
        await File.WriteAllTextAsync(path, planXml, new UTF8Encoding(false), ct);
        return path;
    }

    public async Task<PlanAnalysisResult> AnalyzePlanAsync(string planPath, CancellationToken ct = default)
    {
        var prompt =
            $"Use the query-plan-analysis skill to analyze the SQL Server execution plan file at {planPath}. " +
            "Output the full analysis as markdown. Do not write any files.";

        using var process = new Process { StartInfo = BuildStartInfo(planPath, prompt) };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Could not start the Claude Code CLI ('claude'). Is it installed and on PATH?", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
            throw;
        }

        var markdown = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"claude exited with code {process.ExitCode}: {(string.IsNullOrWhiteSpace(stderr) ? markdown : stderr)}");

        var reportPath = Path.ChangeExtension(planPath, ".md");
        await File.WriteAllTextAsync(reportPath, markdown, new UTF8Encoding(false), ct);
        return new PlanAnalysisResult(reportPath, markdown);
    }

    private static ProcessStartInfo BuildStartInfo(string planPath, string prompt)
    {
        var claude = FindClaudeExecutable();
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = Path.GetDirectoryName(planPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (claude is not null && claude.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            psi.FileName = claude;
        }
        else
        {
            // npm installs only provide a claude.cmd shim, which cmd.exe must launch
            psi.FileName = "cmd.exe";
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(claude ?? "claude");
        }
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(prompt);
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("text");
        // plan XML is untrusted input: grant only the tools the skill needs, never bypassPermissions
        psi.ArgumentList.Add("--allowedTools");
        psi.ArgumentList.Add("Skill");
        psi.ArgumentList.Add("Read");
        psi.ArgumentList.Add("Glob");
        psi.ArgumentList.Add("Grep");
        psi.ArgumentList.Add("Bash(python *)");
        return psi;
    }

    private static string? FindClaudeExecutable()
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        dirs.Insert(0, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"));
        foreach (var dir in dirs)
            foreach (var name in new[] { "claude.exe", "claude.cmd" })
            {
                var candidate = Path.Combine(dir.Trim(), name);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
