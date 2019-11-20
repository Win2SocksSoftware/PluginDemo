using System;
using System.Collections.Generic;
using System.Composition;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Win2Socks.Plugin;

namespace PluginDemo {
    public sealed class PluginClient : IPluginClient {
        public Socket UnderlyingSocket => _type == ClientProtocolType.Tcp ? _tcpClient.Client : _udpClient.Client;

        const int AddressTypeBytesCount = 1;
        const int IPv4AddressBytesCount = 4;
        const int IPv6AddressBytesCount = 16;
        const int DomainLengthBytesCount = 1;
        const int PortBytesCount = 2;

        private readonly TcpClient _tcpClient;
        private readonly UdpClient _udpClient;
        private readonly IPEndPoint _proxyServer;
        private readonly ClientProtocolType _type;
        private readonly Algorithm _algorithm;
        private readonly byte _key;

        private NetworkStream _stream; // TcpClient.Dispose will dispose it, we don't need to dispose it manually.
        private bool _disposed;

        internal PluginClient(IPEndPoint proxyServer, ClientProtocolType type, Algorithm algorithm, byte key) {
            _proxyServer = proxyServer;
            _type = type;

            switch (type) {
                case ClientProtocolType.Tcp:
                    _tcpClient = new TcpClient(proxyServer.AddressFamily);
                    break;
                case ClientProtocolType.Udp:
                    _udpClient = new UdpClient(proxyServer.AddressFamily);
                    break;
                default:
                    throw new NotImplementedException();
            }

            _algorithm = algorithm;
            _key = key;
        }

        public void Dispose() {
            if (!_disposed) {
                _tcpClient?.Dispose();
                _udpClient?.Dispose();

                _disposed = true;
            }
        }

        public async Task ConnectAsync(IPAddress address, int port) {
            if (_type == ClientProtocolType.Tcp) {
                // At first, we connect to proxy server.
                await _tcpClient.ConnectAsync(_proxyServer.Address, _proxyServer.Port);
                _stream = _tcpClient.GetStream();

                // Then build first packet that includes the real destination address and port.
                var firstPacketLength = AddressTypeBytesCount +
                                        (address.AddressFamily == AddressFamily.InterNetworkV6 ? IPv6AddressBytesCount : IPv4AddressBytesCount) +
                                        PortBytesCount;

                var buffer = new byte[firstPacketLength];

                using (var stream = new MemoryStream(buffer))
                using (var writer = new BinaryWriter(stream)) {
                    // address type
                    writer.Write((byte)(address.AddressFamily == AddressFamily.InterNetworkV6 ? AddressType.IPv6 : AddressType.IPv4));
                    // real destination address
                    writer.Write(address.GetAddressBytes());
                    // port
                    writer.Write(IPAddress.HostToNetworkOrder((short)port));
                }

                // encryption
                Encrypt(buffer, 0, buffer.Length);

                // At last, send to proxy server.
                await _stream.WriteAsync(buffer, 0, buffer.Length);

            } else { // ClientProtocolType.Udp

                // for UDP, we decide whether we need to communicate with the server according to the actual situation.

                // For example like SOCKS5, we need to do handshake here before send any UDP datagram.

                // In this demo, we do not need to handshake, so just do the UDP connect,
                // which will allow us to send UDP data without having to specify the address and port of the proxy server. 

                _udpClient.Connect(_proxyServer);
            }
        }

        public async Task ConnectAsync(string host, int port) {
            // All here similar to ConnectAsync(IPAddress address, int port) above, but destination is a hostname rather than IP address.
            if (_type == ClientProtocolType.Tcp) {
                await _tcpClient.ConnectAsync(_proxyServer.Address, _proxyServer.Port);
                _stream = _tcpClient.GetStream();

                var domainBytes = Encoding.UTF8.GetBytes(host);
                var firstPacketLength = AddressTypeBytesCount + DomainLengthBytesCount + domainBytes.Length + PortBytesCount;
                var buffer = new byte[firstPacketLength];

                using (var stream = new MemoryStream(buffer))
                using (var writer = new BinaryWriter(stream)) {
                    writer.Write((byte)AddressType.Domain);
                    writer.Write((byte)domainBytes.Length);
                    writer.Write(domainBytes);
                    writer.Write(IPAddress.HostToNetworkOrder((short)port));
                }

                Encrypt(buffer, 0, buffer.Length);

                await _stream.WriteAsync(buffer, 0, buffer.Length);

            } else { // ClientProtocolType.Udp
                _udpClient.Connect(_proxyServer);
            }
        }

        public async Task WriteAsync(byte[] buffer, int offset, int count) {
            // This method is a hot path, so try not to allocate buffers within this method. 
            // We should define a buffer through class member if need.

            // encryption
            Encrypt(buffer, 0, buffer.Length);

            await _stream.WriteAsync(buffer, offset, count);
        }

        public async Task<int> ReadAsync(byte[] buffer, int offset, int count) {
            // This method is a hot path, so try not to allocate buffers within this method. 
            // You should define a buffer through class member if need.
            var bytesRead = await _stream.ReadAsync(buffer, offset, count);

            // decryption
            Decrypt(buffer, offset, bytesRead);

            return bytesRead;
        }

        public async Task<int> SendAsync(byte[] datagram, IPAddress address, int port) {
            // For performance reasons, Win2Socks does not synchronize calls to this method. 
            // In other words, there may be multiple threads calling this method at the same time.
            // If you access shared resources in this method, you should add the appropriate synchronization mechanism to avoid conflicts.
            // Usually will use a lock like this lock(xxx) { ... }, but in an asynchronous method, you can't use await keyword in a lock statement.
            // So we recommend you use class AsyncLock of AsyncEx(https://github.com/StephenCleary/AsyncEx) rather than the class SemaphoreSlim, since AsyncLock is specifically designed for asynchronous locks.

            // In this demo we never access shared resources so we don't need to lock.
            var packetLength = AddressTypeBytesCount +
                               (address.AddressFamily == AddressFamily.InterNetworkV6 ? IPv6AddressBytesCount : IPv4AddressBytesCount) +
                               PortBytesCount +
                               datagram.Length;

            var buffer = new byte[packetLength];
            var offset = 0;

            // address type
            buffer[0] = (byte)(address.AddressFamily == AddressFamily.InterNetworkV6 ? AddressType.IPv6 : AddressType.IPv4);
            offset += AddressTypeBytesCount;

            // real destination address
            var addressBytes = address.GetAddressBytes();
            Buffer.BlockCopy(addressBytes, 0, buffer, offset, addressBytes.Length);
            offset += addressBytes.Length;

            // port
            var portBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)port));
            Buffer.BlockCopy(portBytes, 0, buffer, offset, portBytes.Length);
            offset += portBytes.Length;

            // datagram
            Buffer.BlockCopy(datagram, 0, buffer, offset, datagram.Length);

            // encryption
            Encrypt(buffer, 0, buffer.Length);

            // send to proxy server, we do connect in ConnectAsync, so no need to specify the server address
            return await _udpClient.SendAsync(buffer, buffer.Length);
        }

        public async Task<UdpReceiveResult> ReceiveAsync() {
            // receive 
            var result = await _udpClient.ReceiveAsync();
            var buffer = result.Buffer;

            // decryption
            Decrypt(buffer, 0, buffer.Length);

            if (buffer.Length < AddressTypeBytesCount + IPv4AddressBytesCount + PortBytesCount)
                throw new InvalidDataException("Datagram is too short to contain the IPv4 address information.");

            // extract real remote address and datagram
            var offset = 0;

            // address type
            var addressType = (AddressType)buffer[0];
            offset += AddressTypeBytesCount;

            if (addressType == AddressType.IPv6 && buffer.Length < AddressTypeBytesCount + IPv6AddressBytesCount + PortBytesCount)
                throw new InvalidDataException("Datagram is too short to contain the IPv6 address information.");

            // address
            var addressBytes = new byte[addressType == AddressType.IPv6 ? IPv6AddressBytesCount : IPv4AddressBytesCount];
            Buffer.BlockCopy(buffer, offset, addressBytes, 0, addressBytes.Length);
            offset += addressBytes.Length;

            // port
            var port = IPAddress.NetworkToHostOrder(BitConverter.ToInt16(buffer, offset));
            offset += PortBytesCount;

            // datagram
            var datagram = new byte[buffer.Length - AddressTypeBytesCount - addressBytes.Length - PortBytesCount];
            Buffer.BlockCopy(buffer, offset, datagram, 0, buffer.Length - offset);

            // return to Win2Socks
            return new UdpReceiveResult(datagram, new IPEndPoint(new IPAddress(addressBytes), port));
        }

        private void Encrypt(byte[] buffer, int offset, int count) {
            // For demonstration purposes, we simply XOR or offset the data.
            if (_algorithm == Algorithm.Xor) {
                for (int i = 0; i < count; i++) {
                    buffer[offset + i] ^= _key;
                }

            } else { // Algorithm.Offset
                for (int i = 0; i < count; i++) {
                    buffer[offset + i] += _key;
                }
            }
        }

        private void Decrypt(byte[] buffer, int offset, int count) {
            // For demonstration purposes, we simply XOR or offset the data.
            if (_algorithm == Algorithm.Xor) {
                for (int i = 0; i < count; i++) {
                    buffer[offset + i] ^= _key;
                }

            } else { // Algorithm.Offset
                for (int i = 0; i < count; i++) {
                    buffer[offset + i] -= _key;
                }
            }
        }
    }

    internal enum AddressType {
        IPv4,
        IPv6,
        Domain
    }
}
