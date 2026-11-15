using Microsoft.Extensions.Logging;

namespace ApexPost.Courier.Tests;

/// <summary>
/// A background log line used to land on the menu's prompt line ("Choose 1-7:   [warn OutboxService] …", Week 4
/// screenshot D09). These check that it now goes on its own line and the prompt is printed again under it.
/// </summary>
public sealed class ScreenOutputTests
{
    private const string Warning = "  [warn OutboxService] Upload failed; 2 scans stay Pending.";

    [Fact]
    public void A_log_line_while_a_prompt_waits_goes_on_its_own_line_and_the_prompt_is_printed_again()
    {
        var text = new StringWriter();
        var screen = new ScreenOutput(text);
        var logger = new LineLoggerProvider(screen).CreateLogger("ApexPost.Courier.Offline.Sync.OutboxService");

        screen.WritePrompt("Choose 1-7: ");
        logger.LogWarning("Upload failed; {Count} scans stay Pending.", 2);

        Assert.Equal($"Choose 1-7: {Environment.NewLine}{Warning}{Environment.NewLine}Choose 1-7: ", text.ToString());
    }

    [Fact]
    public void A_log_line_after_the_prompt_was_answered_is_written_as_it_is()
    {
        var text = new StringWriter();
        var screen = new ScreenOutput(text);
        var logger = new LineLoggerProvider(screen).CreateLogger("OutboxService");

        screen.WritePrompt("Choose 1-7: ");
        screen.EndPrompt();
        screen.Writer.WriteLine();
        logger.LogWarning("Upload failed; {Count} scans stay Pending.", 2);

        Assert.Equal($"Choose 1-7: {Environment.NewLine}{Warning}{Environment.NewLine}", text.ToString());
    }

    [Fact]
    public void A_log_line_with_no_prompt_open_is_one_line()
    {
        var text = new StringWriter();
        var screen = new ScreenOutput(text);

        screen.WriteLogLine(Warning);

        Assert.Equal(Warning + Environment.NewLine, text.ToString());
    }
}
