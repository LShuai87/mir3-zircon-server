using System;
using System.Net.Sockets;

namespace Client.Envir
{
    // The callback only publishes a result. The game thread owns disposal and handoff.
    internal sealed class ClientConnectionAttempt : IDisposable
    {
        private readonly TcpClient client = new TcpClient();
        private volatile bool completed;
        private bool ownsClient = true;

        public bool IsCompleted => completed;
        public Exception Error { get; private set; }

        public ClientConnectionAttempt(string host, int port)
        {
            // TcpClient.Close clears its internal Socket. Keep the original Socket for EndConnect.
            Socket socket = client.Client;
            try
            {
                socket.BeginConnect(host, port, Connecting, socket);
            }
            catch (Exception ex)
            {
                Error = ex;
                completed = true;
            }
        }

        private void Connecting(IAsyncResult result)
        {
            try
            {
                ((Socket)result.AsyncState).EndConnect(result);
            }
            catch (Exception ex)
            {
                Error = ex;
            }
            finally
            {
                // Publish Error before the game thread observes completion.
                completed = true;
            }
        }

        public TcpClient TakeClient()
        {
            if (!ownsClient) throw new ObjectDisposedException(nameof(ClientConnectionAttempt));
            if (!completed) throw new InvalidOperationException("The connection attempt is still pending.");
            if (Error != null) return null;

            ownsClient = false;
            return client;
        }

        public void Dispose()
        {
            if (!ownsClient) return;

            ownsClient = false;
            client.Close();
        }
    }
}
