using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Client.Envir;

internal static class ClientConnectionAttemptTests
{
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Wait(ClientConnectionAttempt attempt)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!attempt.IsCompleted && DateTime.UtcNow < deadline) Thread.Sleep(1);
        Assert(attempt.IsCompleted, "Connection callback did not finish.");
    }

    private static void SuccessfulHandoff(TcpListener listener)
    {
        using (var attempt = new ClientConnectionAttempt("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port))
        {
            Wait(attempt);
            Assert(attempt.Error == null, "Loopback connection failed: " + attempt.Error);
            using (TcpClient client = attempt.TakeClient())
            using (TcpClient accepted = listener.AcceptTcpClient())
            {
                attempt.Dispose();
                client.GetStream().WriteByte(42);
                accepted.ReceiveTimeout = 2000;
                Assert(accepted.GetStream().ReadByte() == 42, "Disposal closed the transferred client.");
            }
        }
    }

    private static void Run()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(512);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            SuccessfulHandoff(listener);
            Console.WriteLine("PASS: successful handoff retains socket ownership");

            var cancelled = new List<ClientConnectionAttempt>();
            for (int index = 0; index < 200; index++)
            {
                var attempt = new ClientConnectionAttempt("127.0.0.1", port);
                cancelled.Add(attempt);
                attempt.Dispose();
                attempt.Dispose();
            }
            foreach (var attempt in cancelled)
            {
                Wait(attempt);
                Assert(!(attempt.Error is NullReferenceException), "Cancellation caused a null reference.");
                bool rejected = false;
                try { attempt.TakeClient(); }
                catch (ObjectDisposedException) { rejected = true; }
                Assert(rejected, "A cancelled attempt transferred its client.");
            }
            while (listener.Pending()) listener.AcceptTcpClient().Close();
            SuccessfulHandoff(listener);
            Console.WriteLine("PASS: 200 cancellations complete safely; subsequent connection succeeds");
        }
        finally
        {
            listener.Stop();
        }

        using (var refused = new ClientConnectionAttempt("127.0.0.1", port))
        {
            Wait(refused);
            Assert(refused.Error is SocketException, "Expected a refused-connection SocketException.");
            Assert(refused.TakeClient() == null, "Failed connection returned a client.");
        }
        Console.WriteLine("PASS: refused connection yields a failed result");

        using (var invalid = new ClientConnectionAttempt("127.0.0.1", -1))
        {
            Assert(invalid.IsCompleted, "Synchronous failure was not published.");
            Assert(invalid.Error is ArgumentOutOfRangeException, "Unexpected invalid-port result.");
            Assert(invalid.TakeClient() == null, "Invalid connection returned a client.");
        }
        Console.WriteLine("PASS: synchronous connection failure yields a failed result");
    }

    public static int Main()
    {
        try { Run(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
