using System.Text;

namespace Starlight.Console;

/// <summary>
///     Keeps command input on the bottom console row while allowing logs to scroll
///     above it without corrupting the text currently being edited.
/// </summary>
public sealed class InteractiveConsole : IDisposable
{
    private readonly ConsoleLineBuffer _buffer = new();
    private readonly bool _interactive;
    private readonly TextWriter? _interactiveWriter;
    private readonly TextWriter _output;
    private readonly object _syncRoot = new();
    private bool _disposed;
    private bool _hasOutputPosition;
    private int _lastInputLeft = -1;
    private int _lastInputRow = -1;
    private int _lastInputWidth;
    private int _outputLeft;
    private int _outputRow;

    private bool _reading;

    public InteractiveConsole()
    {
        _output = System.Console.Out;
        _interactive = !System.Console.IsInputRedirected && !System.Console.IsOutputRedirected;

        if (!_interactive)
            return;

        _interactiveWriter = new InteractiveConsoleWriter(this, _output);
        System.Console.SetOut(_interactiveWriter);
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
                return;

            if (_reading)
            {
                ClearLastInput();
                RestoreOutputPosition();
                _reading = false;
            }

            if (_interactiveWriter is not null)
                System.Console.SetOut(_output);

            _disposed = true;
        }
    }

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        if (System.Console.IsInputRedirected)
            return null;

        if (!_interactive)
            return await ReadRedirectedOutputLineAsync(cancellationToken);

        BeginInput();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                while (!System.Console.KeyAvailable)
                    await Task.Delay(millisecondsDelay: 20, cancellationToken);

                var key = System.Console.ReadKey(intercept: true);

                lock (_syncRoot)
                {
                    switch (key.Key)
                    {
                        case ConsoleKey.Enter:
                            return SubmitInput();

                        case ConsoleKey.Backspace:
                            _buffer.Backspace();
                            break;

                        case ConsoleKey.Delete:
                            _buffer.Delete();
                            break;

                        case ConsoleKey.LeftArrow:
                            _buffer.MoveLeft();
                            break;

                        case ConsoleKey.RightArrow:
                            _buffer.MoveRight();
                            break;

                        case ConsoleKey.Home:
                            _buffer.MoveHome();
                            break;

                        case ConsoleKey.End:
                            _buffer.MoveEnd();
                            break;

                        case ConsoleKey.Escape:
                            _buffer.Clear();
                            break;

                        default:
                            if (!char.IsControl(key.KeyChar))
                                _buffer.Insert(key.KeyChar);
                            break;
                    }

                    DrawInput();
                }
            }

            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            EndInput();
        }
    }

    private void BeginInput()
    {
        lock (_syncRoot)
        {
            CaptureOutputPosition();
            _reading = true;
            DrawInput();
        }
    }

    private void EndInput()
    {
        lock (_syncRoot)
        {
            if (!_reading)
                return;

            ClearLastInput();
            RestoreOutputPosition();
            _buffer.Clear();
            _reading = false;
        }
    }

    private string SubmitInput()
    {
        var line = _buffer.TakeLine();

        ClearLastInput();
        RestoreOutputPosition();
        _reading = false;

        // Keep submitted commands in the normal output stream rather than
        // printing them from the reserved bottom input row.
        _output.WriteLine(line);
        _output.Flush();
        CaptureOutputPosition();

        return line;
    }

    private async Task<string?> ReadRedirectedOutputLineAsync(CancellationToken cancellationToken)
    {
        var readTask = Task.Run(System.Console.ReadLine, CancellationToken.None);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using (cancellationToken.Register(() => cancelled.TrySetResult()))
        {
            if (await Task.WhenAny(readTask, cancelled.Task) != readTask)
                return null;
        }

        return await readTask;
    }

    private void WriteOutput(TextWriter target, string value)
    {
        lock (_syncRoot)
        {
            if (!_reading)
            {
                target.Write(value);
                return;
            }

            ClearLastInput();
            RestoreOutputPosition();

            target.Write(value);

            // Serilog's configured console template always terminates an event
            // with {NewLine}. If some future direct Console.Write() does not,
            // move the input onto its own row rather than overwriting that text.
            if (!EndsWithNewLine(value))
                target.WriteLine();

            target.Flush();
            CaptureOutputPosition();
            DrawInput(clearPrevious: false);
        }
    }

    private void DrawInput(bool clearPrevious = true)
    {
        if (!_reading || !TryGetInputArea(out var left, out var row, out var width))
            return;

        if (clearPrevious)
            ClearLastInput();

        ClearLine(left, row, width);

        var text = _buffer.Text;
        var cursor = _buffer.CursorIndex;
        var start = cursor >= width ? cursor - width + 1 : 0;
        var visibleLength = Math.Min(width, Math.Max(val1: 0, text.Length - start));
        var visible = visibleLength == 0 ? string.Empty : text.Substring(start, visibleLength);

        if (!TrySetCursorPosition(left, row))
            return;

        _output.Write(visible);
        _output.Flush();

        var cursorColumn = Math.Clamp(cursor - start, min: 0, Math.Max(val1: 0, width - 1));
        TrySetCursorPosition(left + cursorColumn, row);

        _lastInputLeft = left;
        _lastInputRow = row;
        _lastInputWidth = width;
    }

    private void ClearLastInput()
    {
        if (_lastInputRow < 0 || _lastInputWidth <= 0)
            return;

        ClearLine(_lastInputLeft, _lastInputRow, _lastInputWidth);
        _lastInputLeft = -1;
        _lastInputRow = -1;
        _lastInputWidth = 0;
    }

    private void ClearLine(int left, int row, int width)
    {
        if (width <= 0 || !TrySetCursorPosition(left, row))
            return;

        _output.Write(new string(c: ' ', width));
        _output.Flush();
        TrySetCursorPosition(left, row);
    }

    private void CaptureOutputPosition()
    {
        if (TryGetCursorPosition(out var left, out var row))
        {
            _outputLeft = left;
            _outputRow = row;
            _hasOutputPosition = true;
        }
    }

    private void RestoreOutputPosition()
    {
        if (_hasOutputPosition)
            TrySetCursorPosition(_outputLeft, _outputRow);
    }

    private static bool TryGetCursorPosition(out int left, out int row)
    {
        try
        {
            left = System.Console.CursorLeft;
            row = System.Console.CursorTop;
            return true;
        }
        catch (Exception ex) when (IsConsoleException(ex))
        {
            left = 0;
            row = 0;
            return false;
        }
    }

    private static bool TryGetInputArea(out int left, out int row, out int width)
    {
        try
        {
            left = System.Console.WindowLeft;
            row = System.Console.WindowTop + System.Console.WindowHeight - 1;

            // Leave the final console cell unused. Writing into the bottom-right
            // cell can wrap/scroll on the legacy Windows console host.
            width = Math.Max(val1: 1, System.Console.WindowWidth - 1);
            return true;
        }
        catch (Exception ex) when (IsConsoleException(ex))
        {
            left = 0;
            row = 0;
            width = 0;
            return false;
        }
    }

    private static bool TrySetCursorPosition(int left, int top)
    {
        try
        {
            System.Console.SetCursorPosition(left, top);
            return true;
        }
        catch (Exception ex) when (IsConsoleException(ex))
        {
            return false;
        }
    }

    private static bool IsConsoleException(Exception exception) =>
        exception is IOException or InvalidOperationException or ArgumentOutOfRangeException or PlatformNotSupportedException;

    private static bool EndsWithNewLine(string value) =>
        value.EndsWith('\n') || value.EndsWith('\r');

    private sealed class InteractiveConsoleWriter(InteractiveConsole console, TextWriter target) : TextWriter
    {
        public override Encoding Encoding => target.Encoding;

        public override void Write(string? value)
        {
            if (!string.IsNullOrEmpty(value))
                console.WriteOutput(target, value);
        }

        public override void Write(char value) => console.WriteOutput(target, value.ToString());

        public override void Write(char[] buffer, int index, int count) =>
            console.WriteOutput(target, new string(buffer, index, count));

        public override void Write(ReadOnlySpan<char> buffer) =>
            console.WriteOutput(target, buffer.ToString());

        public override void WriteLine(string? value) =>
            console.WriteOutput(target, (value ?? string.Empty) + NewLine);

        public override void WriteLine() => console.WriteOutput(target, NewLine);

        public override void Flush() => target.Flush();
    }
}
