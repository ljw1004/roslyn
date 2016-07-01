using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Formatting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MefRegistration
{
    using System.ComponentModel.Composition;
    using Microsoft.VisualStudio.Utilities;
    using Microsoft.VisualStudio.Text.Editor;

    [Export(typeof(IWpfTextViewCreationListener)), ContentType("text"), TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class ReplayAdornmentTextViewCreationListener : IWpfTextViewCreationListener
    {
        // This class will be instantiated the first time a text document is opened. (That's because our VSIX manifest
        // lists this project, i.e. this DLL, and VS scans all such listed DLLs to find all types with the right attributes).
        // The TextViewCreated event will be raised each time a text document tab is created. It won't be
        // raised for subsequent re-activation of an existing document tab.
        public void TextViewCreated(IWpfTextView textView) => new ReplayAdornment(textView);

#pragma warning disable CS0169 // C# warning "the field editorAdornmentLayer is never used" -- but it is used, by MEF!
        [Export(typeof(AdornmentLayerDefinition)), Name(nameof(ReplayAdornment)), Order(After = PredefinedAdornmentLayers.Selection, Before = Microsoft.VisualStudio.Text.Editor.PredefinedAdornmentLayers.Text)]
        private AdornmentLayerDefinition editorAdornmentLayer;
#pragma warning restore CS0169
    }
}


internal sealed class ReplayAdornment
{
    // VS integration
    readonly Microsoft.VisualStudio.Text.Editor.IWpfTextView View;
    Workspace Workspace;
    DocumentId DocumentId;

    // Replay
    ReplayHostManager ReplayManager;
    VersionStamp ReplayDocumentVersionStamp;



    public ReplayAdornment(Microsoft.VisualStudio.Text.Editor.IWpfTextView view)
    {
        var componentModel = (Microsoft.VisualStudio.ComponentModelHost.IComponentModel)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(Microsoft.VisualStudio.ComponentModelHost.SComponentModel));
        Workspace = componentModel.GetService<Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace>();

        View = view;
        View.LayoutChanged += OnLayoutChanged;
        View.ViewportWidthChanged += OnViewportWidthChanged;
        View.Closed += OnClosed;
    }

    private void OnClosed(object sender, EventArgs e)
    {
        ReplayManager.LineChanged -= OnReplayLineChanged;
        if (ReplayManager != null) ReplayManager.Dispose(); ReplayManager = null;
    }

    internal void OnLayoutChanged(object sender, Microsoft.VisualStudio.Text.Editor.TextViewLayoutChangedEventArgs e)
    {
        // Raised whenever the rendered text displayed in the ITextView changes - whenever the view does a layout
        // (which happens when DisplayTextLineContainingBufferPosition is called or in response to text or classification
        // changes), and also when the view scrolls or when its size changes.
        // Responsible for adding the adornment to any reformatted lines.

        if (DocumentId == null)
        {
            var dte = Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            var activeDocument = dte?.ActiveDocument; // sometimes we're constructed/invoked before ActiveDocument has been set
            if (activeDocument != null) DocumentId = Workspace.CurrentSolution.GetDocumentIdsWithFilePath(activeDocument.FullName).FirstOrDefault();
            if (DocumentId == null) return;
        }

        var document = Workspace.CurrentSolution.GetDocument(DocumentId);
        if (document == null) return;
        if (ReplayManager == null)
        {
            ReplayManager = ReplayHostManager.Create(document.Project);
            ReplayManager.LineChanged += OnReplayLineChanged;
        }
        VersionStamp docVersion; if (!document.TryGetTextVersion(out docVersion)) return;
        if (docVersion != ReplayDocumentVersionStamp)
        {
            ReplayDocumentVersionStamp = docVersion;
            ReplayManager.TriggerReplayAsync(document);
        }

        var start = View.TextViewLines.FirstVisibleLine.Start.GetContainingLine().LineNumber;
        var end = View.TextViewLines.LastVisibleLine.End.GetContainingLine().LineNumber;
        ReplayManager.WatchAndMissing(start, end - start, e.NewOrReformattedLines.Select(line => line.Start.GetContainingLine().LineNumber));
    }


    private void OnReplayLineChanged(int line, string msg)
    {
        View.VisualElement.Dispatcher.BeginInvoke((Action)delegate
        {
            var existing = View.GetAdornmentLayer(nameof(ReplayAdornment)).Elements.FirstOrDefault(e => (int)e.Tag == line) as TextBlock;
            if (existing != null) { existing.Text = msg; return; }
            var snapshotLine = View.TextSnapshot.GetLineFromLineNumber(line);
            var span = new SnapshotSpan(snapshotLine.Start, snapshotLine.End);
            var geometry = View.TextViewLines.GetMarkerGeometry(span);
            if (geometry == null) return;
            var adornment = new TextBlock { Width = 240, Height = geometry.Bounds.Height, Background = Brushes.Yellow, Opacity = 0.2, Text = $"// {msg}" };
            Canvas.SetLeft(adornment, View.ViewportWidth - adornment.Width);
            Canvas.SetTop(adornment, geometry.Bounds.Top);
            View.GetAdornmentLayer(nameof(ReplayAdornment)).AddAdornment(Microsoft.VisualStudio.Text.Editor.AdornmentPositioningBehavior.TextRelative, span, line, adornment, null);
        });
    }

    private void OnViewportWidthChanged(object sender, EventArgs e)
    {
        var elements = View?.GetAdornmentLayer(nameof(ReplayAdornment))?.Elements;
        if (elements == null) return;
        foreach (var element in elements)
        {
            var adornment = element.Adornment as FrameworkElement;
            if (adornment != null) Canvas.SetLeft(adornment, View.ViewportWidth - adornment.Width);
        }
    }

}


class ReplayHostManager : IDisposable
{
    static Dictionary<string, ReplayHostManager> projects = new Dictionary<string, ReplayHostManager>();

    int RefCount;
    string ProjectOutputFilePath;
    CancellationTokenSource Cancel;
    Task Task;
    //
    int WatchLine, WatchLineCount; ImmutableArray<int> WatchMissing;
    TaskCompletionSource<object> WatchChanged;
    public event Action<int, string> LineChanged;

    public void TriggerReplayAsync(Document document)
    {
        Cancel?.Cancel();
        Cancel = new CancellationTokenSource();
        Task = ReplayInnerAsync(document, Task, Cancel.Token);
    }

    public void WatchAndMissing(int line, int lineCount, IEnumerable<int> missing)
    {
        WatchLine = line;
        WatchLineCount = lineCount;
        WatchMissing = missing.ToImmutableArray();
        WatchChanged?.TrySetResult(null);
    }

    async Task ReplayInnerAsync(Document document, Task prevTask, CancellationToken cancel)
    {
        if (prevTask != null) try { await prevTask.ConfigureAwait(false); } catch (Exception) { }
        if (document == null) return;
        if (!File.Exists(document.Project.OutputFilePath)) return; // if the user has done at least one build, then all needed DLLs will likely be in place

        var project = await ReplayHost.InstrumentAsync(document.Project, cancel).ConfigureAwait(false);
        var results = await ReplayHost.BuildAsync(project, cancel).ConfigureAwait(false);
        if (!results.Success) return; // don't wipe out the existing results in case of error
        var host = await ReplayHost.RunAsync(results.ReplayOutputFilePath, cancel).ConfigureAwait(false);
        if (WatchLineCount != 0) host.WatchAndMissing(document.FilePath, WatchLine, WatchLineCount, null);
        WatchChanged = new TaskCompletionSource<object>();
        var replayTask = host.ReadReplayAsync(cancel);
        while (true)
        {
            await Task.WhenAny(WatchChanged.Task, replayTask).ConfigureAwait(false);
            if (WatchChanged.Task.IsCompleted)
            {
                host.WatchAndMissing(document.FilePath, WatchLine, WatchLineCount, WatchMissing);
                WatchChanged = new TaskCompletionSource<object>();
            }
            else if (replayTask.IsCompleted)
            {
                var replay = await replayTask.ConfigureAwait(false);
                if (replay == null) return;
                LineChanged?.Invoke(replay.Item1, replay.Item2);
                replayTask = host.ReadReplayAsync(cancel);
            }
        }
    }

    public static ReplayHostManager Create(Project project)
    {
        if (project == null || project.OutputFilePath == null) throw new ArgumentNullException(nameof(project));
        lock (projects)
        {
            ReplayHostManager r;
            if (projects.TryGetValue(project.OutputFilePath, out r)) { r.RefCount++; return r; }
            r = new ReplayHostManager { RefCount = 1, ProjectOutputFilePath = project.OutputFilePath };
            projects[project.OutputFilePath] = r;
            return r;
        }
    }

    public void Dispose()
    {
        // TODO: this is weird that I'm using the same instance of ReplayManager. Imagine if someone
        // calls ForProject() and then disposes of it twice. The proper result is that they should only
        // decrement the refcount once (since Dispose is meant to be idempotent). But what will happen
        // here is it'll be decremented twice.
        if (ProjectOutputFilePath == null) return;
        lock (this)
        {
            RefCount--;
            if (RefCount == 0) { lock (projects) projects.Remove(ProjectOutputFilePath); ProjectOutputFilePath = null; Cancel?.Cancel(); }
        }
    }

}

