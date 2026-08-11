using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Classic {
    public class ToolsManager : SingletonBase<ToolsManager> {
        static object _locker = new object();

        private readonly List<IResampler> resamplers = new List<IResampler>();
        private readonly List<IWavtool> wavtools = new List<IWavtool>();
        private readonly Dictionary<string, IResampler> resamplersMap
            = new Dictionary<string, IResampler>();
        private readonly Dictionary<string, IWavtool> wavtoolsMap
            = new Dictionary<string, IWavtool>();

        public List<IResampler> Resamplers {
            get {
                lock (_locker) {
                    return resamplers.ToList();
                }
            }
        }

        public List<IWavtool> Wavtools {
            get {
                lock (_locker) {
                    return wavtools.ToList();
                }
            }
        }

        /// <summary>Checks whether a file is a native Linux ELF binary.</summary>
        private static bool IsElfBinary(string filePath) {
            try {
                using var fs = File.OpenRead(filePath);
                if (fs.Length < 4) return false;
                Span<byte> magic = stackalloc byte[4];
                fs.ReadExactly(magic);
                return magic[0] == 0x7F && magic[1] == 'E' && magic[2] == 'L' && magic[3] == 'F';
            } catch {
                return false;
            }
        }

        IResampler LoadResampler(string filePath, string basePath) {
            if (!File.Exists(filePath)) {
                return null;
            }
            string ext = Path.GetExtension(filePath).ToLower();
            // Windows executables: load if on Windows, or if Wine is available on Linux
            if ((OS.IsWindows() || !string.IsNullOrEmpty(Preferences.Default.WinePath)) && (ext == ".exe" || ext == ".bat")) {
                return new ExeResampler(filePath, basePath);
            }
            // Native Linux: shell scripts (.sh) or ELF binaries (no extension, or detected by magic)
            if (!OS.IsWindows() && (ext == ".sh" || string.IsNullOrEmpty(ext))) {
                if (string.IsNullOrEmpty(ext) && !IsElfBinary(filePath)) {
                    return null; // skip non-ELF extensionless files (README, etc.)
                }
                return new ExeResampler(filePath, basePath);
            }
            return null;
        }

        IWavtool LoadWavtool(string filePath, string basePath) {
            if (!File.Exists(filePath)) {
                return null;
            }
            string ext = Path.GetExtension(filePath).ToLower();
            // Windows executables: load if on Windows, or if Wine is available on Linux
            if ((OS.IsWindows() || !string.IsNullOrEmpty(Preferences.Default.WinePath)) && (ext == ".exe" || ext == ".bat")) {
                return new ExeWavtool(filePath, basePath);
            }
            // Native Linux: shell scripts (.sh) or ELF binaries (no extension, or detected by magic)
            if (!OS.IsWindows() && (ext == ".sh" || string.IsNullOrEmpty(ext))) {
                if (string.IsNullOrEmpty(ext) && !IsElfBinary(filePath)) {
                    return null; // skip non-ELF extensionless files
                }
                return new ExeWavtool(filePath, basePath);
            }
            return null;
        }

        public void Initialize() {
            lock (_locker) {
                SearchResamplers();
                SearchWavtools();
            }
        }

        public void SearchResamplers() {
            resamplers.Clear();
            resamplersMap.Clear();
            resamplers.Add(new WorldlineResampler());
            string basePath = PathManager.Inst.ResamplersPath;
            try {
                Directory.CreateDirectory(basePath);
                foreach (var file in Directory.EnumerateFiles(basePath, "*", new EnumerationOptions() {
                    RecurseSubdirectories = true
                })) {
                    var driver = LoadResampler(file, basePath);
                    if (driver != null) {
                        resamplers.Add(driver);
                    }
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to search resamplers.");
                resamplers.Clear();
            }
            foreach (var resampler in resamplers) {
                resamplersMap[resampler.ToString()] = resampler;
            }
        }

        public void SearchWavtools() {
            wavtools.Clear();
            wavtoolsMap.Clear();
            wavtools.Add(new SharpWavtool(true));
            wavtools.Add(new SharpWavtool(false));
            string basePath = PathManager.Inst.WavtoolsPath;
            try {
                Directory.CreateDirectory(basePath);
                foreach (var file in Directory.EnumerateFiles(basePath, "*", new EnumerationOptions() {
                    RecurseSubdirectories = true
                })) {
                    var driver = LoadWavtool(file, basePath);
                    if (driver != null) {
                        wavtools.Add(driver);
                    }
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to search resamplers.");
                wavtools.Clear();
            }
            foreach (var wavtool in wavtools) {
                wavtoolsMap[wavtool.ToString()] = wavtool;
            }
        }

        public IResampler GetResampler(string name) {
            lock (_locker) {
                if (name != null && resamplersMap.TryGetValue(name, out var resampler)) {
                    return resampler;
                } else {
                    return resamplersMap[WorldlineResampler.name];
                }
            }
        }

        public IWavtool GetWavtool(string name) {
            lock (_locker) {
                if (name != null && wavtoolsMap.TryGetValue(name, out var wavtool)) {
                    return wavtool;
                } else {
                    return wavtoolsMap[SharpWavtool.nameConvergence];
                }
            }
        }
    }
}
