using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core {
    public class GpuInfo {
        public int deviceId;
        public string description = "";

        override public string ToString() {
            return $"[{deviceId}] {description}";
        }
    }

    public enum OnnxRunnerChoice {
        Default,
        CPU,
        CPUForCoreML,
    }

    public class Onnx {
        private static Dictionary<int, OrtEpDevice> devices = initializeDevices();

        private static Dictionary<int, OrtEpDevice> initializeDevices() {
            var env = OrtEnv.Instance();
            var ortDevices = env.GetEpDevices();

            return ortDevices
                .Where(device => {
                    var ep = device.EpName.ToLower();
                    return ep.Contains("dml") || ep.Contains("cuda");
                })
                .Select((device, index) => new { index, device })
                .ToDictionary(x => x.index, x => x.device);
        }

        public static List<string> getRunnerOptions() {
            var options = new List<string> { "CPU" };
            if (OS.IsWindows()) {
                options.Add("DirectML");
            } else if (OS.IsMacOS()) {
                options.Add("CoreML");
            } else if (OS.IsAndroid()) {
                options.Add("NNAPI");
            } else {
                // Linux: offer CUDA if the provider is available
                try {
                    var providers = OrtEnv.Instance().GetAvailableProviders();
                    if (providers.Any(p => p.Contains("CUDA"))) {
                        options.Add("CUDA");
                    }
                } catch { }
            }
            return options;
        }

        public static List<GpuInfo> getGpuInfo() {
            if (OS.IsAndroid()) {
                return new List<GpuInfo>{new GpuInfo {
                    deviceId = 0, // eliminate exception of taking OnnxGpuOptions[0]
                }};
            }
            List<GpuInfo> gpuList = new List<GpuInfo>();
            var env = OrtEnv.Instance();
            var ortDevices = env.GetEpDevices();

            var i = 0;
            // Enumerate DML (Windows) and CUDA (Linux) GPU devices
            foreach (var device in ortDevices.Where(device => {
                var ep = device.EpName.ToLower();
                return ep.Contains("dml") || ep.Contains("cuda");
            })) {
                var description = "";
                foreach (var item in device.HardwareDevice.Metadata.Entries) {
                    if (item.Key.ToLower() == "description") {
                        description = $"{item.Value} ({device.HardwareDevice.Type})";
                        break;
                    }
                }
                if (string.IsNullOrEmpty(description)) { // fallback
                    description = $"{device.EpName} {device.HardwareDevice.Vendor} ({device.HardwareDevice.Type})";
                }
                devices[i] = device;
                gpuList.Add(new GpuInfo {
                    deviceId = i++,
                    description = description
                });
            }

            // If no devices found via GetEpDevices but CUDA provider is available,
            // create a generic CUDA GPU entry (CUDA EP doesn't always expose devices)
            if (gpuList.Count == 0 && !OS.IsMacOS() && !OS.IsAndroid()) {
                try {
                    var providers = env.GetAvailableProviders();
                    if (providers.Any(p => p.Contains("CUDA"))) {
                        gpuList.Add(new GpuInfo {
                            deviceId = 0,
                            description = "NVIDIA CUDA GPU"
                        });
                    }
                } catch { }
            }

            if (gpuList.Count == 0) {
                gpuList.Add(new GpuInfo {
                    deviceId = 0,
                });
            }
            return gpuList;
        }

        private static SessionOptions getOnnxSessionOptions(bool coremlEnableOnSubgraphs = false) {
            SessionOptions options = new SessionOptions();
            List<string> runnerOptions = getRunnerOptions();
            string runner = Preferences.Default.OnnxRunner;
            if (String.IsNullOrEmpty(runner)) {
                runner = runnerOptions[0];
            }
            if (!runnerOptions.Contains(runner)) {
                runner = "CPU";
            }
            switch (runner) {
                case "DirectML":
                    var d = devices[Preferences.Default.OnnxGpu];
                    options.AppendExecutionProvider(
                        OrtEnv.Instance(),
                        new List<OrtEpDevice> { d },
                        new Dictionary<string, string> { }
                     );
                    break;
                case "CUDA":
                    options.AppendExecutionProvider_CUDA(Preferences.Default.OnnxGpu);
                    break;
                case "CoreML":
                    // Note: MLProgram format has stricter validation and may fail with complex DiffSinger models
                    // that have topological sorting issues (e.g., variance_predictor with diffusion embeddings)
                    // so we always use NeuralNetwork format (default) as MLProgram fails with complex models.
                    options.AppendExecutionProvider("CoreML", new Dictionary<string, string> {
                        { "MLComputeUnits", "ALL" },
                        { "RequireStaticInputShapes", "1"},
                        { "ModelFormat", "NeuralNetwork"},
                        { "EnableOnSubgraphs", coremlEnableOnSubgraphs ? "1" : "0" }  // Disable subgraph processing to avoid complex control flow issues
                    });
                    break;
                case "NNAPI":
                    options.AppendExecutionProvider_Nnapi();
                    break;
            }
            return options;
        }

        public static InferenceSession getInferenceSession(byte[] model, OnnxRunnerChoice runnerChoice = OnnxRunnerChoice.Default) {
            if (runnerChoice == OnnxRunnerChoice.CPU ||
                (runnerChoice == OnnxRunnerChoice.CPUForCoreML && Preferences.Default.OnnxRunner == "CoreML")) {
                return new InferenceSession(model);
            } else {
                // Try with CoreML subgraphs enabled first, fallback to default if it fails
                if (OS.IsMacOS() && Preferences.Default.OnnxRunner == "CoreML") {
                    try {
                        return new InferenceSession(model, getOnnxSessionOptions(coremlEnableOnSubgraphs: true));
                    } catch (Exception e) {
                        Log.Warning(e, "Failed to create session with CoreML subgraphs enabled, falling back to default settings");
                    }
                }
                return new InferenceSession(model, getOnnxSessionOptions());
            }
        }

        public static InferenceSession getInferenceSession(string modelPath, OnnxRunnerChoice runnerChoice = OnnxRunnerChoice.Default) {
            if (runnerChoice == OnnxRunnerChoice.CPU ||
                (runnerChoice == OnnxRunnerChoice.CPUForCoreML && Preferences.Default.OnnxRunner == "CoreML")) {
                return new InferenceSession(modelPath);
            } else {
                // Try with CoreML subgraphs enabled first, fallback to default if it fails
                if (OS.IsMacOS() && Preferences.Default.OnnxRunner == "CoreML") {
                    try {
                        return new InferenceSession(modelPath, getOnnxSessionOptions(coremlEnableOnSubgraphs: true));
                    } catch (Exception e) {
                        Log.Warning(e, "Failed to create session with CoreML subgraphs enabled, falling back to default settings");
                    }
                }
                return new InferenceSession(modelPath, getOnnxSessionOptions());
            }
        }

        public static void VerifyInputNames(InferenceSession session, IEnumerable<NamedOnnxValue> inputs) {
            var sessionInputNames = session.InputNames.ToHashSet();
            var givenInputNames = inputs.Select(v => v.Name).ToHashSet();
            var missing = sessionInputNames
                .Except(givenInputNames)
                .OrderBy(s => s, StringComparer.InvariantCulture)
                .ToArray();
            if (missing.Length > 0) {
                throw new ArgumentException("Missing input(s) for the inference session: " + string.Join(", ", missing));
            }
            var unexpected = givenInputNames
                .Except(sessionInputNames)
                .OrderBy(s => s, StringComparer.InvariantCulture)
                .ToArray();
            if (unexpected.Length > 0) {
                throw new ArgumentException("Unexpected input(s) for the inference session: " + string.Join(", ", unexpected));
            }
        }
    }
}
