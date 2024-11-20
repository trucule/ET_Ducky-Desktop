using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EtwMonitor.Desktop.Services
{
    public enum DiagnosticActionType
    {
        CheckFreeDiskSpace,
        ScanFolderForLargeFiles
    }

    public class DiagnosticResult
    {
        public DiagnosticActionType ActionType { get; set; }
        public DateTime Timestamp { get; set; }
        public string Summary { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? Parameter { get; set; }
    }

    /// <summary>
    /// Performs simple on-demand diagnostics that the AI layer can use
    /// together with ETW events for troubleshooting.
    /// </summary>
    public class DiagnosticsService
    {
        private readonly MonitorStateService _monitorState;

        public DiagnosticsService(MonitorStateService monitorState)
        {
            _monitorState = monitorState;
        }

        public async Task<DiagnosticResult> RunActionAsync(
            DiagnosticActionType actionType,
            string? parameter = null,
            CancellationToken cancellationToken = default)
        {
            var result = new DiagnosticResult
            {
                ActionType = actionType,
                Timestamp = DateTime.Now,
                Parameter = parameter
            };

            try
            {
                switch (actionType)
                {
                    case DiagnosticActionType.CheckFreeDiskSpace:
                        result = CheckFreeDiskSpace();
                        break;

                    case DiagnosticActionType.ScanFolderForLargeFiles:
                        result = await ScanFolderForLargeFilesAsync(
                            parameter ?? "C:\\",
                            cancellationToken);
                        break;
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Summary = "Diagnostic action failed";
                result.Details = ex.ToString();
            }

            return result;
        }

        private DiagnosticResult CheckFreeDiskSpace()
        {
            var lines = new List<string>();

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var totalGb = drive.TotalSize / (1024d * 1024d * 1024d);
                var freeGb = drive.AvailableFreeSpace / (1024d * 1024d * 1024d);

                lines.Add($"{drive.Name}  Total: {totalGb:F1} GB, Free: {freeGb:F1} GB");
            }

            return new DiagnosticResult
            {
                ActionType = DiagnosticActionType.CheckFreeDiskSpace,
                Timestamp = DateTime.Now,
                Summary = "Free disk space by drive",
                Details = string.Join(Environment.NewLine, lines),
                Success = true
            };
        }

        private async Task<DiagnosticResult> ScanFolderForLargeFilesAsync(
            string rootFolder,
            CancellationToken cancellationToken)
        {
            var largeFiles = new List<FileInfo>();

            await Task.Run(() =>
            {
                try
                {
                    foreach (var path in Directory.EnumerateFiles(rootFolder, "*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            var fi = new FileInfo(path);
                            // Threshold: > 100 MB
                            if (fi.Length > 100 * 1024 * 1024)
                            {
                                largeFiles.Add(fi);
                            }
                        }
                        catch
                        {
                            // Ignore access denied, etc.
                        }
                    }
                }
                catch
                {
                    // Ignore failures for top-level enumeration
                }
            }, cancellationToken);

            var formatted = largeFiles
                .OrderByDescending(f => f.Length)
                .Take(50)
                .Select(f => $"{f.FullName}  ({f.Length / (1024d * 1024d):F1} MB)");

            return new DiagnosticResult
            {
                ActionType = DiagnosticActionType.ScanFolderForLargeFiles,
                Timestamp = DateTime.Now,
                Summary = $"Found {largeFiles.Count} files > 100 MB under {rootFolder}",
                Details = string.Join(Environment.NewLine, formatted),
                Success = true,
                Parameter = rootFolder
            };
        }
    }
}
