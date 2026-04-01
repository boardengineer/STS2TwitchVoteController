using System.Collections.Concurrent;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Debug;

namespace STS2Twitch;

public static class DevConsoleLogger
{
    private static readonly ConcurrentQueue<string> MessageQueue = new();

    public static void Enqueue(string message)
    {
        MessageQueue.Enqueue(message);
    }

    public static void FlushToConsole()
    {
        var console = NDevConsole.Instance;
        if (console == null)
            return;

        var outputBuffer = console.GetNode<RichTextLabel>("OutputContainer/OutputBuffer");
        if (outputBuffer == null)
            return;

        while (MessageQueue.TryDequeue(out var message))
        {
            outputBuffer.Text += message + "\n";
        }
    }
}
