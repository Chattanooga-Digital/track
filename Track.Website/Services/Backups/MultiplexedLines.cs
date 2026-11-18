using System.Text;
using Docker.DotNet;

namespace Track.Website.Services.Backups;

public static class MultiplexedLines
{
    /// <summary>Calls <paramref name="onLine"/> for each line of a container's stdout and stderr until the stream ends.</summary>
    public static async Task ReadAsync(MultiplexedStream stream, Action<string, bool> onLine, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var pending = new Dictionary<bool, (Decoder Decoder, StringBuilder Text)>
        {
            [false] = (Encoding.UTF8.GetDecoder(), new StringBuilder()),
            [true] = (Encoding.UTF8.GetDecoder(), new StringBuilder()),
        };

        while (true)
        {
            var read = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cancellationToken);
            if (read.EOF)
                break;

            var isError = read.Target == MultiplexedStream.TargetStream.StandardError;
            var (decoder, text) = pending[isError];
            var chars = new char[decoder.GetCharCount(buffer, 0, read.Count)];
            decoder.GetChars(buffer, 0, read.Count, chars, 0);
            text.Append(chars);
            Flush(text, isError, onLine, final: false);
        }

        foreach (var (isError, (_, text)) in pending)
            Flush(text, isError, onLine, final: true);
    }

    private static void Flush(StringBuilder text, bool isError, Action<string, bool> onLine, bool final)
    {
        var content = text.ToString();
        var lines = content.Split('\n');
        var complete = final ? lines.Length : lines.Length - 1;
        for (var i = 0; i < complete; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length > 0)
                onLine(line, isError);
        }

        text.Clear();
        if (!final)
            text.Append(lines[^1]);
    }
}
