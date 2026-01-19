using Spectre.Console;

namespace SharpAgent.Console;

public class HistoryTextPrompt
{
    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private readonly string _promptText;
    private readonly IAnsiConsole _console;

    public HistoryTextPrompt(IAnsiConsole console, string prompt)
    {
        _console = console;
        _promptText = prompt;
    }

    public void AddToHistory(string input)
    {
        if (!string.IsNullOrWhiteSpace(input))
        {
            _history.Add(input);
            _historyIndex = _history.Count;
        }
    }

    public string Prompt()
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
                    return new string(input.ToArray());
                }
                else if (keyInfo.Key == ConsoleKey.Escape)
                {
                    _console.WriteLine();
                    Environment.Exit(0);
                    return string.Empty;
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
                else if (!char.IsControl(keyInfo.KeyChar))
                {
                    input.Add(keyInfo.KeyChar);
                }
            }
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
