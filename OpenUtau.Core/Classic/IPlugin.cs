using System.Threading.Tasks;

namespace OpenUtau.Classic {
    public interface IPlugin {
        string Encoding { get; }
        /// <summary>
        /// True when plugin.txt declares "notes=all": the plugin always receives
        /// every note of the part, instead of only the selected notes.
        /// </summary>
        bool AllNotes { get; }
        Task Run(string tempFile);
    }
}
