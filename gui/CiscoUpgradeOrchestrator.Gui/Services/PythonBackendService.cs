using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CiscoUpgradeOrchestrator.Gui.Models;

namespace CiscoUpgradeOrchestrator.Gui.Services;

public class PythonBackendService
{
    private static PythonBackendService? _instance;
    public static PythonBackendService Instance => _instance ??= new PythonBackendService();

    public string PythonPath { get; set; } = string.Empty;
    public string ProjectRoot { get; set; } = string.Empty;

    public string? LastError { get; set; }
    public string? LastStderr { get; set; }
    public string? LastCommandExecuted { get; set; }

    public PythonBackendService()
    {
        InitializePaths();
    }

    public void InitializePaths()
    {
        var saved = AppSettingsService.Load();

        // 1. Prefer the repo containing the running executable when launched from dist/
        var discoveredFromBinary = DiscoverProjectRootFromBinary();
        if (!string.IsNullOrEmpty(discoveredFromBinary))
        {
            ProjectRoot = discoveredFromBinary;
        }

        // 2. Otherwise use saved settings if valid and not pointing to a temporary worktree
        if (string.IsNullOrEmpty(ProjectRoot)
            && !string.IsNullOrEmpty(saved.ProjectRoot)
            && !saved.ProjectRoot.Contains(@"\.gemini\antigravity\worktrees\", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(saved.ProjectRoot))
        {
            if (File.Exists(Path.Combine(saved.ProjectRoot, "orchestrator.py")) ||
                File.Exists(Path.Combine(saved.ProjectRoot, "gui_bridge.py")))
            {
                ProjectRoot = saved.ProjectRoot;
            }
        }

        // 3. Fallback discovery
        if (string.IsNullOrEmpty(ProjectRoot))
        {
            ProjectRoot = DiscoverProjectRoot();
        }

        // 4. Resolve Python executable: prioritize bundled standalone python-runtime first!
        var bundledPython = FindBundledPythonRuntime();
        if (!string.IsNullOrEmpty(bundledPython))
        {
            PythonPath = bundledPython;
        }
        else if (!string.IsNullOrEmpty(saved.PythonExecutable)
            && !saved.PythonExecutable.Contains(@"\.gemini\antigravity\worktrees\", StringComparison.OrdinalIgnoreCase)
            && File.Exists(saved.PythonExecutable))
        {
            PythonPath = saved.PythonExecutable;
        }
        else
        {
            PythonPath = AutoDetectPythonInterpreter();
        }

        // Persist detected values if settings were blank or pointed to a stale worktree
        if (string.IsNullOrEmpty(saved.ProjectRoot) || string.IsNullOrEmpty(saved.PythonExecutable)
            || saved.ProjectRoot.Contains(@"\.gemini\antigravity\worktrees\", StringComparison.OrdinalIgnoreCase))
        {
            saved.ProjectRoot = ProjectRoot;
            saved.PythonExecutable = PythonPath;
            AppSettingsService.Save(saved);
        }
    }

    private string? FindBundledPythonRuntime()
    {
        bool isWindows = OperatingSystem.IsWindows();
        string relExe = isWindows ? Path.Combine("python-runtime", "python.exe") : Path.Combine("python-runtime", "bin", "python3");

        var roots = new List<string?>();
        try
        {
            if (!string.IsNullOrEmpty(Environment.ProcessPath))
                roots.Add(Path.GetDirectoryName(Environment.ProcessPath));
        }
        catch { }
        try { roots.Add(AppDomain.CurrentDomain.BaseDirectory); } catch { }
        roots.Add(ProjectRoot);

        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root)) continue;
            var candidate = Path.Combine(root, relExe);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }
        return null;
    }

    private string? DiscoverProjectRootFromBinary()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath)) return null;
            var startDir = Path.GetDirectoryName(processPath);
            if (string.IsNullOrEmpty(startDir) || !Directory.Exists(startDir)) return null;

            var dir = new DirectoryInfo(startDir);
            int maxDepth = 5;
            while (dir != null && maxDepth-- > 0)
            {
                if (File.Exists(Path.Combine(dir.FullName, "orchestrator.py")) &&
                    File.Exists(Path.Combine(dir.FullName, "gui_bridge.py")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
        }
        catch { }
        return null;
    }

    public string DiscoverProjectRoot()
    {
        // Candidate start directories
        var candidates = new List<string?>();

        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath))
            {
                candidates.Add(Path.GetDirectoryName(processPath));
            }
        }
        catch { }

        try { candidates.Add(AppDomain.CurrentDomain.BaseDirectory); } catch { }
        try { candidates.Add(Directory.GetCurrentDirectory()); } catch { }

        foreach (var startDir in candidates)
        {
            if (string.IsNullOrEmpty(startDir) || !Directory.Exists(startDir)) continue;

            var dir = new DirectoryInfo(startDir);
            int maxDepth = 6;
            while (dir != null && maxDepth-- > 0)
            {
                if (File.Exists(Path.Combine(dir.FullName, "orchestrator.py")) ||
                    File.Exists(Path.Combine(dir.FullName, "gui_bridge.py")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    public string AutoDetectPythonInterpreter()
    {
        var foundList = FindAvailablePythonInterpreters();
        if (foundList.Count > 0)
        {
            // Prefer bundled python-runtime first, then virtual environments (.venv or venv)
            var preferredBundled = foundList.FirstOrDefault(p => p.Contains("python-runtime", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(preferredBundled))
            {
                return preferredBundled;
            }

            var preferredVenv = foundList.FirstOrDefault(p => p.Contains(".venv", StringComparison.OrdinalIgnoreCase) ||
                                                              p.Contains($"{Path.DirectorySeparatorChar}venv{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(preferredVenv))
            {
                return preferredVenv;
            }

            return foundList[0];
        }

        return OperatingSystem.IsWindows() ? "python" : "python3";
    }

    public List<string> FindAvailablePythonInterpreters()
    {
        var results = new List<string>();
        bool isWindows = OperatingSystem.IsWindows();

        void TryAdd(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                var full = Path.GetFullPath(path);
                if (!results.Contains(full, StringComparer.OrdinalIgnoreCase))
                {
                    results.Add(full);
                }
            }
        }

        void TryAddVenv(string baseDir)
        {
            if (isWindows)
            {
                TryAdd(Path.Combine(baseDir, ".venv", "Scripts", "python.exe"));
                TryAdd(Path.Combine(baseDir, "venv", "Scripts", "python.exe"));
            }
            else
            {
                TryAdd(Path.Combine(baseDir, ".venv", "bin", "python3"));
                TryAdd(Path.Combine(baseDir, ".venv", "bin", "python"));
                TryAdd(Path.Combine(baseDir, "venv", "bin", "python3"));
                TryAdd(Path.Combine(baseDir, "venv", "bin", "python"));
            }
        }

        // 0. Bundled standalone python-runtime (highest priority for installed/portable releases)
        TryAdd(FindBundledPythonRuntime());

        // 1. Virtual environments inside ProjectRoot
        if (!string.IsNullOrEmpty(ProjectRoot) && Directory.Exists(ProjectRoot))
        {
            TryAdd(Path.Combine(ProjectRoot, "python-runtime", isWindows ? "python.exe" : Path.Combine("bin", "python3")));
            TryAddVenv(ProjectRoot);

            // Check parent directories up to 3 levels
            var dir = new DirectoryInfo(ProjectRoot).Parent;
            int depth = 3;
            while (dir != null && depth-- > 0)
            {
                TryAddVenv(dir.FullName);
                dir = dir.Parent;
            }
        }

        if (isWindows)
        {
            // 2. Common user profile virtualenvs
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile))
            {
                TryAdd(Path.Combine(userProfile, ".virtualenvs", "cisco-upgrade", "Scripts", "python.exe"));
                TryAdd(Path.Combine(userProfile, "miniconda3", "python.exe"));
                TryAdd(Path.Combine(userProfile, "anaconda3", "python.exe"));
            }

            // 4. Standard Windows Python installations (Python 3.12, 3.11, 3.10)
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
            {
                TryAdd(Path.Combine(localAppData, "Programs", "Python", "Python312", "python.exe"));
                TryAdd(Path.Combine(localAppData, "Programs", "Python", "Python311", "python.exe"));
                TryAdd(Path.Combine(localAppData, "Programs", "Python", "Python310", "python.exe"));
                TryAdd(Path.Combine(localAppData, "Python", "pythoncore-3.14-64", "python.exe"));
            }

            // 5. Program Files Python installations
            var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(progFiles))
            {
                TryAdd(Path.Combine(progFiles, "Python312", "python.exe"));
                TryAdd(Path.Combine(progFiles, "Python311", "python.exe"));
                TryAdd(Path.Combine(progFiles, "Python310", "python.exe"));
            }
        }
        else
        {
            // macOS / Linux common Python 3 locations (Homebrew, pyenv, system)
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                TryAdd(Path.Combine(home, ".pyenv", "shims", "python3"));
                TryAdd(Path.Combine(home, ".virtualenvs", "cisco-upgrade", "bin", "python3"));
                TryAdd(Path.Combine(home, "miniconda3", "bin", "python3"));
            }
            TryAdd("/opt/homebrew/bin/python3");
            TryAdd("/usr/local/bin/python3");
            TryAdd("/usr/bin/python3");
        }

        return results;
    }

    public void SaveConfiguration(string pythonPath, string projectRoot)
    {
        if (!string.IsNullOrWhiteSpace(pythonPath)) PythonPath = pythonPath;
        if (!string.IsNullOrWhiteSpace(projectRoot)) ProjectRoot = projectRoot;

        var saved = AppSettingsService.Load();
        if (!string.IsNullOrWhiteSpace(pythonPath)) saved.PythonExecutable = pythonPath;
        if (!string.IsNullOrWhiteSpace(projectRoot)) saved.ProjectRoot = projectRoot;
        AppSettingsService.Save(saved);
    }

    public async Task<EnvironmentInfo?> TestEnvironmentAsync()
    {
        var output = await RunBridgeCommandAsync("test-env");
        if (string.IsNullOrWhiteSpace(output)) return null;
        try
        {
            var info = JsonSerializer.Deserialize<EnvironmentInfo>(output);
            return info;
        }
        catch (Exception ex)
        {
            LastError = $"JSON Deserialization failed: {ex.Message}. Output: {output}";
            return null;
        }
    }

    public async Task<InventoryParseResult> ParseInventoryAsync(string filePath)
    {
        var output = await RunBridgeCommandAsync($"parse-inventory --file \"{filePath}\"");
        if (string.IsNullOrWhiteSpace(output))
        {
            return new InventoryParseResult
            {
                Valid = false,
                Errors = new List<string> { LastError ?? "Empty response from inventory parser." }
            };
        }
        try
        {
            var result = JsonSerializer.Deserialize<InventoryParseResult>(output);
            return result ?? new InventoryParseResult
            {
                Valid = false,
                Errors = new List<string> { "Failed to deserialize inventory response." }
            };
        }
        catch (Exception ex)
        {
            return new InventoryParseResult
            {
                Valid = false,
                Errors = new List<string> { ex.Message, output }
            };
        }
    }

    public async Task<InventorySaveResult> SaveInventoryAsync(string filePath, IEnumerable<DeviceModel> devices)
    {
        var tempJsonPath = Path.Combine(Path.GetTempPath(), $"cisco_inv_{Guid.NewGuid():N}.json");
        try
        {
            var json = JsonSerializer.Serialize(devices);
            await File.WriteAllTextAsync(tempJsonPath, json);

            var output = await RunBridgeCommandAsync($"save-inventory --file \"{filePath}\" --json \"{tempJsonPath}\"");
            if (string.IsNullOrWhiteSpace(output))
            {
                return new InventorySaveResult
                {
                    Success = false,
                    Valid = false,
                    FilePath = filePath,
                    Errors = new List<string> { LastError ?? "Empty response from save-inventory bridge." }
                };
            }

            return JsonSerializer.Deserialize<InventorySaveResult>(output) ?? new InventorySaveResult
            {
                Success = false,
                Valid = false,
                FilePath = filePath,
                Errors = new List<string> { "Failed to deserialize save-inventory response." }
            };
        }
        catch (Exception ex)
        {
            return new InventorySaveResult
            {
                Success = false,
                Valid = false,
                FilePath = filePath,
                Errors = new List<string> { ex.Message }
            };
        }
        finally
        {
            try
            {
                if (File.Exists(tempJsonPath)) File.Delete(tempJsonPath);
            }
            catch { }
        }
    }

    public async Task<PlanResultModel> CreatePlanAsync(string inventoryFile)
    {
        var output = await RunBridgeCommandAsync($"plan --file \"{inventoryFile}\"");
        if (string.IsNullOrWhiteSpace(output))
        {
            return new PlanResultModel
            {
                Success = false,
                Error = LastError ?? "Empty response from planning engine."
            };
        }
        try
        {
            return JsonSerializer.Deserialize<PlanResultModel>(output) ?? new PlanResultModel
            {
                Success = false,
                Error = "Deserialization failed."
            };
        }
        catch (Exception ex)
        {
            return new PlanResultModel { Success = false, Error = ex.Message };
        }
    }

    public async Task<List<JobModel>> GetJobsAsync()
    {
        var output = await RunBridgeCommandAsync("get-jobs");
        if (string.IsNullOrWhiteSpace(output)) return new List<JobModel>();
        try
        {
            var jobs = JsonSerializer.Deserialize<List<JobModel>>(output);
            if (jobs != null)
            {
                // Filter out error-only placeholder items
                return jobs.Where(j => !string.IsNullOrEmpty(j.JobId)).ToList();
            }
            return new List<JobModel>();
        }
        catch
        {
            return new List<JobModel>();
        }
    }

    public async Task<JobDetailsModel> GetJobDetailsAsync(string jobId)
    {
        var output = await RunBridgeCommandAsync($"get-job-devices --job-id \"{jobId}\"");
        if (string.IsNullOrWhiteSpace(output)) return new JobDetailsModel { JobId = jobId, Error = LastError };
        try
        {
            return JsonSerializer.Deserialize<JobDetailsModel>(output) ?? new JobDetailsModel { JobId = jobId };
        }
        catch (Exception ex)
        {
            return new JobDetailsModel { JobId = jobId, Error = ex.Message };
        }
    }

    public async Task<string> GetConfigJsonAsync()
    {
        return await RunBridgeCommandAsync("get-config");
    }

    public async Task<HistoryActionResult> DeleteJobAsync(string jobId)
    {
        var output = await RunBridgeCommandAsync($"delete-job --job-id \"{jobId}\"");
        if (string.IsNullOrWhiteSpace(output))
        {
            return new HistoryActionResult { Success = false, Error = LastError ?? "Empty response from delete-job." };
        }
        try
        {
            return JsonSerializer.Deserialize<HistoryActionResult>(output)
                ?? new HistoryActionResult { Success = false, Error = "Failed to parse delete-job response." };
        }
        catch (Exception ex)
        {
            return new HistoryActionResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<HistoryActionResult> ClearHistoryAsync()
    {
        var output = await RunBridgeCommandAsync("clear-history");
        if (string.IsNullOrWhiteSpace(output))
        {
            return new HistoryActionResult { Success = false, Error = LastError ?? "Empty response from clear-history." };
        }
        try
        {
            return JsonSerializer.Deserialize<HistoryActionResult>(output)
                ?? new HistoryActionResult { Success = false, Error = "Failed to parse clear-history response." };
        }
        catch (Exception ex)
        {
            return new HistoryActionResult { Success = false, Error = ex.Message };
        }
    }

    public string ResolveOrchestratorConfigPrefix()
    {
        if (string.IsNullOrWhiteSpace(ProjectRoot)) return string.Empty;

        var primary = Path.Combine(ProjectRoot, "config", "config.yaml");
        if (File.Exists(primary))
        {
            return $"--config \"{primary}\" ";
        }

        var example = Path.Combine(ProjectRoot, "config", "config.example.yaml");
        if (File.Exists(example))
        {
            return $"--config \"{example}\" ";
        }

        return string.Empty;
    }

    public Task RunOrchestratorStreamingAsync(
        string commandArgs,
        Action<string> onLine,
        Action<bool, int> onFinished,
        CancellationToken ct,
        Dictionary<string, string>? extraEnv = null)
    {
        // Offload Process.Start() and OneDrive/Defender file checks completely off the UI thread
        return Task.Run(async () =>
        {
            var fullArgs = commandArgs.TrimStart().StartsWith("--config", StringComparison.OrdinalIgnoreCase)
                ? commandArgs
                : $"{ResolveOrchestratorConfigPrefix()}{commandArgs}";

            var psi = new ProcessStartInfo
            {
                FileName = PythonPath,
                Arguments = $"orchestrator.py {fullArgs}",
                WorkingDirectory = ProjectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // Enable unbuffered python output, isolate bundled runtime, and ensure ProjectRoot is in PYTHONPATH
            psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
            psi.EnvironmentVariables["PYTHONNOUSERSITE"] = "1";
            psi.EnvironmentVariables.Remove("PYTHONHOME");
            if (!string.IsNullOrEmpty(ProjectRoot))
            {
                var existingPythonPath = Environment.GetEnvironmentVariable("PYTHONPATH") ?? string.Empty;
                psi.EnvironmentVariables["PYTHONPATH"] = string.IsNullOrEmpty(existingPythonPath)
                    ? ProjectRoot
                    : $"{ProjectRoot}{Path.PathSeparator}{existingPythonPath}";
            }

            if (extraEnv != null)
            {
                foreach (var kvp in extraEnv)
                {
                    if (!string.IsNullOrEmpty(kvp.Value))
                    {
                        psi.EnvironmentVariables[kvp.Key] = kvp.Value;
                    }
                }
            }

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null) onLine(e.Data);
            };
            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null) onLine($"[ERR] {e.Data}");
            };

            try
            {
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                using var registration = ct.Register(() =>
                {
                    try
                    {
                        if (!proc.HasExited) proc.Kill(true);
                    }
                    catch { }
                });

                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
                onFinished(proc.ExitCode == 0, proc.ExitCode);
            }
            catch (OperationCanceledException)
            {
                onLine("[CANCELLED] Execution was cancelled by user.");
                onFinished(false, -1);
            }
            catch (Exception ex)
            {
                onLine($"[EXCEPTION] {ex.Message}");
                onFinished(false, -1);
            }
        });
    }

    private Task<string> RunBridgeCommandAsync(string subArgs)
    {
        // Offload Process.Start() and filesystem probes completely off the Avalonia UI thread
        return Task.Run(async () =>
        {
            try
            {
                var bridgePath = Path.Combine(ProjectRoot, "gui_bridge.py");
                if (!File.Exists(bridgePath))
                {
                    LastError = $"gui_bridge.py not found at: '{bridgePath}'. Please configure the Project / Script Root directory in Settings.";
                    return $"{{\"error\": {JsonSerializer.Serialize(LastError)}}}";
                }

                if (!File.Exists(PythonPath) && PythonPath != "python" && PythonPath != "python3" && PythonPath != "py")
                {
                    LastError = $"Python executable not found at: '{PythonPath}'. Please configure the Python path in Settings.";
                    return $"{{\"error\": {JsonSerializer.Serialize(LastError)}}}";
                }

                LastCommandExecuted = $"\"{PythonPath}\" \"{bridgePath}\" {subArgs}";

                var psi = new ProcessStartInfo
                {
                    FileName = PythonPath,
                    Arguments = $"\"{bridgePath}\" {subArgs}",
                    WorkingDirectory = ProjectRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
                psi.EnvironmentVariables["PYTHONNOUSERSITE"] = "1";
                psi.EnvironmentVariables.Remove("PYTHONHOME");
                if (!string.IsNullOrEmpty(ProjectRoot))
                {
                    var existingPythonPath = Environment.GetEnvironmentVariable("PYTHONPATH") ?? string.Empty;
                    psi.EnvironmentVariables["PYTHONPATH"] = string.IsNullOrEmpty(existingPythonPath)
                        ? ProjectRoot
                        : $"{ProjectRoot}{Path.PathSeparator}{existingPythonPath}";
                }

                using var proc = new Process { StartInfo = psi };
                proc.Start();

                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();

                await proc.WaitForExitAsync().ConfigureAwait(false);

                var stdout = (await stdoutTask.ConfigureAwait(false)).Trim();
                var stderr = (await stderrTask.ConfigureAwait(false)).Trim();

                if (proc.ExitCode != 0 || (string.IsNullOrWhiteSpace(stdout) && !string.IsNullOrWhiteSpace(stderr)))
                {
                    LastStderr = stderr;
                    LastError = !string.IsNullOrWhiteSpace(stderr)
                        ? stderr
                        : $"Python process exited with non-zero exit code: {proc.ExitCode}";
                    return $"{{\"error\": {JsonSerializer.Serialize(LastError)}}}";
                }

                LastError = null;
                LastStderr = null;
                return stdout;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return $"{{\"error\": {JsonSerializer.Serialize(ex.Message)}}}";
            }
        });
    }
}
