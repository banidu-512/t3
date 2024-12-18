﻿using System.IO;
using System.Text;
using System.Text.Json;
using ImGuiNET;
using Sentry;
using T3.Core.Animation;
using T3.Core.Compilation;
using T3.Core.SystemUi;
using T3.Editor.Gui.AutoBackup;
using T3.Editor.Gui.Commands;
using T3.Editor.Gui.Graph;
using T3.Editor.Gui.Graph.Modification;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.SystemUi;
using T3.Editor.UiModel;

namespace T3.Editor;

internal static class CrashReporting
{
    public static void InitializeCrashReporting()
    {
        SentrySdk.Init(o =>
                       {
                           // Tells which project in Sentry to send events to:
                           o.Dsn = "https://52e37e10dc9cebcc2328cc63fab57211@o4505681078059008.ingest.sentry.io/4505681082384384";
                           o.Debug = false;
                           o.TracesSampleRate = 0.0;
                           o.IsGlobalModeEnabled = true;
                           o.SendClientReports = false;
                           o.AutoSessionTracking = false;
                           o.SendDefaultPii = false;
                           o.Release = Program.Version.ToBasicVersionString();
                           o.SetBeforeSend((Func<SentryEvent, SentryHint, SentryEvent>)CrashHandler);
                       });

        //SentrySdk.ConfigureScope(scope => { scope.SetTag("IsStandAlone", Program.IsStandAlone ? "Yes" : "No"); });

        var configuration = "Release";
        #if DEBUG
        configuration = "Debug";
        #endif

        SentrySdk.ConfigureScope(scope => { scope.SetTag("Configuration", configuration); });
    }

    private static SentryEvent CrashHandler(SentryEvent sentryEvent, SentryHint hint)
    {
        // Aggregate exception normally don't cause crashes
        if (sentryEvent.Exception is AggregateException)
            return null;

        var timeOfLastBackup = AutoBackup.GetTimeOfLastBackup();
        var timeSpan = DrawUtils.GetReadableRelativeTime(timeOfLastBackup);

        var components = GraphWindow.Focused?.Components;
        
        sentryEvent.SetTag("Nickname", UserSettings.Config.UserName);
        sentryEvent.Contexts["tooll3"]= new
                                            {
                                                UndoStack = UndoRedoStack.GetUndoStackAsString(),
                                                Selection = components == null ? string.Empty : string.Join("\n", components.NodeSelection),
                                                Nickname = "",
                                                RuntimeSeconds = Playback.RunTimeInSecs,
                                                RuntimeFrames = ImGui.GetFrameCount(),
                                                UndoActions = UndoRedoStack.UndoStack.Count,
                                            };
        
        string? json = null;
        try
        {
            var primaryComposition = GraphWindow.Focused?.Components.CompositionOp;
            if (primaryComposition != null)
            {
                var compositionUi = primaryComposition.Symbol.GetSymbolUi();
                GraphOperations.TryCopyNodesAsJson(primaryComposition,
                                                   compositionUi.ChildUis.Values,
                                                   compositionUi.Annotations.Values.ToList(),
                                                   out json);
            }
        }
        catch (Exception e)
        {
            sentryEvent.SetExtra("CurrentOpExportFailed", e.Message);
        }

        WriteCrashReportFile(sentryEvent);

        // We only show crash report dialog in release mode 
        #if RELEASE
        string message = "On noooo, how embarrassing! T3 just crashed\n" +
                         $"Last backup was saved {timeSpan} to .t3/backups/\n" +
                         "Please consult the Wiki on what to do next.";
        
        if (json != null)
        {
            message += "\n\n" + "When this window closes, the current operator will be copied to your clipboard. " +
                       "You can use it to troubleshoot/reproduce the issue.";
        }
        
        message += "\n\n" + (sentryEvent.Exception?.ToString() ?? Environment.StackTrace);
        
        const string confirmation = "Send crash report (it really helps!)";
        var result = BlockingWindow.Instance.ShowMessageBox(message, @"☠🙈 Damn!", confirmation, "No thanks, I hate it when things are fixed");
        
        if (json != null)
        {
            EditorUi.Instance.SetClipboardText(json);
        }
        
        var sendingEnabled = result == confirmation;

        if (!string.IsNullOrWhiteSpace(LogPath))
        {
            CoreUi.Instance.OpenWithDefaultApplication(LogPath);
        }
        
        return sendingEnabled ? sentryEvent : null;
        #else
        WriteReportToLog(sentryEvent, false);
        CoreUi.Instance.SetUnhandledExceptionMode(true);
        return null;
        #endif
    }
    
    private static void WriteReportToLog(SentryEvent sentryEvent, bool sendingEnabled)
    {
        // Write formatted stacktrace for readability
        if (sentryEvent.Exception != null)
        {
            Log.Warning($"{sentryEvent.Exception.Message}\n{sentryEvent.Exception}");
        }

        // Dump report as json
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        sentryEvent.WriteTo(writer, null);
        writer.Flush();

        var eventJson = Encoding.UTF8.GetString(stream.ToArray());
        Log.Warning($"Encountered crash reported:{sendingEnabled}\n" + eventJson);
        
        // Force writing
        FileWriter.Flush();
    }
    
    /** Additional to logging the crash we also write a copy to a dedicated crash file. */
    private static void WriteCrashReportFile(SentryEvent sentryEvent)
    {
        if (sentryEvent?.Exception == null)
            return;
        
        var exceptionTitle = sentryEvent.Exception.GetType().Name;
        var filepath= Path.Combine(FileWriter.Instance.LogDirectory,$@"crash {DateTime.Now:yyyy-MM-dd  HH-mm-ss} - {exceptionTitle}.txt");
        
        using var streamFileWriter = new StreamWriter(filepath);
        streamFileWriter.WriteLine($"{sentryEvent.Exception.Message}\n{sentryEvent.Exception}");
        
        using var memoryStream = new MemoryStream();
        using var jsonWriter = new Utf8JsonWriter(memoryStream, new JsonWriterOptions { Indented = true });

        sentryEvent.WriteTo(jsonWriter, null);
        jsonWriter.Flush();

        streamFileWriter.WriteLine(Encoding.UTF8.GetString(memoryStream.ToArray()));
        streamFileWriter.Flush();
    }

    public static string LogPath { get; set; }
}