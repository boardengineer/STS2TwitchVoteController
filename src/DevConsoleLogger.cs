using System.Collections.Concurrent;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Debug;

namespace STS2Twitch;

public static class DevConsoleLogger
{
    private static readonly ConcurrentQueue<string> MessageQueue = new();
    private static readonly List<string> History = new();
    private static RichTextLabel? _lastOutputBuffer;

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

        if (outputBuffer != _lastOutputBuffer)
        {
            _lastOutputBuffer = outputBuffer;
            foreach (var old in History)
            {
                outputBuffer.Text += old + "\n";
            }
        }

        while (MessageQueue.TryDequeue(out var message))
        {
            History.Add(message);
            outputBuffer.Text += message + "\n";
        }
    }
}
