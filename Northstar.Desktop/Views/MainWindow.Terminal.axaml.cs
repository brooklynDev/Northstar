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
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (EmbeddedTerminalPane.IsVisible)
        {
            SetEmbeddedTerminalVisible(false);
            ExplorerList.Focus();
            return;
        }

        var started = await EnsureEmbeddedTerminalStartedAsync(viewModel);
        if (!started)
        {
            viewModel.OpenInTerminalCommand.Execute(null);
            return;
        }

        SetEmbeddedTerminalVisible(true);
        Dispatcher.UIThread.Post(() => TerminalInputBox.Focus());
    }

    private void TerminalCloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        SetEmbeddedTerminalVisible(false);
        ExplorerList.Focus();
    }

    private void TerminalClearButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _ansiPendingBuffer.Clear();
        _terminalOutputCharCount = 0;
        _terminalCurrentAnsiColor = -1;
        _terminalAnsiBold = false;
        TerminalOutputText.Inlines?.Clear();
        TerminalOutputText.Text = string.Empty;
        ResetTerminalCompletionCycle();
        TerminalInputBox.Focus();
    }

    private async void TerminalInputBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox inputBox)
        {
            return;
        }

        if (e.Key == Key.Tab)
        {
            _ = TryApplyTerminalCompletion(inputBox);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            var command = inputBox.Text ?? string.Empty;
            inputBox.Text = string.Empty;
            ResetTerminalCompletionCycle();
            await SendTerminalInputAsync(command + Environment.NewLine);
            e.Handled = true;
            return;
        }

        if ((e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)) && e.Key == Key.C)
        {
            ResetTerminalCompletionCycle();
            await SendTerminalInputAsync("\u0003");
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End))
        {
            ResetTerminalCompletionCycle();
        }
    }

    private void SetEmbeddedTerminalVisible(bool isVisible)
    {
        EmbeddedTerminalPane.IsVisible = isVisible;

        if (RootLayoutGrid.RowDefinitions.Count > 3)
        {
            RootLayoutGrid.RowDefinitions[3].Height = isVisible
                ? new GridLength(280)
                : new GridLength(0);
        }

        TerminalToggleButton.Content = isVisible ? "⌨ Hide Terminal" : "⌨ Open Terminal";
    }

    private async Task<bool> EnsureEmbeddedTerminalStartedAsync(MainWindowViewModel viewModel)
    {
        if (_embeddedTerminalProcess is { HasExited: false })
        {
            return true;
        }

        await _terminalStartGate.WaitAsync();
        try
        {
            if (_embeddedTerminalProcess is { HasExited: false })
            {
                return true;
            }

            ShutdownEmbeddedTerminal();

            var shellPath = ResolveUserShellPath();
            TerminalShellLabel.Text = $"shell: {Path.GetFileName(shellPath)}";
            AppendTerminalOutput($"Starting shell in {viewModel.CurrentPath}{Environment.NewLine}");

            if (!TryStartTerminalProcess(shellPath, viewModel.CurrentPath, usePtyWrapper: true) &&
                !TryStartTerminalProcess(shellPath, viewModel.CurrentPath, usePtyWrapper: false))
            {
                AppendTerminalOutput($"Failed to start embedded shell ({shellPath}).{Environment.NewLine}");
                return false;
            }

            _terminalReadCts = new CancellationTokenSource();
            _ = ReadTerminalStreamAsync(_embeddedTerminalProcess!.StandardOutput, _terminalReadCts.Token);
            _ = ReadTerminalStreamAsync(_embeddedTerminalProcess!.StandardError, _terminalReadCts.Token);
            _ = ConfigureTerminalPathSyncHookAsync(shellPath);
            return true;
        }
        finally
        {
            _terminalStartGate.Release();
        }
    }

    private bool TryStartTerminalProcess(string shellPath, string workingDirectory, bool usePtyWrapper)
    {
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

            var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            _embeddedTerminalProcess = process;
            return true;
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

    private async Task ConfigureTerminalPathSyncHookAsync(string shellPath)
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

        await SendTerminalInputAsync(hookCommand + Environment.NewLine);
    }

    private async Task SendTerminalInputAsync(string input)
    {
        try
        {
            if (_embeddedTerminalProcess is null || _embeddedTerminalProcess.HasExited)
            {
                return;
            }

            await _embeddedTerminalProcess.StandardInput.WriteAsync(input);
            await _embeddedTerminalProcess.StandardInput.FlushAsync();
        }
        catch
        {
            AppendTerminalOutput($"{Environment.NewLine}[terminal input failed]{Environment.NewLine}");
        }
    }

    private bool TryApplyTerminalCompletion(TextBox inputBox)
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

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return false;
        }

        var (lookupDirectory, pathPrefix, namePrefix) = ResolveCompletionContext(token, viewModel.CurrentPath);
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
        if (!string.Equals(contextKey, _terminalCompletionContextKey, StringComparison.Ordinal))
        {
            _terminalCompletionMatches.Clear();
            _terminalCompletionMatches.AddRange(matches.Select(entry =>
                pathPrefix + entry.Name + (entry.IsDirectory ? "/" : string.Empty)));
            _terminalCompletionContextKey = contextKey;
            _terminalCompletionIndex = 0;
        }
        else
        {
            _terminalCompletionIndex = (_terminalCompletionIndex + 1) % _terminalCompletionMatches.Count;
        }

        var replacement = _terminalCompletionMatches[_terminalCompletionIndex];
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

    private void ResetTerminalCompletionCycle()
    {
        _terminalCompletionContextKey = string.Empty;
        _terminalCompletionIndex = -1;
        _terminalCompletionMatches.Clear();
    }

    private async Task ReadTerminalStreamAsync(StreamReader reader, CancellationToken cancellationToken)
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
                AppendTerminalOutput(chunk);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when terminal session is being shut down.
        }
        catch
        {
            AppendTerminalOutput($"{Environment.NewLine}[terminal output stream ended unexpectedly]{Environment.NewLine}");
        }
    }

    private void AppendTerminalOutput(string rawText)
    {
        if (string.IsNullOrEmpty(rawText))
        {
            return;
        }

        var visibleText = ExtractAndApplyTerminalPathMarkers(rawText);
        if (string.IsNullOrEmpty(visibleText))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            var inlineCollection = TerminalOutputText.Inlines;
            if (inlineCollection is null)
            {
                return;
            }

            foreach (var segment in ParseAnsiSegments(visibleText))
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
                _terminalOutputCharCount += segment.Length;
            }

            const int maxOutputChars = 160_000;
            if (_terminalOutputCharCount > maxOutputChars)
            {
                inlineCollection.Clear();
                _terminalOutputCharCount = 0;
                _terminalCurrentAnsiColor = -1;
                _terminalAnsiBold = false;
                inlineCollection.Add(new Run("[terminal output truncated]" + Environment.NewLine)
                {
                    Foreground = Brushes.SlateGray,
                });
            }

            if (TerminalOutputScrollViewer.Extent.Height > 0)
            {
                TerminalOutputScrollViewer.Offset = new Vector(
                    TerminalOutputScrollViewer.Offset.X,
                    TerminalOutputScrollViewer.Extent.Height);
            }
        });
    }

    private string ExtractAndApplyTerminalPathMarkers(string rawText)
    {
        var visible = new StringBuilder(rawText.Length);

        foreach (var ch in rawText)
        {
            if (_capturingTerminalPathMarker)
            {
                if (ch == TerminalPathMarkerEnd)
                {
                    var path = _terminalPathMarkerBuffer.ToString().Trim();
                    _terminalPathMarkerBuffer.Clear();
                    _capturingTerminalPathMarker = false;
                    TrySyncExplorerPathFromTerminal(path);
                    continue;
                }

                _terminalPathMarkerBuffer.Append(ch);
                continue;
            }

            if (ch == TerminalPathMarkerStart)
            {
                _capturingTerminalPathMarker = true;
                _terminalPathMarkerBuffer.Clear();
                continue;
            }

            visible.Append(ch);
        }

        return visible.ToString();
    }

    private void TrySyncExplorerPathFromTerminal(string path)
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
            if (DataContext is not MainWindowViewModel viewModel)
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

    private IEnumerable<TerminalTextSegment> ParseAnsiSegments(string chunk)
    {
        if (_ansiPendingBuffer.Length > 0)
        {
            _ansiPendingBuffer.Append(chunk);
            chunk = _ansiPendingBuffer.ToString();
            _ansiPendingBuffer.Clear();
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
                    yield return new TerminalTextSegment(text, _terminalCurrentAnsiColor, _terminalAnsiBold);
                    textBuffer.Clear();
                }

                if (index + 1 >= chunk.Length)
                {
                    _ansiPendingBuffer.Append(chunk[index]);
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
                    _ansiPendingBuffer.Append(chunk[index..]);
                    yield break;
                }

                var commandPayload = chunk[(index + 2)..commandIndex];
                ApplyAnsiSgrCommand(commandPayload);
                index = commandIndex + 1;
                continue;
            }

            textBuffer.Append(current);
            index++;
        }

        if (textBuffer.Length > 0)
        {
            var text = textBuffer.ToString().Replace("\r\n", "\n").Replace('\r', '\n');
            yield return new TerminalTextSegment(text, _terminalCurrentAnsiColor, _terminalAnsiBold);
        }
    }

    private void ApplyAnsiSgrCommand(string payload)
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
                    _terminalCurrentAnsiColor = -1;
                    _terminalAnsiBold = false;
                    break;
                case 1:
                    _terminalAnsiBold = true;
                    break;
                case 22:
                    _terminalAnsiBold = false;
                    break;
                case 39:
                    _terminalCurrentAnsiColor = -1;
                    break;
                default:
                    if ((code >= 30 && code <= 37) || (code >= 90 && code <= 97))
                    {
                        _terminalCurrentAnsiColor = code;
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

    private void ShutdownEmbeddedTerminal()
    {
        try
        {
            _ansiPendingBuffer.Clear();
            ResetTerminalCompletionCycle();
            _capturingTerminalPathMarker = false;
            _terminalPathMarkerBuffer.Clear();

            _terminalReadCts?.Cancel();
            _terminalReadCts?.Dispose();
            _terminalReadCts = null;

            if (_embeddedTerminalProcess is not null)
            {
                if (!_embeddedTerminalProcess.HasExited)
                {
                    try
                    {
                        _embeddedTerminalProcess.StandardInput.WriteLine("exit");
                        _embeddedTerminalProcess.StandardInput.Flush();
                    }
                    catch
                    {
                        // Ignore write failures while shutting down.
                    }

                    _embeddedTerminalProcess.WaitForExit(350);
                    if (!_embeddedTerminalProcess.HasExited)
                    {
                        _embeddedTerminalProcess.Kill(entireProcessTree: true);
                    }
                }

                _embeddedTerminalProcess.Dispose();
                _embeddedTerminalProcess = null;
            }
        }
        catch
        {
            // Ignore shutdown errors for terminal process cleanup.
        }
    }

    private readonly record struct TerminalTextSegment(string Text, int ColorCode, bool Bold)
    {
        public int Length => Text.Length;
    }
}
