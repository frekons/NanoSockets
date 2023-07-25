/*
 *  Lightweight UDP sockets abstraction for rapid implementation of message-oriented protocols
 *  Copyright (c) 2019 Stanislav Denisov
 *
 *  Permission is hereby granted, free of charge, to any person obtaining a copy
 *  of this software and associated documentation files (the "Software"), to deal
 *  in the Software without restriction, including without limitation the rights
 *  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 *  copies of the Software, and to permit persons to whom the Software is
 *  furnished to do so, subject to the following conditions:
 *
 *  The above copyright notice and this permission notice shall be included in all
 *  copies or substantial portions of the Software.
 *
 *  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 *  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 *  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 *  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 *  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 *  SOFTWARE.
 */

using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace NanoSockets
{
    public enum Status
    {
        OK = 0,
        Error = -1
    }

    public enum PollType
    {
        SelectRead = 0,
        SelectWrite = 1,
        SelectError = 2,
    }

    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct Socket
    {
        [FieldOffset(0)]
        private long handle;

        public readonly bool IsCreated
        {
            get
            {
                return handle > 0;
            }
        }

        public readonly bool Blocking
        {
            set
            {
                if (UDP.SetNonBlocking(this, shouldBlock: value) == Status.Error)
                    throw new SocketException();
            }
        }

        public readonly bool DontFragment
        {
            get
            {
                int result = -1, length = sizeof(int);

                if (UDP.GetOption(this, (int)System.Net.Sockets.SocketOptionLevel.IP, (int)System.Net.Sockets.SocketOptionName.DontFragment, ref result, ref length) == Status.Error)
                    throw new SocketException();

                //Debug.Log($"DontFragment: {result}");

                return result != 0;
            }

            set
            {
                if (value)
                {
                    UDP.SetDontFragment(this);
                }
                else
                {
                    throw new NotSupportedException("DontFragment = false is not supported!");
                }
            }
        }

        //internal void UpdateStatusAfterSocketError(SocketException socketException)
        //{
        //    var errorCode = (SocketError)socketException.NativeErrorCode;

        //    //if (Socket.s_LoggingEnabled)
        //    //{
        //    //    Logging.PrintError(Logging.Sockets, this, "UpdateStatusAfterSocketError", errorCode.ToString());
        //    //}

        //    //if (this.IsCreated && (this.m_Handle.IsInvalid || (errorCode != SocketError.WouldBlock && errorCode != SocketError.IOPending && errorCode != SocketError.NoBufferSpaceAvailable && errorCode != SocketError.TimedOut)))
        //    //{
        //    //    this.SetToDisconnected();
        //    //}
        //}

        public readonly bool Poll(long timeout, PollType type)
        {
            var num = UDP.Poll(this, timeout, type);

            if (num == 0) return false;

            if (num == -1)
            {
                SocketException ex = new SocketException();
                //this.UpdateStatusAfterSocketError(ex);

                //if (Socket.s_LoggingEnabled)
                //{
                //    Logging.Exception(Logging.Sockets, this, "Poll", ex);
                //}

                if (ex.ErrorCode != (int)SocketError.MessageSize)
                {
                    throw ex;
                }
                else return false;
            }

            return true;
        }

        public void SendTo(byte[] buffer, int offset, int count, System.Net.Sockets.SocketFlags flags, ref Address address)
        {
            UDP.Send(this, ref address, buffer, offset, count);
        }

        // socket.ReceiveFrom(recvBuffer, 0, recvBuffer.Length, System.Net.Sockets.SocketFlags.None, ref remoteEP);
        public int ReceiveFrom(byte[] buffer, int offset, int size, System.Net.Sockets.SocketFlags flags, ref Address address)
        {
            return UDP.Receive(this, ref address, buffer, offset, size);
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 18)]
    public struct Address : IEquatable<Address>
    {
        [FieldOffset(0)]
        public ulong address0;
        [FieldOffset(8)]
        public ulong address1;
        [FieldOffset(8)]
        public ushort zeros;
        [FieldOffset(10)]
        public ushort ffff;
        [FieldOffset(12)]
        public int ipv4;
        [FieldOffset(16)]
        public ushort port;
        [FieldOffset(16)]
        public ushort Port;

        public bool IsIPv4()
        {
            return address0 == 0 && zeros == 0 && ffff == 0xFFFF;
        }

        public bool Equals(Address other)
        {
            return address0 == other.address0 && address1 == other.address1 && port == other.port;
        }

        //public override bool Equals(object obj)
        //{
        //    if (obj is Address)
        //        return Equals((Address)obj);

        //    return false;
        //}

        public override int GetHashCode()
        {
            int hash = 17;

            hash = hash * 31 + address0.GetHashCode();
            hash = hash * 31 + address1.GetHashCode();
            hash = hash * 31 + port.GetHashCode();

            return hash;
        }

        public string GetIp()
        {
            StringBuilder ip = new StringBuilder(64);

            NanoSockets.UDP.GetIP(ref this, ip, 64);

            return ip.ToString();
        }

        public string GetIpAndPort()
        {
            StringBuilder ip = new StringBuilder(64);

            var status = NanoSockets.UDP.GetIP(ref this, ip, 64);

            //Debug.Log($"[NanoSockets] GetIpAndPort, UDP.GetIP status: {status}, this.address0: {this.address0}, this.address1: {this.address1}, this.ffff: {this.ffff}, this.zeros: {this.zeros}, this.ipv4: {this.ipv4}, this.port: {this.port}");

            if (status != Status.OK)
                return string.Empty;

            ip.Append(":");
            ip.Append(this.port);

            return ip.ToString();
        }

        public override string ToString()
        {
            return GetIpAndPort();
        }

        public static Address CreateFromIpPort(string ip, ushort port)
        {
            Address address = default;

            NanoSockets.UDP.SetIP(ref address, ip);
            address.port = port;

            return address;
        }
    }

    [SuppressUnmanagedCodeSecurity]
    public static class UDP
    {
#if __IOS__ || UNITY_IOS && !UNITY_EDITOR
			private const string nativeLibrary = "__Internal";
#else
        private const string nativeLibrary = "nanosockets";
#endif

        public const int hostNameSize = 1025;

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_initialize", CallingConvention = CallingConvention.Cdecl)]
        public static extern Status Initialize();

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_deinitialize", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Deinitialize();

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_create", CallingConvention = CallingConvention.Cdecl)]
        public static extern Socket Create(int domain, int sendBufferSize, int receiveBufferSize);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_destroy", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Destroy(ref Socket socket);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_bind", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Bind(Socket socket, IntPtr address);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_bind", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Bind(Socket socket, ref Address address);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_connect", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Connect(Socket socket, ref Address address);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_set_option", CallingConvention = CallingConvention.Cdecl)]
        public static extern Status SetOption(Socket socket, int level, int optionName, ref int optionValue, int optionLength);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_get_option", CallingConvention = CallingConvention.Cdecl)]
        public static extern Status GetOption(Socket socket, int level, int optionName, ref int optionValue, ref int optionLength);

        public static Status SetNonBlocking(Socket socket, bool shouldBlock = false)
                => SetNonBlocking(socket, shouldBlock ? (byte)0 : (byte)1);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_set_nonblocking", CallingConvention = CallingConvention.Cdecl)]
        private static extern Status SetNonBlocking(Socket socket, byte state);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_set_dontfragment", CallingConvention = CallingConvention.Cdecl)]
        public static extern Status SetDontFragment(Socket socket);

        public static int Poll(Socket socket, long timeout, PollType pollType) => Poll(socket, timeout, (byte)pollType);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_poll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Poll(Socket socket, long timeout, byte pollType);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_send", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Send(Socket socket, IntPtr address, IntPtr buffer, int bufferLength);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_send", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Send(Socket socket, IntPtr address, byte[] buffer, int bufferLength);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_send", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Send(Socket socket, ref Address address, IntPtr buffer, int bufferLength);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_send", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Send(Socket socket, ref Address address, byte[] buffer, int bufferLength);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_send_offset", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Send(Socket socket, IntPtr address, byte[] buffer, int offset, int bufferLength);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_send_offset", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Send(Socket socket, ref Address address, byte[] buffer, int offset, int bufferLength);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_receive", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Receive(Socket socket, IntPtr address, IntPtr buffer, int bufferLength);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_receive", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Receive(Socket socket, IntPtr address, byte[] buffer, int bufferLength);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_receive", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Receive(Socket socket, ref Address address, IntPtr buffer, int bufferLength);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_receive", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Receive(Socket socket, ref Address address, byte[] buffer, int bufferLength);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_receive_offset", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Receive(Socket socket, IntPtr address, byte[] buffer, int offset, int bufferLength);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_receive_offset", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Receive(Socket socket, ref Address address, byte[] buffer, int offset, int bufferLength);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_address_get", CallingConvention = CallingConvention.Cdecl)]
        public static extern Status GetAddress(Socket socket, ref Address address);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_address_is_equal", CallingConvention = CallingConvention.Cdecl)]
        public static extern Status IsEqual(ref Address left, ref Address right);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_address_set_ip", CallingConvention = CallingConvention.Cdecl)]
        public static extern Status SetIP(ref Address address, IntPtr ip);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_address_set_ip", CallingConvention = CallingConvention.Cdecl)]
        public static extern Status SetIP(ref Address address, string ip);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_address_get_ip", CallingConvention = CallingConvention.Cdecl)]
        public static extern Status GetIP(ref Address address, IntPtr ip, int ipLength);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_address_get_ip", CallingConvention = CallingConvention.Cdecl)]
        public static extern Status GetIP(ref Address address, StringBuilder ip, int ipLength);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_address_set_hostname", CallingConvention = CallingConvention.Cdecl)]
        public static extern Status SetHostName(ref Address address, IntPtr name);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_address_set_hostname", CallingConvention = CallingConvention.Cdecl)]
        public static extern Status SetHostName(ref Address address, string name);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_address_get_hostname", CallingConvention = CallingConvention.Cdecl)]
        public static extern Status GetHostName(ref Address address, IntPtr name, int nameLength);

        [DllImport(nativeLibrary, EntryPoint = "nanosockets_address_get_hostname", CallingConvention = CallingConvention.Cdecl)]
        public static extern Status GetHostName(ref Address address, StringBuilder name, int nameLength);

#if NANOSOCKETS_UNSAFE_API
			public static unsafe class Unsafe {
				[DllImport(nativeLibrary, EntryPoint = "nanosockets_receive", CallingConvention = CallingConvention.Cdecl)]
				public static extern int Receive(Socket socket, Address* address, byte* buffer, int bufferLength);

				[DllImport(nativeLibrary, EntryPoint = "nanosockets_send", CallingConvention = CallingConvention.Cdecl)]
				public static extern int Send(Socket socket, Address* address, byte* buffer, int bufferLength);
			}
#endif
    }
}
