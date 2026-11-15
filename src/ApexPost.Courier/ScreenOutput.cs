namespace ApexPost.Courier;

/// <summary>
/// The one writer the screens and the log lines share. It remembers the prompt that is waiting for an answer, so a
/// log line from a background service (the outbox, auto sync, the change listener) does not land after
/// "Choose 1-7: " on the same line: the line goes on a line of its own and the prompt is printed again under it.
/// </summary>
internal sealed class ScreenOutput
{
    private readonly Lock _gate = new();
    private string? _openPrompt;

    public ScreenOutput(TextWriter writer) => Writer = TextWriter.Synchronized(writer);

    /// <summary>For the screens' own lines, which are written on the screen's thread.</summary>
    public TextWriter Writer { get; }

    /// <summary>Writes a prompt and keeps it open until <see cref="EndPrompt"/>.</summary>
    public void WritePrompt(string prompt)
    {
        lock (_gate)
        {
            Writer.Write(prompt);
            _openPrompt = prompt;
        }
    }

    /// <summary>The prompt has its answer; later log lines are written as they are.</summary>
    public void EndPrompt()
    {
        lock (_gate)
        {
            _openPrompt = null;
        }
    }

    /// <summary>Writes one log line; when a prompt is waiting, moves off its line first and prints it again after.</summary>
    public void WriteLogLine(string line)
    {
        lock (_gate)
        {
            if (_openPrompt is null)
            {
                Writer.WriteLine(line);
                return;
            }

            Writer.WriteLine();
            Writer.WriteLine(line);
            Writer.Write(_openPrompt);
        }
    }
}
