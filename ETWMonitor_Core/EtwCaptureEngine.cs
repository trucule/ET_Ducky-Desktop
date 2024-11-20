using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using EtwMonitor.Core.Models;
using Serilog;

namespace EtwMonitor.Core.Capture
{
    public class EtwCaptureEngine : IDisposable
    {
        private readonly ILogger _logger;
        private TraceEventSession? _session;
        private Task? _processingTask;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        
        public event EventHandler<SystemEvent>? EventCaptured;
        public event EventHandler<string>? ErrorOccurred;
        
        private long _eventsProcessed = 0;
        private readonly object _statsLock = new();

        public EtwCaptureEngine(ILogger logger)
        {
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            _logger.Information("Starting ETW Capture Engine...");
            
            try
            {
                // Create ETW session (requires Administrator)
                _session = new TraceEventSession("EtwMonitorSession");
                
                // Enable kernel providers
                _session.EnableKernelProvider(
                    KernelTraceEventParser.Keywords.FileIO | 
                    KernelTraceEventParser.Keywords.FileIOInit |
                    KernelTraceEventParser.Keywords.Registry |
                    KernelTraceEventParser.Keywords.Process |
                    KernelTraceEventParser.Keywords.Thread |
                    KernelTraceEventParser.Keywords.NetworkTCPIP,
                    KernelTraceEventParser.Keywords.None
                );

                _logger.Information("ETW Kernel providers enabled");

                // Set up event handlers
                SetupEventHandlers();

                // Start processing events in background
                _processingTask = Task.Run(() => ProcessEvents(_cancellationTokenSource.Token), cancellationToken);
                
                _logger.Information("ETW Capture Engine started successfully");
                
                await Task.CompletedTask;
            }
            catch (UnauthorizedAccessException)
            {
                _logger.Error("ETW capture requires Administrator privileges");
                ErrorOccurred?.Invoke(this, "Administrator privileges required. Please run as Administrator.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to start ETW capture");
                ErrorOccurred?.Invoke(this, $"Failed to start: {ex.Message}");
                throw;
            }
        }

        private void SetupEventHandlers()
        {
            if (_session == null) return;

            var kernel = _session.Source.Kernel;

            // File I/O Events
            kernel.FileIOCreate += data => HandleFileEvent("Create", data);
            kernel.FileIOWrite += data => HandleFileEvent("Write", data);
            kernel.FileIORead += data => HandleFileEvent("Read", data);
            kernel.FileIODelete += data => HandleFileEvent("Delete", data);
            kernel.FileIORename += data => HandleFileEvent("Rename", data);
            kernel.FileIOFileCreate += data => HandleFileEvent("FileCreate", data);
            kernel.FileIOFileDelete += data => HandleFileEvent("FileDelete", data);

            // Registry Events
            kernel.RegistryCreate += data => HandleRegistryEvent("Create", data);
            kernel.RegistryOpen += data => HandleRegistryEvent("Open", data);
            kernel.RegistryDelete += data => HandleRegistryEvent("Delete", data);
            kernel.RegistrySetValue += data => HandleRegistryEvent("SetValue", data);
            kernel.RegistryQueryValue += data => HandleRegistryEvent("QueryValue", data);

            // Process Events
            kernel.ProcessStart += data => HandleProcessEvent("Start", data);
            kernel.ProcessStop += data => HandleProcessEvent("Stop", data);

            // Thread Events
            kernel.ThreadStart += data => HandleThreadEvent("Start", data);
            kernel.ThreadStop += data => HandleThreadEvent("Stop", data);

            // Network Events
            kernel.TcpIpConnect += data => HandleNetworkEvent("TcpConnect", data);
            kernel.TcpIpDisconnect += data => HandleNetworkEvent("TcpDisconnect", data);
            kernel.TcpIpSend += data => HandleNetworkEvent("TcpSend", data);
            kernel.TcpIpRecv += data => HandleNetworkEvent("TcpRecv", data);
        }

        private void HandleFileEvent(string operation, TraceEvent data)
        {
            try
            {
                var evt = new SystemEvent
                {
                    Timestamp = data.TimeStamp,
                    Type = EventType.FileSystem,
                    ProcessId = data.ProcessID,
                    ThreadId = data.ThreadID,
                    ProcessName = data.ProcessName,
                    Operation = operation,
                    Path = GetFilePathFromEvent(data),
                    Result = GetResultFromEvent(data),
                    Details = data.ToString()
                };

                IncrementCounter();
                EventCaptured?.Invoke(this, evt);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error handling file event");
            }
        }

        private void HandleRegistryEvent(string operation, TraceEvent data)
        {
            try
            {
                var evt = new SystemEvent
                {
                    Timestamp = data.TimeStamp,
                    Type = EventType.Registry,
                    ProcessId = data.ProcessID,
                    ThreadId = data.ThreadID,
                    ProcessName = data.ProcessName,
                    Operation = operation,
                    Path = GetRegistryPathFromEvent(data),
                    Result = GetResultFromEvent(data)
                };

                IncrementCounter();
                EventCaptured?.Invoke(this, evt);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error handling registry event");
            }
        }

        private void HandleProcessEvent(string operation, TraceEvent data)
        {
            try
            {
                var evt = new SystemEvent
                {
                    Timestamp = data.TimeStamp,
                    Type = EventType.Process,
                    ProcessId = data.ProcessID,
                    ThreadId = data.ThreadID,
                    ProcessName = data.ProcessName,
                    Operation = operation,
                    Path = GetProcessImagePathFromEvent(data),
                    Result = "SUCCESS"
                };

                IncrementCounter();
                EventCaptured?.Invoke(this, evt);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error handling process event");
            }
        }

        private void HandleThreadEvent(string operation, TraceEvent data)
        {
            try
            {
                var evt = new SystemEvent
                {
                    Timestamp = data.TimeStamp,
                    Type = EventType.Process,
                    ProcessId = data.ProcessID,
                    ThreadId = data.ThreadID,
                    ProcessName = data.ProcessName,
                    Operation = $"Thread{operation}",
                    Result = "SUCCESS"
                };

                IncrementCounter();
                EventCaptured?.Invoke(this, evt);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error handling thread event");
            }
        }

        private void HandleNetworkEvent(string operation, TraceEvent data)
        {
            try
            {
                var evt = new SystemEvent
                {
                    Timestamp = data.TimeStamp,
                    Type = EventType.Network,
                    ProcessId = data.ProcessID,
                    ThreadId = data.ThreadID,
                    ProcessName = data.ProcessName,
                    Operation = operation,
                    Result = "SUCCESS"
                };

                IncrementCounter();
                EventCaptured?.Invoke(this, evt);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error handling network event");
            }
        }

        private void ProcessEvents(CancellationToken cancellationToken)
        {
            try
            {
                _logger.Information("Processing ETW events...");
                _session?.Source.Process();
            }
            catch (OperationCanceledException)
            {
                _logger.Information("ETW processing cancelled");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing ETW events");
                ErrorOccurred?.Invoke(this, $"Processing error: {ex.Message}");
            }
        }

        // Helper methods to extract data from TraceEvent
        private string? GetFilePathFromEvent(TraceEvent data)
        {
            try
            {
                return data.PayloadByName("FileName") as string;
            }
            catch
            {
                return null;
            }
        }

        private string? GetRegistryPathFromEvent(TraceEvent data)
        {
            try
            {
                return data.PayloadByName("KeyName") as string;
            }
            catch
            {
                return null;
            }
        }

        private string? GetProcessImagePathFromEvent(TraceEvent data)
        {
            try
            {
                return data.PayloadByName("ImageFileName") as string ?? 
                       data.PayloadByName("CommandLine") as string;
            }
            catch
            {
                return null;
            }
        }

        private string GetResultFromEvent(TraceEvent data)
        {
            try
            {
                var status = data.PayloadByName("Status");
                if (status == null) return "SUCCESS";
                
                var statusValue = Convert.ToInt32(status);
                return statusValue == 0 ? "SUCCESS" : $"ERROR_{statusValue:X}";
            }
            catch
            {
                return "UNKNOWN";
            }
        }

        private void IncrementCounter()
        {
            lock (_statsLock)
            {
                _eventsProcessed++;
                
                if (_eventsProcessed % 10000 == 0)
                {
                    _logger.Debug("Processed {Count} events", _eventsProcessed);
                }
            }
        }

        public long GetEventsProcessed()
        {
            lock (_statsLock)
            {
                return _eventsProcessed;
            }
        }

        public async Task StopAsync()
        {
            _logger.Information("Stopping ETW Capture Engine...");
            
            _cancellationTokenSource.Cancel();
            
            if (_processingTask != null)
            {
                await _processingTask;
            }
            
            _session?.Dispose();
            _session = null;
            
            _logger.Information("ETW Capture Engine stopped. Total events: {Count}", _eventsProcessed);
        }

        public void Dispose()
        {
            StopAsync().Wait();
            _cancellationTokenSource.Dispose();
        }
    }
}
