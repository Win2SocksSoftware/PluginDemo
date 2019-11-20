using System;
using System.Collections.Generic;
using System.Composition;
using System.Net;
using System.Text;
using Win2Socks.Plugin;

namespace PluginDemo {
    public class ClientBuilder : IClientBuilder {
        private readonly Algorithm _algorithm;
        private readonly byte _key;

        internal ClientBuilder(Algorithm algorithm, byte key) {
            _algorithm = algorithm;
            _key = key;
        }

        public IPluginClient Build(IPEndPoint proxyServer, ClientProtocolType protocolType) {
            return new PluginClient(proxyServer, protocolType, _algorithm, _key);
        }
    }
}
