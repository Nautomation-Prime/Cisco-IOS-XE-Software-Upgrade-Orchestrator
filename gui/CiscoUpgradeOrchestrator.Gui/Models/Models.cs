using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CiscoUpgradeOrchestrator.Gui.Models;

public class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var i) ? i.ToString() : reader.GetDouble().ToString(),
            JsonTokenType.True => "Yes",
            JsonTokenType.False => "No",
            JsonTokenType.Null => null,
            _ => reader.GetString()
        };
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}

public class DeviceModel
{
    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = string.Empty;

    [JsonPropertyName("ip_address")]
    public string IpAddress { get; set; } = string.Empty;

    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;

    [JsonPropertyName("current_version")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? CurrentVersion { get; set; }

    [JsonPropertyName("target_version")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string TargetVersion { get; set; } = string.Empty;

    [JsonPropertyName("image_path")]
    public string ImagePath { get; set; } = string.Empty;

    [JsonPropertyName("image_md5")]
    public string ImageMd5 { get; set; } = string.Empty;

    [JsonPropertyName("redundancy_group")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? RedundancyGroup { get; set; }

    [JsonPropertyName("console_verified")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? ConsoleVerified { get; set; }

    [JsonPropertyName("enabled")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Enabled { get; set; }

    [JsonPropertyName("ssh_port")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? SshPort { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("batch_number")]
    public int? BatchNumber { get; set; }

    [JsonPropertyName("attempt")]
    public int? Attempt { get; set; }

    [JsonPropertyName("backup_path")]
    public string? BackupPath { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public class JobModel
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("inventory_path")]
    public string? InventoryPath { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("operator")]
    public string? Operator { get; set; }

    [JsonPropertyName("dry_run")]
    public int DryRun { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("started_at")]
    public string? StartedAt { get; set; }

    [JsonPropertyName("completed_at")]
    public string? CompletedAt { get; set; }
}

public class JobSummaryModel
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    [JsonPropertyName("complete")]
    public int Complete { get; set; }

    [JsonPropertyName("cleanup_pending")]
    public int CleanupPending { get; set; }
}

public class JobEventModel
{
    [JsonPropertyName("event_id")]
    public int EventId { get; set; }

    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    [JsonPropertyName("level")]
    public string Level { get; set; } = "INFO";

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = string.Empty;
}

public class JobDetailsModel
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("devices")]
    public List<DeviceModel> Devices { get; set; } = new();

    [JsonPropertyName("summary")]
    public JobSummaryModel Summary { get; set; } = new();

    [JsonPropertyName("events")]
    public List<JobEventModel> Events { get; set; } = new();

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class InventoryParseResult
{
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();

    [JsonPropertyName("total_devices")]
    public int TotalDevices { get; set; }

    [JsonPropertyName("devices")]
    public List<DeviceModel> Devices { get; set; } = new();
}

public class InventorySaveResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();

    [JsonPropertyName("total_devices")]
    public int TotalDevices { get; set; }

    [JsonPropertyName("file_path")]
    public string FilePath { get; set; } = string.Empty;
}

public class PlanResultModel
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("plan")]
    public PlanDataModel? Plan { get; set; }
}

public class PlanDataModel
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;

    [JsonPropertyName("inventory")]
    public string Inventory { get; set; } = string.Empty;

    [JsonPropertyName("dry_run")]
    public bool DryRun { get; set; }

    [JsonPropertyName("batches")]
    public List<List<string>> Batches { get; set; } = new();

    [JsonPropertyName("commands")]
    public List<string> Commands { get; set; } = new();
}

public class EnvironmentInfo
{
    [JsonPropertyName("python_executable")]
    public string PythonExecutable { get; set; } = string.Empty;

    [JsonPropertyName("python_version")]
    public string PythonVersion { get; set; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = string.Empty;

    [JsonPropertyName("dependencies")]
    public Dictionary<string, bool> Dependencies { get; set; } = new();

    [JsonPropertyName("all_installed")]
    public bool AllInstalled { get; set; }

    [JsonPropertyName("state_store_available")]
    public bool StateStoreAvailable { get; set; } = true;

    [JsonPropertyName("inventory_manager_available")]
    public bool InventoryManagerAvailable { get; set; } = true;

    [JsonPropertyName("import_error")]
    public string? ImportError { get; set; }
}

public class HistoryActionResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("deleted")]
    public int Deleted { get; set; }

    [JsonPropertyName("skipped_active")]
    public int SkippedActive { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
