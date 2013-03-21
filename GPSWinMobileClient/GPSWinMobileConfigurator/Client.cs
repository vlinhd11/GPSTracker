﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Threading;

namespace GPSWinMobileConfigurator
{
    // State object for receiving data from remote device.
    public class StateObject
    {
        // Client socket.
        public Socket workSocket = null;
        // Size of receive buffer.
        public const int BufferSize = 32;
        // Receive buffer.
        public byte[] buffer = new byte[BufferSize];
        // Received data string.
        public StringBuilder sb = new StringBuilder();
    }

    public class AsynchronousClient
    {
        public delegate void ConnectionEventDelegate(string status);
        public event ConnectionEventDelegate Connected;

        Settings settings;

        public bool IsConnected { get; private set; }

        Thread th;

        // ManualResetEvent instances signal completion.
        private ManualResetEvent connectDone = new ManualResetEvent(false);
        private ManualResetEvent sendDone = new ManualResetEvent(false);
        private ManualResetEvent AuthDone = new ManualResetEvent(false);
        private Socket client;

        public AsynchronousClient()
        {
            settings = new Settings();
        }

        public AsynchronousClient(Settings _settings)
        {
            settings = _settings;
        }

        public void Start()
        {
            IsConnected = false;
            try
            {
                // Establish the remote endpoint for the socket.
                // The name of the 
                // remote device is "host.contoso.com".

                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Parse(settings.Host), Convert.ToInt16(settings.Port));

                // Create a TCP/IP socket.
                client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                // Connect to the remote endpoint.
                client.BeginConnect(remoteEP, new AsyncCallback(ConnectCallback), client);
                connectDone.WaitOne();
                if (!IsConnected) { return; }

                // Receive the response from the remote device.
                th = new Thread(delegate()
                {
                    Receive(client);
                });
                th.IsBackground = true;
                th.Start();

                // Send test data to the remote device.
                Send(client, "!" + settings.UserName + "@" + settings.Password);
                //Send(client, string.Format("!{0}@{1}", settings.UserName, settings.Password));
                sendDone.WaitOne();
                AuthDone.WaitOne();
            }
            catch (Exception e)
            {
                Allert.ShowMessage(e.ToString());
                //Connected("Connection Error");
            }
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket client = (Socket)ar.AsyncState;

                // Complete the connection.
                client.EndConnect(ar);

                //TODO: допилить инвок
                //Console.WriteLine("Socket connected to {0}", client.RemoteEndPoint.ToString());

                // Signal that the connection has been made.
                IsConnected = true;
            }
            catch (Exception e)
            {
                Allert.ShowMessage(e.Message);
                IsConnected = false;
            }
            finally { connectDone.Set(); }
        }

        public void Receive(Socket client)
        {
            try
            {
                // Create the state object.
                StateObject state = new StateObject();
                state.workSocket = client;

                // Begin receiving the data from the remote device.
                client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReceiveCallback), state);
            }
            catch (Exception e)
            {
                Allert.ShowMessage(e.Message);
                Connected("Connection Error");
                return;
            }
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the state object and the client socket 
                // from the asynchronous state object.
                StateObject state = (StateObject)ar.AsyncState;
                Socket client = state.workSocket;

                int bytesRead = 0;

                try // Дело в том, что этот кэллбэк запускается в отдельном потоке,а при уничтожении объекта сокета посылает пустое значение, к которому этот, еще живой кэллбэк, пытается применить метод уже уничтоженного сокета.
                    // Поэтому этот эксепшен может появиться только в случае закрытия клиента и не требует ни обработки ни вывода какого-либо сообщения.
                {
                    // Read data from the remote device.
                    bytesRead = client.EndReceive(ar);
                }
                catch (ObjectDisposedException) { }

                // The response from the remote device.
                StringBuilder response = new StringBuilder();

                if (bytesRead > 0)
                {
                    // There might be more data, so store the data received so far.
                    state.sb.Append(Encoding.ASCII.GetString(state.buffer, 0, bytesRead));

                    if (state.sb.ToString() == "Success")
                    {
                        Connected(state.sb.ToString());
                        AuthDone.Set();
                    }
                    state.sb = new StringBuilder();

                    client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReceiveCallback), state);
                }
            }
            catch (Exception e)
            {
                Allert.ShowMessage(e.Message);
                Connected("Connection Error");
                return;
            }
        }

        public void Send(string _msg)
        {
            Send(client, _msg);
            sendDone.WaitOne();
        }

        private void Send(Socket client, String data)
        {
            // Convert the string data to byte data using ASCII encoding.
            byte[] byteData = Encoding.ASCII.GetBytes(data);

            // Begin sending the data to the remote device.
            client.BeginSend(byteData, 0, byteData.Length, 0, new AsyncCallback(SendCallback), client);
        }

        private void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                Socket client = (Socket)ar.AsyncState;

                // Complete sending the data to the remote device.
                int bytesSent = client.EndSend(ar);
                //Console.WriteLine("Sent {0} bytes to server.", bytesSent);

                
            }
            catch (Exception e)
            {
                Allert.ShowMessage(e.Message);
                Connected("Connection Error");
                return;
            }
            finally 
            {
                // Signal that all bytes have been sent.
                sendDone.Set();
            }
        }

        public void Stop()
        {
            // Release the socket.
            client.Shutdown(SocketShutdown.Both);
            client.Close();
            IsConnected = false;
        }
    }
}
