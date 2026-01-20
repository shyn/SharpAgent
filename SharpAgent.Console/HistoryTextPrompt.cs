using System.Text.Json;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Spectre.Console;

namespace SharpAgent.Console;

public record PromptResult(string Input, bool IsModelSwitch = false);

public class HistoryTextPrompt
{
    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private readonly string _promptText;
    private readonly IAnsiConsole _console;
    private readonly string _workingDirectory;

    public HistoryTextPrompt(IAnsiConsole console, string prompt, string? workingDirectory = null)
    {
        _console = console;
        _promptText = prompt;
        _workingDirectory = workingDirectory ?? Directory.GetCurrentDirectory();
    }

    public void AddToHistory(string input)
    {
        if (!string.IsNullOrWhiteSpace(input))
        {
            _history.Add(input);
            _historyIndex = _history.Count;
        }
    }

    public PromptResult PromptWithResult()
    {
        while (true)
        {
            var input = new List<char>();

            while (true)
            {
                ShowPrompt(input);

                var keyInfo = _console.Input.ReadKey(intercept: true)!.Value;

                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    _historyIndex = _history.Count;
                    _console.WriteLine();
                    return new PromptResult(new string(input.ToArray()));
                }
                else if (keyInfo.Key == ConsoleKey.Escape)
                {
                    _console.WriteLine();
                    Environment.Exit(0);
                    return new PromptResult(string.Empty);
                }
                else if (keyInfo.Key == ConsoleKey.L && keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
                {
                    _console.WriteLine();
                    return new PromptResult(string.Empty, IsModelSwitch: true);
                }
                else if (keyInfo.Key == ConsoleKey.UpArrow)
                {
                    if (_history.Count > 0 && _historyIndex > 0)
                    {
                        _historyIndex--;
                        var historyText = _history[_historyIndex];
                        input.Clear();
                        input.AddRange(historyText);
                    }
                }
                else if (keyInfo.Key == ConsoleKey.DownArrow)
                {
                    if (_history.Count > 0 && _historyIndex < _history.Count)
                    {
                        _historyIndex++;
                        input.Clear();
                        if (_historyIndex < _history.Count)
                        {
                            input.AddRange(_history[_historyIndex]);
                        }
                    }
                }
                else if (keyInfo.Key == ConsoleKey.Backspace)
                {
                    if (input.Count > 0)
                    {
                        input.RemoveAt(input.Count - 1);
                    }
                }
                else if (keyInfo.KeyChar == '@')
                {
                    var searchResult = HandleAtSymbol();
                    if (searchResult != null)
                    {
                        input.AddRange(searchResult);
                    }
                }
                else if (!char.IsControl(keyInfo.KeyChar))
                {
                    input.Add(keyInfo.KeyChar);
                }
            }
        }
    }

    public string Prompt() => PromptWithResult().Input;

    // TODO: implement search menu like fzf
    private string? HandleAtSymbol()
    {
        // Show a prompt for searching files
        var searchPattern = _console.Prompt(
            new TextPrompt<string>("[bold yellow]@[/]")
                .Validate(input => !string.IsNullOrWhiteSpace(input), "Pattern cannot be empty")
        );

        if (string.IsNullOrWhiteSpace(searchPattern))
            return null;

        try
        {
            var matcher = new Matcher();
            matcher.AddInclude(searchPattern);

            var directoryInfo = new DirectoryInfoWrapper(new DirectoryInfo(_workingDirectory));
            var result = matcher.Execute(directoryInfo);

            var files = result.Files
                .Select(f => f.Path.Replace('\\', '/'))
                .OrderBy(f => f)
                .ToList();

            if (files.Count == 0)
            {
                AnsiConsole.MarkupLine($"[dim]No files found matching '{searchPattern.EscapeMarkup()}'[/]");
                return null;
            }

            if (files.Count == 1)
            {
                return files[0];
            }

            // Use selection prompt if multiple files found
            var selected = _console.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold yellow]Select file:[/]")
                    .AddChoices(files)
                    .MoreChoicesText("[dim](Use arrow keys to navigate, Enter to select)[/]")
                    .PageSize(10)
            );

            return selected;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Search error: {ex.Message.EscapeMarkup()}[/]");
            return null;
        }
    }

    private void ShowPrompt(List<char> input)
    {
        _console.Write("\u001b[2K"); // Clear line
        _console.Write("\u001b[G");  // Move to beginning
        _console.Markup($"[bold green]{_promptText.EscapeMarkup()}[/]");
        _console.Write(new string(input.ToArray()));
    }
}
