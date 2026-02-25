using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Northstar.Desktop.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Northstar.Desktop.Views;

public partial class MainWindow
{
    private async void OpenTerminalButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.SelectedTab is null)
        {
            return;
        }

        var session = GetOrCreateTerminalSession(viewModel.SelectedTab);
        _activeTerminalSession = session;

        if (session.IsVisible)
        {
            SetEmbeddedTerminalVisible(false, session);
            ExplorerList.Focus();
            return;
        }

        var started = await EnsureEmbeddedTerminalStartedAsync(viewModel, session);
        if (!started)
        {
            viewModel.OpenInTerminalCommand.Execute(null);
            return;
        }

        RenderTerminalSession(session);
        SetEmbeddedTerminalVisible(true, session);
        Dispatcher.UIThread.Post(() => TerminalInputBox.Focus());
    }

    private void TerminalCloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_activeTerminalSession is null)
        {
            return;
        }

        SetEmbeddedTerminalVisible(false, _activeTerminalSession);
        ExplorerList.Focus();
    }

    private void TerminalClearButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_activeTerminalSession is null)
        {
            return;
        }

        ClearTerminalSession(_activeTerminalSession);
        RenderTerminalSession(_activeTerminalSession);
        TerminalInputBox.Focus();
    }

    private async void TerminalInputBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox inputBox || _activeTerminalSession is null)
        {
            return;
        }

        if (e.Key == Key.Tab)
        {
            _ = TryApplyTerminalCompletion(_activeTerminalSession, inputBox);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            var command = inputBox.Text ?? string.Empty;
            inputBox.Text = string.Empty;
            ResetTerminalCompletionCycle(_activeTerminalSession);
            await SendTerminalInputAsync(_activeTerminalSession, command + Environment.NewLine);
            e.Handled = true;
            return;
        }

        if ((e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)) && e.Key == Key.C)
        {
            ResetTerminalCompletionCycle(_activeTerminalSession);
            await SendTerminalInputAsync(_activeTerminalSession, "\u0003");
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End))
        {
            ResetTerminalCompletionCycle(_activeTerminalSession);
        }
    }

    private void SetEmbeddedTerminalVisible(bool isVisible, TerminalTabSession? session = null)
    {
        var targetSession = session ?? _activeTerminalSession;
        if (targetSession is not null)
        {
            targetSession.IsVisible = isVisible;
        }

        EmbeddedTerminalPane.IsVisible = isVisible;

        if (RootLayoutGrid.RowDefinitions.Count > 4)
        {
            RootLayoutGrid.RowDefinitions[4].Height = isVisible
                ? new GridLength(280)
                : new GridLength(0);
        }

        TerminalToggleButton.Content = isVisible ? "⌨ Hide Terminal" : "⌨ Open Terminal";
    }

    private void SwitchTerminalSessionForSelectedTab()
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.SelectedTab is null)
        {
            _activeTerminalSession = null;
            TerminalOutputText.Inlines?.Clear();
            TerminalOutputText.Text = string.Empty;
            TerminalShellLabel.Text = string.Empty;
            SetEmbeddedTerminalVisible(false);
            return;
        }

        var session = GetOrCreateTerminalSession(viewModel.SelectedTab);
        _activeTerminalSession = session;
        RenderTerminalSession(session);
        SetEmbeddedTerminalVisible(session.IsVisible, session);
    }

    private async Task<bool> EnsureEmbeddedTerminalStartedAsync(MainWindowViewModel viewModel, TerminalTabSession session)
    {
        if (session.Process is { HasExited: false })
        {
            return true;
        }

        await _terminalStartGate.WaitAsync();
        try
        {
            if (session.Process is { HasExited: false })
            {
                return true;
            }

            ShutdownEmbeddedTerminal(session);

            var shellPath = ResolveUserShellPath();
            session.ShellPath = shellPath;

            var workingDirectory = session.Tab.Path;
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                workingDirectory = viewModel.CurrentPath;
            }

            AppendTerminalOutput(session, $"Starting shell in {workingDirectory}{Environment.NewLine}");

            if (!TryStartTerminalProcess(shellPath, workingDirectory, usePtyWrapper: true, out var process) &&
                !TryStartTerminalProcess(shellPath, workingDirectory, usePtyWrapper: false, out process))
            {
                AppendTerminalOutput(session, $"Failed to start embedded shell ({shellPath}).{Environment.NewLine}");
                return false;
            }

            session.Process = process;
            session.ReadCts = new CancellationTokenSource();
            _ = ReadTerminalStreamAsync(session, process!.StandardOutput, session.ReadCts.Token);
            _ = ReadTerminalStreamAsync(session, process.StandardError, session.ReadCts.Token);
            _ = ConfigureTerminalPathSyncHookAsync(session, shellPath);
            return true;
        }
        finally
        {
            _terminalStartGate.Release();
        }
    }

    private bool TryStartTerminalProcess(string shellPath, string workingDirectory, bool usePtyWrapper, out Process? process)
    {
        process = null;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Directory.Exists(workingDirectory)
                    ? workingDirectory
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };

            if (usePtyWrapper)
            {
                startInfo.FileName = "script";
                startInfo.ArgumentList.Add("-q");
                startInfo.ArgumentList.Add("/dev/null");
                startInfo.ArgumentList.Add(shellPath);
                startInfo.ArgumentList.Add("-l");
            }
            else
            {
                startInfo.FileName = shellPath;
                startInfo.ArgumentList.Add("-l");
            }

            process = Process.Start(startInfo);
            return process is not null;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveUserShellPath()
    {
        var shellPath = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrWhiteSpace(shellPath) && File.Exists(shellPath))
        {
            return shellPath;
        }

        if (File.Exists("/bin/bash"))
        {
            return "/bin/bash";
        }

        return "/bin/zsh";
    }

    private async Task ConfigureTerminalPathSyncHookAsync(TerminalTabSession session, string shellPath)
    {
        var shellName = Path.GetFileName(shellPath).ToLowerInvariant();
        var hookCommand = shellName switch
        {
            "bash" => "function __ns_pwd_hook(){ printf '\\036%s\\037' \"$PWD\"; }; " +
                      "if [[ -n \"$PROMPT_COMMAND\" ]]; then " +
                      "PROMPT_COMMAND=\"__ns_pwd_hook;$PROMPT_COMMAND\"; " +
                      "else PROMPT_COMMAND=\"__ns_pwd_hook\"; fi",
            "zsh" => "function __ns_pwd_hook(){ printf '\\036%s\\037' \"$PWD\"; }; " +
                     "typeset -ga precmd_functions; " +
                     "if (( ${precmd_functions[(I)__ns_pwd_hook]} == 0 )); then " +
                     "precmd_functions+=(__ns_pwd_hook); fi",
            _ => string.Empty,
        };

        if (string.IsNullOrWhiteSpace(hookCommand))
        {
            return;
        }

        await SendTerminalInputAsync(session, hookCommand + Environment.NewLine);
    }

    private async Task SendTerminalInputAsync(TerminalTabSession session, string input)
    {
        try
        {
            if (session.Process is null || session.Process.HasExited)
            {
                return;
            }

            await session.Process.StandardInput.WriteAsync(input);
            await session.Process.StandardInput.FlushAsync();
        }
        catch
        {
            AppendTerminalOutput(session, $"{Environment.NewLine}[terminal input failed]{Environment.NewLine}");
        }
    }

    private bool TryApplyTerminalCompletion(TerminalTabSession session, TextBox inputBox)
    {
        var text = inputBox.Text ?? string.Empty;
        var caret = Math.Clamp(inputBox.CaretIndex, 0, text.Length);
        var tokenStart = caret;
        while (tokenStart > 0 && !char.IsWhiteSpace(text[tokenStart - 1]))
        {
            tokenStart--;
        }

        var token = text[tokenStart..caret];
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var currentDirectory = session.Tab.Path;
        if (string.IsNullOrWhiteSpace(currentDirectory) || !Directory.Exists(currentDirectory))
        {
            currentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        var (lookupDirectory, pathPrefix, namePrefix) = ResolveCompletionContext(token, currentDirectory);
        if (string.IsNullOrWhiteSpace(lookupDirectory) || !Directory.Exists(lookupDirectory))
        {
            return false;
        }

        var matches = Directory.EnumerateFileSystemEntries(lookupDirectory)
            .Select(path => new
            {
                Name = Path.GetFileName(path),
                IsDirectory = Directory.Exists(path),
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name) &&
                            entry.Name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Count == 0)
        {
            return false;
        }

        var contextKey = $"{lookupDirectory}\n{pathPrefix}\n{namePrefix}\n{text[..tokenStart]}\n{text[caret..]}";
        if (!string.Equals(contextKey, session.CompletionContextKey, StringComparison.Ordinal))
        {
            session.CompletionMatches.Clear();
            session.CompletionMatches.AddRange(matches.Select(entry =>
                pathPrefix + entry.Name + (entry.IsDirectory ? "/" : string.Empty)));
            session.CompletionContextKey = contextKey;
            session.CompletionIndex = 0;
        }
        else
        {
            session.CompletionIndex = (session.CompletionIndex + 1) % session.CompletionMatches.Count;
        }

        var replacement = session.CompletionMatches[session.CompletionIndex];
        var updatedText = text[..tokenStart] + replacement + text[caret..];
        inputBox.Text = updatedText;
        inputBox.CaretIndex = tokenStart + replacement.Length;
        return true;
    }

    private static (string LookupDirectory, string PathPrefix, string NamePrefix) ResolveCompletionContext(string token, string currentDirectory)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var normalizedToken = token.Replace('\\', '/');
        var lastSeparator = normalizedToken.LastIndexOf('/');
        if (lastSeparator < 0)
        {
            return (currentDirectory, string.Empty, normalizedToken);
        }

        var prefixPart = normalizedToken[..(lastSeparator + 1)];
        var namePrefix = normalizedToken[(lastSeparator + 1)..];
        var directoryToken = prefixPart;
        if (directoryToken.StartsWith("~/", StringComparison.Ordinal))
        {
            directoryToken = Path.Combine(home, directoryToken[2..]);
        }

        string lookupDirectory;
        if (Path.IsPathRooted(directoryToken))
        {
            lookupDirectory = Path.GetFullPath(directoryToken);
        }
        else
        {
            lookupDirectory = Path.GetFullPath(Path.Combine(currentDirectory, directoryToken));
        }

        return (lookupDirectory, prefixPart, namePrefix);
    }

    private void ResetTerminalCompletionCycle(TerminalTabSession session)
    {
        session.CompletionContextKey = string.Empty;
        session.CompletionIndex = -1;
        session.CompletionMatches.Clear();
    }

    private async Task ReadTerminalStreamAsync(TerminalTabSession session, StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[2048];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read <= 0)
                {
                    break;
                }

                var chunk = new string(buffer, 0, read);
                AppendTerminalOutput(session, chunk);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when terminal session is being shut down.
        }
        catch
        {
            AppendTerminalOutput(session, $"{Environment.NewLine}[terminal output stream ended unexpectedly]{Environment.NewLine}");
        }
    }

    private void AppendTerminalOutput(TerminalTabSession session, string rawText)
    {
        if (string.IsNullOrEmpty(rawText))
        {
            return;
        }

        var visibleText = ExtractAndApplyTerminalPathMarkers(session, rawText);
        if (string.IsNullOrEmpty(visibleText))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var segment in ParseAnsiSegments(session, visibleText))
            {
                if (segment.Length == 0)
                {
                    continue;
                }

                session.Segments.Add(segment);
                session.OutputCharCount += segment.Length;
            }

            const int maxOutputChars = 160_000;
            if (session.OutputCharCount > maxOutputChars)
            {
                session.Segments.Clear();
                session.OutputCharCount = 0;
                session.CurrentAnsiColor = -1;
                session.AnsiBold = false;
                session.Segments.Add(new TerminalTextSegment("[terminal output truncated]" + Environment.NewLine, 90, false));
            }

            if (ReferenceEquals(_activeTerminalSession, session))
            {
                RenderTerminalSession(session);
            }
        });
    }

    private string ExtractAndApplyTerminalPathMarkers(TerminalTabSession session, string rawText)
    {
        var visible = new StringBuilder(rawText.Length);

        foreach (var ch in rawText)
        {
            if (session.CapturingPathMarker)
            {
                if (ch == TerminalPathMarkerEnd)
                {
                    var path = session.PathMarkerBuffer.ToString().Trim();
                    session.PathMarkerBuffer.Clear();
                    session.CapturingPathMarker = false;
                    TrySyncExplorerPathFromTerminal(session, path);
                    continue;
                }

                session.PathMarkerBuffer.Append(ch);
                continue;
            }

            if (ch == TerminalPathMarkerStart)
            {
                session.CapturingPathMarker = true;
                session.PathMarkerBuffer.Clear();
                continue;
            }

            visible.Append(ch);
        }

        return visible.ToString();
    }

    private void TrySyncExplorerPathFromTerminal(TerminalTabSession session, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(path);
        }
        catch
        {
            return;
        }

        if (!Directory.Exists(normalizedPath))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            session.Tab.Path = normalizedPath;
            session.Tab.Title = BuildTabTitleFromPath(normalizedPath);

            if (DataContext is not MainWindowViewModel viewModel || viewModel.SelectedTab is null)
            {
                return;
            }

            if (!ReferenceEquals(viewModel.SelectedTab, session.Tab))
            {
                return;
            }

            if (string.Equals(viewModel.CurrentPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            viewModel.NavigateToPath(normalizedPath);
        });
    }

    private IEnumerable<TerminalTextSegment> ParseAnsiSegments(TerminalTabSession session, string chunk)
    {
        if (session.AnsiPendingBuffer.Length > 0)
        {
            session.AnsiPendingBuffer.Append(chunk);
            chunk = session.AnsiPendingBuffer.ToString();
            session.AnsiPendingBuffer.Clear();
        }

        var textBuffer = new StringBuilder();
        var index = 0;
        while (index < chunk.Length)
        {
            var current = chunk[index];
            if (current == '\u001b')
            {
                if (textBuffer.Length > 0)
                {
                    var text = textBuffer.ToString().Replace("\r\n", "\n").Replace('\r', '\n');
                    yield return new TerminalTextSegment(text, session.CurrentAnsiColor, session.AnsiBold);
                    textBuffer.Clear();
                }

                if (index + 1 >= chunk.Length)
                {
                    session.AnsiPendingBuffer.Append(chunk[index]);
                    yield break;
                }

                if (chunk[index + 1] != '[')
                {
                    index++;
                    continue;
                }

                var commandIndex = index + 2;
                while (commandIndex < chunk.Length && chunk[commandIndex] != 'm')
                {
                    commandIndex++;
                }

                if (commandIndex >= chunk.Length)
                {
                    session.AnsiPendingBuffer.Append(chunk[index..]);
                    yield break;
                }

                var commandPayload = chunk[(index + 2)..commandIndex];
                ApplyAnsiSgrCommand(session, commandPayload);
                index = commandIndex + 1;
                continue;
            }

            textBuffer.Append(current);
            index++;
        }

        if (textBuffer.Length > 0)
        {
            var text = textBuffer.ToString().Replace("\r\n", "\n").Replace('\r', '\n');
            yield return new TerminalTextSegment(text, session.CurrentAnsiColor, session.AnsiBold);
        }
    }

    private void ApplyAnsiSgrCommand(TerminalTabSession session, string payload)
    {
        var codes = string.IsNullOrWhiteSpace(payload)
            ? ["0"]
            : payload.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var raw in codes)
        {
            if (!int.TryParse(raw, out var code))
            {
                continue;
            }

            switch (code)
            {
                case 0:
                    session.CurrentAnsiColor = -1;
                    session.AnsiBold = false;
                    break;
                case 1:
                    session.AnsiBold = true;
                    break;
                case 22:
                    session.AnsiBold = false;
                    break;
                case 39:
                    session.CurrentAnsiColor = -1;
                    break;
                default:
                    if ((code >= 30 && code <= 37) || (code >= 90 && code <= 97))
                    {
                        session.CurrentAnsiColor = code;
                    }
                    break;
            }
        }
    }

    private static IBrush ResolveTerminalForeground(int colorCode, bool bold)
    {
        return colorCode switch
        {
            30 => new SolidColorBrush(Color.Parse("#2D323A")),
            31 => new SolidColorBrush(Color.Parse("#E06C75")),
            32 => new SolidColorBrush(Color.Parse("#98C379")),
            33 => new SolidColorBrush(Color.Parse("#E5C07B")),
            34 => new SolidColorBrush(Color.Parse("#61AFEF")),
            35 => new SolidColorBrush(Color.Parse("#C678DD")),
            36 => new SolidColorBrush(Color.Parse("#56B6C2")),
            37 => new SolidColorBrush(Color.Parse("#D7E6FF")),
            90 => new SolidColorBrush(Color.Parse("#6B778D")),
            91 => new SolidColorBrush(Color.Parse("#FF7B86")),
            92 => new SolidColorBrush(Color.Parse("#B8E986")),
            93 => new SolidColorBrush(Color.Parse("#FFD885")),
            94 => new SolidColorBrush(Color.Parse("#84C7FF")),
            95 => new SolidColorBrush(Color.Parse("#D89AFF")),
            96 => new SolidColorBrush(Color.Parse("#87E6F6")),
            97 => new SolidColorBrush(Color.Parse("#F0F7FF")),
            _ => bold
                ? new SolidColorBrush(Color.Parse("#EAF2FF"))
                : new SolidColorBrush(Color.Parse("#D7E6FF")),
        };
    }

    private void RenderTerminalSession(TerminalTabSession session)
    {
        var inlineCollection = TerminalOutputText.Inlines;
        if (inlineCollection is null)
        {
            return;
        }

        inlineCollection.Clear();
        foreach (var segment in session.Segments)
        {
            if (segment.Length == 0)
            {
                continue;
            }

            var run = new Run(segment.Text)
            {
                Foreground = ResolveTerminalForeground(segment.ColorCode, segment.Bold),
            };

            if (segment.Bold)
            {
                run.FontWeight = FontWeight.SemiBold;
            }

            inlineCollection.Add(run);
        }

        TerminalShellLabel.Text = string.IsNullOrWhiteSpace(session.ShellPath)
            ? string.Empty
            : $"shell: {Path.GetFileName(session.ShellPath)}";

        if (TerminalOutputScrollViewer.Extent.Height > 0)
        {
            TerminalOutputScrollViewer.Offset = new Vector(
                TerminalOutputScrollViewer.Offset.X,
                TerminalOutputScrollViewer.Extent.Height);
        }
    }

    private TerminalTabSession GetOrCreateTerminalSession(ExplorerTabViewModel tab)
    {
        if (_terminalSessions.TryGetValue(tab, out var existing))
        {
            return existing;
        }

        var created = new TerminalTabSession(tab);
        _terminalSessions[tab] = created;
        return created;
    }

    private void ClearTerminalSession(TerminalTabSession session)
    {
        session.AnsiPendingBuffer.Clear();
        session.OutputCharCount = 0;
        session.CurrentAnsiColor = -1;
        session.AnsiBold = false;
        session.Segments.Clear();
        session.CapturingPathMarker = false;
        session.PathMarkerBuffer.Clear();
        ResetTerminalCompletionCycle(session);
    }

    private void ShutdownEmbeddedTerminal(TerminalTabSession session)
    {
        try
        {
            ClearTerminalSession(session);

            session.ReadCts?.Cancel();
            session.ReadCts?.Dispose();
            session.ReadCts = null;

            if (session.Process is not null)
            {
                if (!session.Process.HasExited)
                {
                    try
                    {
                        session.Process.StandardInput.WriteLine("exit");
                        session.Process.StandardInput.Flush();
                    }
                    catch
                    {
                        // Ignore write failures while shutting down.
                    }

                    session.Process.WaitForExit(350);
                    if (!session.Process.HasExited)
                    {
                        session.Process.Kill(entireProcessTree: true);
                    }
                }

                session.Process.Dispose();
                session.Process = null;
            }
        }
        catch
        {
            // Ignore shutdown errors for terminal process cleanup.
        }
    }

    private void ShutdownAllEmbeddedTerminals()
    {
        foreach (var session in _terminalSessions.Values)
        {
            ShutdownEmbeddedTerminal(session);
        }

        _terminalSessions.Clear();
        _activeTerminalSession = null;
    }

    private static string BuildTabTitleFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Tab";
        }

        var normalizedPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(normalizedPath);
        if (string.Equals(root, normalizedPath, StringComparison.OrdinalIgnoreCase))
        {
            return "Macintosh HD";
        }

        var fileName = Path.GetFileName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(fileName) ? normalizedPath : fileName;
    }

    private readonly record struct TerminalTextSegment(string Text, int ColorCode, bool Bold)
    {
        public int Length => Text.Length;
    }

    private sealed class TerminalTabSession
    {
        public TerminalTabSession(ExplorerTabViewModel tab)
        {
            Tab = tab;
        }

        public ExplorerTabViewModel Tab { get; }
        public bool IsVisible { get; set; }
        public Process? Process { get; set; }
        public CancellationTokenSource? ReadCts { get; set; }
        public string ShellPath { get; set; } = string.Empty;

        public List<TerminalTextSegment> Segments { get; } = [];
        public int OutputCharCount { get; set; }
        public int CurrentAnsiColor { get; set; } = -1;
        public bool AnsiBold { get; set; }
        public StringBuilder AnsiPendingBuffer { get; } = new();

        public List<string> CompletionMatches { get; } = [];
        public string CompletionContextKey { get; set; } = string.Empty;
        public int CompletionIndex { get; set; } = -1;

        public bool CapturingPathMarker { get; set; }
        public StringBuilder PathMarkerBuffer { get; } = new();
    }
}
