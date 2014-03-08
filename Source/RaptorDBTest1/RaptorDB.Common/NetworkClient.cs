﻿using System;
using System.IO;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

using System.Collections;
using System.Net;
using System.Threading.Tasks;

namespace RaptorDB.Common
{
    //
    // Header bits format : 0 - json = 1 , bin = 0 
    //                      1 - binaryjson = 1 , text json = 0
    //                      2 - compressed = 1 , uncompressed = 0 

    public class NetworkClient
    {
        internal static class Config
        {
            public static int BufferSize = 32 * 1024;
            public static int LogDataSizesOver = 1000000;
            public static int CompressDataOver = 1000000;
        }

        public NetworkClient(string server, int port)
        {
            _server = server;
            _port = port;
        }

        private TcpClient _client;
        private string _server;
        private int _port;

        public bool UseBJSON = true;

        public string LastErrorMessage { get; private set; }

        public void Connect()
        {
            //JSR - protect against nothing listening
            try
            {
                _client = new TcpClient(_server, _port);
                _client.SendBufferSize = Config.BufferSize;
                _client.ReceiveBufferSize = _client.SendBufferSize;
            }
            catch (ArgumentNullException e)
            {
                LastErrorMessage = e.Message;
            }
            catch (SocketException e)
            {
                LastErrorMessage = e.Message;
            }
            catch (Exception e)
            {
                LastErrorMessage = e.Message;
            }
        }

        public object Send(object data)
        {
            LastErrorMessage = "";

            CheckConnection();

            //JSR Connect() has set error if exists
            if (!String.IsNullOrEmpty(LastErrorMessage))
                return null;

            byte[] hdr = new byte[5];
            hdr[0] = (UseBJSON ? (byte)3 : (byte)0);
            byte[] dat = fastBinaryJSON.BJSON.Instance.ToBJSON(data);
            byte[] len = Helper.GetBytes(dat.Length, false);
            Array.Copy(len, 0, hdr, 1, 4);
            _client.Client.Send(hdr);
            _client.Client.Send(dat);

            byte[] rechdr = new byte[5];
            using (NetworkStream n = new NetworkStream(_client.Client))
            {
                n.Read(rechdr, 0, 5);
                int c = Helper.ToInt32(rechdr, 1);
                byte[] recd = new byte[c];
                int bytesRead = 0;
                int chunksize = 1;
                while (bytesRead < c && chunksize > 0)
                    bytesRead +=
                      chunksize = n.Read
                        (recd, bytesRead, c - bytesRead);
                if ((rechdr[0] & (byte)4) == (byte)4)
                    recd = MiniLZO.Decompress(recd);
                if ((rechdr[0] & (byte)3) == (byte)3)
                    return fastBinaryJSON.BJSON.Instance.ToObject(recd);
            }
            return null;
        }

        private void CheckConnection()
        {
            // check connected state before sending
            if (_client == null)
                Connect();
            else
            {
                if (_client.Connected == false)
                    Connect();
            }
        }

        public void Close()
        {
            if (_client != null)
                _client.Close();
        }
    }

    public class NetworkServer
    {
        public delegate object ProcessPayload(object data);

        private ILog log = RaptorDB.LogManager.GetLogger(typeof(NetworkServer));
        ProcessPayload _handler;
        private bool _run = true;
        private int count = 0;
        private int _port;

        public void Start(int port, ProcessPayload handler)
        {
            _handler = handler;
            _port = port;
            ThreadPool.SetMinThreads(50, 50);
            System.Timers.Timer t = new System.Timers.Timer(1000);
            t.AutoReset = true;
            t.Start();
            t.Elapsed += new System.Timers.ElapsedEventHandler(t_Elapsed);
            Task.Factory.StartNew(() => Run(), TaskCreationOptions.AttachedToParent);
        }

        private void Run()
        {
            TcpListener listener = new TcpListener(IPAddress.Any, _port);
            listener.Start();
            while (_run)
            {
                try
                {
                    TcpClient c = listener.AcceptTcpClient();
                    Task.Factory.StartNew(() => Accept(c));
                }
                catch (Exception ex)
                { 
                    log.Error(ex);
                }
            }
        }

        void t_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (count > 0)
                log.Debug("tcp connects/sec = " + count);
            count = 0;
        }

        public void Stop()
        {
            _run = false;
        }

        void Accept(TcpClient client)
        {
            using (NetworkStream n = client.GetStream())
            {
                while (client.Connected)
                {
                    this.count++;
                    byte[] c = new byte[5];
                    n.Read(c, 0, 5);
                    int count = BitConverter.ToInt32(c, 1);
                    byte[] data = new byte[count];
                    int bytesRead = 0;
                    int chunksize = 1;
                    while (bytesRead < count && chunksize > 0)
                        bytesRead +=
                          chunksize = n.Read
                            (data, bytesRead, count - bytesRead);

                    object o = fastBinaryJSON.BJSON.Instance.ToObject(data);

                    object r = _handler(o);
                    bool compressed = false;
                    data = fastBinaryJSON.BJSON.Instance.ToBJSON(r);
                    if (data.Length > RaptorDB.Common.NetworkClient.Config.CompressDataOver)
                    {
                        log.Debug("compressing data over limit : " + data.Length.ToString("#,#"));
                        compressed = true;
                        data = MiniLZO.Compress(data);
                        log.Debug("new size : " + data.Length.ToString("#,#"));
                    }
                    if (data.Length > RaptorDB.Common.NetworkClient.Config.LogDataSizesOver)
                        log.Debug("data size (bytes) = " + data.Length.ToString("#,#"));

                    byte[] b = BitConverter.GetBytes(data.Length);
                    byte[] hdr = new byte[5];
                    hdr[0] = (byte)(3 + (compressed ? 4 : 0));
                    Array.Copy(b, 0, hdr, 1, 4);
                    n.Write(hdr, 0, 5);
                    n.Write(data, 0, data.Length);

                    int wait = 0;
                    while (n.DataAvailable == false)
                    {
                        wait++;
                        if (wait < 10000) // kludge : for insert performance
                            Thread.Sleep(0);
                        else
                            Thread.Sleep(1);
                        // FEATURE : if wait > 10 min -> close connection 
                    }
                }
            }
        }
    }
}
