using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using PluginDemo;

namespace PluginArgumentsEditor {
    public partial class Form1 : Form {
        const string ArgumentAlgorithm = "Algorithm";
        const string ArgumentKey = "Key";

        public Form1() {
            InitializeComponent();

            AlgorithmComboBox.DataSource = Enum.GetValues(typeof(Algorithm));
        }

        private void Form1_Shown(object sender, EventArgs e) {
            // The follow method can be used to attach debugging to the editor process.
            // Debugger.Launch();

            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1) {
                var argumentsString = args[1];
                var argumentDictionary = argumentsString.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                                        .Select(nameValue => nameValue.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries))
                                                        .Where(parts => parts.Length > 1)
                                                        .ToDictionary(parts => parts[0], parts => parts[1]);

                AlgorithmComboBox.SelectedItem = Enum.Parse(typeof(Algorithm), argumentDictionary[ArgumentAlgorithm]);
                KeyNumericUpDown.Value = Convert.ToByte(argumentDictionary[ArgumentKey]);
            }
        }

        private void OkButton_Click(object sender, EventArgs e) {
            // Set the formatted arguments to clipboard, Win2Socks will read it from clipboard too.
            Clipboard.SetText($"{ArgumentAlgorithm}={AlgorithmComboBox.SelectedItem};{ArgumentKey}={KeyNumericUpDown.Value}");

            Close();
        }

        private void CancelButton_Click(object sender, EventArgs e) {
            Close();
        }
    }
}
