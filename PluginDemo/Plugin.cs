using System;
using System.Composition;
using System.Linq;
using Win2Socks.Plugin;

namespace PluginDemo {
    [Export(typeof(IPlugin))]
    public class Plugin : IPlugin {
        public string Name => "Plugin Demo";

        public Guid UniqueId { get; } = new Guid("5c3c11bb-42bb-433b-a9bc-32534d44f316");

        public bool SupportsTcp => true;

        public bool SupportsUdp => true;

        public string ArgumentsEditor => "PluginArgumentsEditor.exe";

        public IClientBuilder CreateClientBuilder(string arguments) {
            const string ArgumentAlgorithm = "Algorithm";
            const string ArgumentKey = "Key";

            var argumentDictionary = arguments.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                              .Select(nameValue => nameValue.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries))
                                              .Where(parts => parts.Length > 1)
                                              .ToDictionary(parts => parts[0], parts => parts[1]);

            if (!Enum.TryParse(argumentDictionary[ArgumentAlgorithm], ignoreCase: true, out Algorithm algorithm))
                throw new ArgumentException(ArgumentAlgorithm);

            var key = Convert.ToByte(argumentDictionary[ArgumentKey]);

            return new ClientBuilder(algorithm, key);
        }
    }

    public enum Algorithm {
        Xor,
        Offset
    }
}
